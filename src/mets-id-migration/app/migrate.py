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

    try:
        report = api.normalise_mets_ids(deposit_id)
        if not report.get("changed"):
            # The survey said this document had invalid IDs and the platform says it has nothing to
            # rewrite. Not an error - the two disagree only where the platform declined a rewrite,
            # which it reports as a warning - but it is not a migration either, and it must not be
            # preserved.
            logger.info("  nothing to normalise; leaving the Archival Group untouched")
            ledger.record(path, NO_CHANGE, deposit=deposit_id,
                          warnings=report.get("warnings", []),
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
    finally:
        api.delete_deposit(deposit_id)


def _refuse_unless_mets_only(import_job: dict) -> None:
    """
    The gate. A METS ID migration changes one file, so the import job must contain one binary to
    patch and nothing else at all. Anything more means the deposit and the Archival Group disagree
    about what the object holds, and this tool is not the thing that should resolve that.
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
                "containersToAdd", "containersToDelete", "containersToRename"):
        entries = import_job.get(key, [])
        if entries:
            raise MigrationRefused(
                f"{key} is not empty ({len(entries)} entries, e.g. "
                f"{entries[0].get('id')}); refusing to preserve")


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
            invalid = ids.invalid_ids(ids.parse(api.get_archival_group_mets(path)))
        except (api.ApiError, etree.XMLSyntaxError) as error:
            logger.error("%s: could not re-read METS: %s", path, error)
            ledger.record(path, FAILED, note=f"verification could not read METS: {error}")
            continue
        if invalid:
            logger.error("%s: still has %s invalid ID(s), e.g. %s", path, len(invalid), invalid[0])
            ledger.record(path, FAILED,
                          note=f"verification found {len(invalid)} invalid ID(s), e.g. {invalid[0]}")
        else:
            logger.info("%s: conforms", path)
