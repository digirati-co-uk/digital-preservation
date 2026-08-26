"""
Migrating one Archival Group, and the gates that stop it doing anything else.

The shape of a migration:

  1. create a deposit against the Archival Group, WITHOUT export - only the METS comes down;
  2. normalise its IDs through the Preservation API;
  3. if that changed nothing, throw the deposit away: an Archival Group must not gain a version
     for a document that did not change;
  4. generate the diff import job and REFUSE unless it is exactly one binary to patch - the METS;
  5. execute it, with the Activity Stream event suppressed;
  6. verify the preserved document, and only then call it done.

Step 4 is the one that matters most. A deposit that holds only the METS gets every other file from
the METS itself, so a file that the Archival Group holds but the METS does not mention would be
listed for DELETION rather than failing the diff. That single assertion is what closes it.
"""

import time

from logzero import logger
from lxml import etree

from app import api, ids, settings
from app.ledger import DONE, FAILED, NO_CHANGE, Ledger


class MigrationRefused(RuntimeError):
    """The migration stopped on purpose, before changing anything preserved."""


def migrate_all(ledger: Ledger, candidates, dry_run: bool) -> None:
    for index, row in enumerate(candidates, start=1):
        path = row["path"]
        logger.info("[%s/%s] %s", index, len(candidates), path)
        try:
            migrate_one(ledger, path, dry_run)
        except (MigrationRefused, api.ApiError, TimeoutError) as error:
            logger.error("%s: %s", path, error)
            ledger.record(path, FAILED, note=str(error))
        if settings.PAUSE_SECONDS:
            time.sleep(settings.PAUSE_SECONDS)


def migrate_one(ledger: Ledger, path: str, dry_run: bool) -> None:
    before = _fingerprint(path)

    deposit = api.create_deposit(path)
    deposit_id = api.slug(deposit["id"])
    logger.info("  deposit %s", deposit_id)

    # Until an import job has been submitted, the deposit is just a staging copy and is deleted on
    # every exit. From submission onward it is only deleted after a clean, verified success: on a
    # timeout the job may still be running - deleting its staging area could break it mid-copy, or
    # destroy the evidence of what happened if it completed after we stopped watching - and on any
    # failure after submission the deposit IS the evidence.
    disposable = True
    try:
        report = api.normalise_mets_ids(deposit_id, deposit.get("metsETag"))
        if not report.get("changed"):
            warnings = report.get("warnings", [])
            if warnings:
                # The survey said this document has invalid IDs and the platform DECLINED to
                # rewrite them - a collision, or an ID it could not make legal. Settling this as
                # no-change would report a document that still violates xs:ID as dealt with, which
                # is the failure that looks like success. It needs a person.
                for warning in warnings:
                    logger.warning("  %s", warning)
                raise MigrationRefused(
                    f"normalise declined to rewrite: {len(warnings)} warning(s), "
                    f"e.g. {warnings[0]}")
            logger.info("  nothing to normalise; leaving the Archival Group untouched")
            ledger.record(path, NO_CHANGE, deposit=deposit_id,
                          note="normalise reported no change")
            return

        logger.info("  %s ID(s) and %s reference(s) rewritten",
                    report["idsRewritten"], report["referencesRewritten"])
        for warning in report.get("warnings", []):
            logger.warning("  %s", warning)

        import_job = api.get_diff_import_job(deposit_id)
        _refuse_unless_mets_only(import_job)

        if dry_run:
            logger.info("  dry run: the diff is a single METS patch, as required; not preserving")
            ledger.record(path, ledger.get(path)["state"], deposit=deposit_id,
                          ids_rewritten=report["idsRewritten"],
                          refs_rewritten=report["referencesRewritten"],
                          note="dry run: diff verified as METS-only")
            return

        import_job["suppressActivityStreamEvent"] = settings.SUPPRESS_ACTIVITY_STREAM_EVENT
        disposable = False
        result = api.await_import_job(deposit_id, api.execute_import_job(deposit_id, import_job))
        if result["status"] != "completed":
            raise MigrationRefused(
                f"Import job finished as '{result['status']}': {result.get('errors')}")

        after = _fingerprint(path)
        _verify(before, after)

        logger.info("  preserved: %s -> %s", result.get("sourceVersion"), result.get("newVersion"))
        ledger.record(path, DONE, deposit=deposit_id,
                      ids_rewritten=report["idsRewritten"],
                      refs_rewritten=report["referencesRewritten"],
                      rewrites=report.get("rewrites", []),
                      warnings=report.get("warnings", []),
                      from_version=_version_of(result, "sourceVersion"),
                      to_version=_version_of(result, "newVersion"),
                      note=None)
        disposable = True
    finally:
        if disposable:
            api.delete_deposit(deposit_id)
        else:
            logger.warning(
                "  keeping deposit %s: the import job's outcome is unknown or failed, and the "
                "deposit is the evidence. Investigate it before deleting it by hand.", deposit_id)


# The platform's own empty scaffold folders, relative to the Archival Group. Creating a deposit
# against a group preserved before they existed (LPII-9) writes them into its METS, so for such a
# group the diff is the METS patch plus these two containers and can never be a pure METS patch.
# They hold nothing and nothing is derived from them: recording, not content. The platform's own
# gate (ImportJobsController.SuppressedButNotMetsOnly) makes the same allowance, and no other.
SCAFFOLD_FOLDERS = frozenset({"metadata", "metadata/ad-hoc"})


def _refuse_unless_mets_only(import_job: dict) -> None:
    """
    The gate. A METS ID migration changes one file, so the import job must contain one binary to
    patch and nothing else at all - bar the platform's own scaffold folders, see SCAFFOLD_FOLDERS.
    Anything more means the deposit and the Archival Group disagree about what the object holds,
    and this tool is not the thing that should resolve that.
    """
    patches = import_job.get("binariesToPatch", [])
    if len(patches) != 1:
        raise MigrationRefused(
            f"Expected exactly one binary to patch, found {len(patches)}: "
            f"{[binary.get('id') for binary in patches]}")

    patched = api.slug(patches[0]["id"])
    if not _is_mets_file(patched):
        raise MigrationRefused(f"The binary to patch is '{patched}', which is not a METS file")

    for key in ("binariesToAdd", "binariesToDelete", "binariesToRename",
                "containersToDelete", "containersToRename"):
        entries = import_job.get(key, [])
        if entries:
            raise MigrationRefused(
                f"{key} is not empty ({len(entries)} entries, e.g. "
                f"{entries[0].get('id')}); refusing to preserve")

    archival_group = (import_job.get("archivalGroup") or "").rstrip("/") + "/"
    for container in import_job.get("containersToAdd", []):
        container_id = container.get("id") or ""
        relative = container_id[len(archival_group):].rstrip("/") \
            if archival_group != "/" and container_id.startswith(archival_group) else None
        if relative not in SCAFFOLD_FOLDERS:
            raise MigrationRefused(
                f"containersToAdd holds {container_id}, which is not one of the platform's own "
                f"scaffold folders ({', '.join(sorted(SCAFFOLD_FOLDERS))}); refusing to preserve")
        logger.info("  the diff also adds the empty scaffold folder %s; allowed", relative)


def _is_mets_file(slug: str) -> bool:
    """The same test as the platform's MetsUtils.IsMetsFile, in its permissive form: third-party
    deposits arrive with names like METS.<uuid>.xml, so the standard name is not required."""
    name = slug.lower()
    return name.endswith(".xml") and "mets" in name


def _fingerprint(path: str) -> dict[str, str]:
    """Path to digest for every file the Archival Group's METS lists."""
    try:
        return ids.file_digests(ids.parse(api.get_archival_group_mets(path)))
    except (api.ApiError, etree.XMLSyntaxError) as error:
        raise MigrationRefused(f"Could not read the Archival Group's METS: {error}") from error


def _verify(before: dict[str, str], after: dict[str, str]) -> None:
    """
    The migration renames identifiers. If it did that and only that, the set of files and their
    digests is identical on both sides - which is also, conveniently, exactly what would change if
    it had done something worse.
    """
    if before != after:
        missing = sorted(set(before) - set(after))
        added = sorted(set(after) - set(before))
        changed = sorted(k for k in before.keys() & after.keys() if before[k] != after[k])
        raise MigrationRefused(
            "The preserved METS no longer lists the same files: "
            f"missing={missing[:5]} added={added[:5]} digests changed={changed[:5]}")


def _version_of(result: dict, key: str) -> str | None:
    value = result.get(key)
    if isinstance(value, dict):
        return value.get("ocflVersion")
    return value


def verify_migrated(ledger: Ledger, limit: int | None = None) -> None:
    """
    Re-read every Archival Group the ledger says was migrated and check the preserved document
    really does conform. Separate from the migration so it can be run again later, over the whole
    campaign, without touching anything.
    """
    rows = ledger.in_state(DONE, limit)
    logger.info("Verifying %s migrated Archival Group(s)", len(rows))
    for row in rows:
        path = row["path"]
        try:
            mets_xml = api.get_archival_group_mets(path)
        except api.ApiError as error:
            # A timeout or a server blip says nothing about the migration, and overwriting DONE
            # would permanently remove a correctly migrated group from the verify queue - the same
            # rule as the survey's: a failed read is not a verdict. Leave the row; rerun verify.
            logger.warning("%s: could not re-read METS, leaving it DONE for a rerun - %s",
                           path, error)
            continue
        try:
            invalid = ids.invalid_ids(ids.parse(mets_xml))
        except etree.XMLSyntaxError as error:
            # Unlike a failed read, a preserved METS that does not parse IS a verdict.
            logger.error("%s: preserved METS is not well-formed XML: %s", path, error)
            ledger.record(path, FAILED, note=f"verification: METS not well-formed: {error}")
            continue
        if invalid:
            logger.error("%s: still has %s invalid ID(s), e.g. %s", path, len(invalid), invalid[0])
            ledger.record(path, FAILED,
                          note=f"verification found {len(invalid)} invalid ID(s), e.g. {invalid[0]}")
        else:
            logger.info("%s: conforms", path)
