"""
Pre-release validation survey: find preserved content that the platform's current validation
rules would refuse, before a release makes that refusal live.

The September 2026 hardening made three kinds of value invalid that older builds accepted:
a METS path (``mets:FLocat/@xlink:href`` or ``premis:originalName``) containing a dot segment,
a backslash, or a percent-encoded separator; and - pending the decision on issue #287 - a slug
containing ``%``. A preserved METS carrying such a path would now fail to parse wherever the
platform reads it (``view=parsed-mets``, diffs, exports, pipeline runs), so the time to find one
is before cutting a release, not after deploying it. See ``docs/rfc-0001-landing-sequence.md``,
"Cutting a production release".

Read-only everywhere: GETs against the Preservation API, and optionally one SELECT against
Fedora's own database. Nothing is written except the CSV you ask for. No ledger.

Two populations, two instruments:

- **Repository slugs** live in Fedora's database, so they are checked with SQL rather than by
  loading Archival Groups (``--fedora-sql`` prints the queries; setting ``FEDORA_DB_DSN`` runs
  them). Any ``fedora_id`` containing ``%`` is the population issue #287 needs to see; dot-segment
  or backslash ids are checked as belt and braces - the platform could never create one, so
  expect zero rows there.
- **METS paths** exist only inside the documents, so those are fetched per Archival Group -
  the same deposits-query walk, pacing and ``--skip-created-by`` lever as the #188 survey, with
  ``--sample-skipped`` to spot-check whatever the lever skipped.

The path rule mirrors ``MetsParser.RejectDotSegments`` exactly (``UriPathX.IsDotSegment`` +
``UriPathX.ContainsEncodedSeparator``); ``tests.py`` pins the mirror with the same cases as the
.NET tests. If the C# rule changes, change :func:`refusal` to match, and its tests.
"""

import csv
import random
import time
from typing import Iterator
from urllib.parse import unquote

from logzero import logger
from lxml import etree

from app import api, settings, survey

#: The two SQL checks, against Fedora 6's own database (read-only credentials suffice). The first
#: is the population for the '%'-in-slugs decision (#287): slugs are stored percent-encoded, so a
#: slug containing '%' appears as '%25...' and is caught by the same LIKE. The second is belt and
#: braces; the platform could never have created such an id, so expect zero rows.
FEDORA_SQL = """\
-- 1. Every repository id containing a literal '%' (decision #287):
SELECT fedora_id FROM simple_search WHERE fedora_id LIKE '%!%%' ESCAPE '!' ORDER BY fedora_id;

-- 2. Dot-segment or backslash ids (expect zero rows):
SELECT fedora_id FROM simple_search
WHERE fedora_id ~ '(^|/)\\.\\.?(/|$)' OR strpos(fedora_id, chr(92)) > 0;
"""


def _decode(segment: str) -> str:
    """One percent-decode, matching .NET's Uri.UnescapeDataString: a malformed sequence
    (``%GG``, a trailing ``%2``) is left exactly as it is, never an error."""
    return unquote(segment, errors="replace")


def _is_dot_segment(segment: str) -> bool:
    return segment in (".", "..") or _decode(segment) in (".", "..")


def _has_encoded_separator(segment: str) -> bool:
    decoded = _decode(segment)
    return "/" in decoded or "\\" in decoded


def refusal(path: str) -> str | None:
    """
    Why MetsParser.RejectDotSegments would refuse this path, or None if it would not.

    Mirrors the C# exactly: a backslash anywhere; otherwise any '/'-separated segment that is a
    dot segment (raw or after one decode) or that decodes to contain a separator. Note what it
    deliberately does NOT refuse, just as the parser does not: absolute ``https:`` references
    (third-party METS carry them), empty segments, ``.hidden``, ``...``, or a lone ``%``.
    """
    if "\\" in path:
        return "backslash"
    for segment in path.split("/"):
        if _is_dot_segment(segment):
            return f"dot segment {segment!r}"
        if _has_encoded_separator(segment):
            return f"encoded separator in {segment!r}"
    return None


#: Explicit, not lxml defaults: preserved METS is third-party content, and this tool may run on
#: an operator machine against production. Element and attribute values are all the survey needs -
#: never entity expansion, DTD retrieval, or the network.
_XML_PARSER = etree.XMLParser(resolve_entities=False, load_dtd=False, no_network=True)


def _mets_paths(document: bytes) -> Iterator[tuple[str, str]]:
    """Every (kind, value) the parser applies the rule to: FLocat hrefs and premis originalNames,
    matched by local name so the PREMIS version and namespace prefixes do not matter."""
    root = etree.fromstring(document, parser=_XML_PARSER)
    for element in root.iter():
        if not isinstance(element.tag, str):
            # Comments and processing instructions: iter() yields those too, and their .tag is a
            # factory function that etree.QName() refuses with a ValueError.
            continue
        local = etree.QName(element).localname
        if local == "FLocat":
            for name, value in element.attrib.items():
                if etree.QName(name).localname == "href" and value:
                    yield "FLocat/@href", value
        elif local == "originalName" and element.text is not None:
            # RAW, no strip: the C# side passes XElement.Value untrimmed to RejectDotSegments,
            # and the mirror must apply the rule to exactly the same input.
            yield "premis:originalName", element.text


def _collect_groups(newest_first: bool, created_after: str | None) -> dict[str, set[str]]:
    """Walk the deposits query once: every Archival Group path -> the set of depositor slugs that
    have ever deposited against it. In memory, no ledger; ~100k paths is tens of megabytes."""
    groups: dict[str, set[str]] = {}
    for path, _created, creator, _preserved in survey.deposit_rows(newest_first, created_after):
        creators = groups.setdefault(path, set())
        if creator:
            creators.add(creator)
    return groups


#: survey.py stops after this many consecutive unreadable groups, and so does this survey:
#: the platform, not the data, is the problem by then.
_UNREAD_RUN_LIMIT = 3


def _check_group(path: str, findings: list[dict[str, str]],
                 problems: list[dict[str, str]]) -> str:
    """Fetch one Archival Group's METS and apply the rule. Returns a one-word outcome.

    ``findings`` is reproducible properties of the stored content (a refusable path, a METS that
    is not well-formed XML); ``problems`` is groups the survey could not check (transient reads).
    The distinction is the exit code's: content fails the gate, an incomplete check does not
    pretend to."""
    try:
        document = api.get_archival_group_mets(path)
    except api.ApiError as error:
        if error.status_code == 404:
            # A normal state, not a finding: an Archival Group can simply have no METS
            # (survey.py records the same 404 as NO_METS). Nothing for the rule to check.
            return "no_mets"
        logger.warning("%s: could not read METS (%s)", path, error)
        problems.append({"path": path, "kind": "unreadable", "value": "", "reason": str(error)})
        return "unreadable"
    try:
        hits = [(kind, value, refusal(value)) for kind, value in _mets_paths(document)]
    except etree.XMLSyntaxError as error:
        logger.warning("%s: METS is not well-formed XML (%s)", path, error)
        findings.append({"path": path, "kind": "unparseable", "value": "", "reason": str(error)})
        return "unparseable"
    found = False
    for kind, value, reason in hits:
        if reason:
            found = True
            logger.error("%s: %s %r would be refused: %s", path, kind, value, reason)
            findings.append({"path": path, "kind": kind, "value": value, "reason": reason})
    return "hit" if found else "clean"


def run(arguments) -> int:
    if arguments.fedora_sql:
        print(FEDORA_SQL)
        return 0

    findings: list[dict[str, str]] = []
    problems: list[dict[str, str]] = []
    slug_check_failed = False
    incomplete = False
    counts = {"clean": 0, "hit": 0, "no_mets": 0, "unreadable": 0, "unparseable": 0}

    # --- slugs, via Fedora's database, when a DSN is provided ---
    dsn = settings.FEDORA_DB_DSN
    if dsn:
        slug_check_failed = not _run_fedora_check(dsn, findings)
    else:
        logger.info("FEDORA_DB_DSN not set: skipping the slug check. Run it separately with the "
                    "SQL from --fedora-sql (read-only credentials suffice).")

    # --- METS paths, via the Preservation API ---
    # survey.skip_slugs, not a raw set: deposit_rows yields creator SLUGS, so the configured
    # values (documented as bare id or agent URI, and environment-specific in URI form) must be
    # slugified the same way or the skip lever silently matches nothing.
    skip_creators = survey.skip_slugs(arguments.skip_created_by or settings.SKIP_CREATED_BY)
    if arguments.paths:
        to_check = list(dict.fromkeys(arguments.paths))
        skipped: list[str] = []
    else:
        groups = _collect_groups(arguments.newest_first, arguments.created_after)
        logger.info("%s Archival Groups named by deposits", len(groups))
        paths = sorted(groups)
        if arguments.path_prefix:
            paths = [p for p in paths if p.startswith(arguments.path_prefix)]
        skipped = [p for p in paths
                   if skip_creators and groups[p] and groups[p] <= skip_creators]
        skipped_set = set(skipped)
        to_check = [p for p in paths if p not in skipped_set]
        if skipped:
            logger.info("%s skipped by depositor (%s); --sample-skipped spot-checks them",
                        len(skipped), ", ".join(sorted(skip_creators)))
        if arguments.sample_skipped and skipped:
            sample = random.sample(skipped, min(arguments.sample_skipped, len(skipped)))
            logger.info("sampling %s of the skipped groups", len(sample))
            to_check.extend(sample)
    if arguments.limit is not None:
        to_check = to_check[:arguments.limit]

    pause = arguments.pause if arguments.pause is not None else settings.SURVEY_PAUSE_SECONDS
    logger.info("checking the METS of %s Archival Group(s) against %s",
                len(to_check), settings.PRESERVATION_API)
    consecutive_unread = 0
    for index, path in enumerate(to_check, start=1):
        outcome = _check_group(path, findings, problems)
        counts[outcome] += 1
        consecutive_unread = consecutive_unread + 1 if outcome == "unreadable" else 0
        if consecutive_unread >= _UNREAD_RUN_LIMIT:
            # survey.py's circuit breaker, for the same reason: one unreadable group is that
            # group's problem, several in a row is the platform's. Failing on through the other
            # ~100k would take a full retry budget apiece and prove nothing about the content.
            logger.error("%s Archival Groups in a row could not be read, so the platform itself "
                         "is probably struggling - stopping the survey at %s of %s. Investigate, "
                         "then rerun.", consecutive_unread, index, len(to_check))
            incomplete = True
            break
        if index % 500 == 0:
            logger.info("...%s/%s (%s finding(s) so far)", index, len(to_check), len(findings))
        if pause and index < len(to_check):
            # Between groups, not after the last one - as survey.py paces.
            time.sleep(pause)

    # --- report ---
    if arguments.csv:
        with open(arguments.csv, "w", newline="", encoding="utf-8") as handle:
            writer = csv.DictWriter(handle, fieldnames=["path", "kind", "value", "reason"])
            writer.writeheader()
            writer.writerows(findings + problems)
        logger.info("findings written to %s", arguments.csv)

    logger.info("METS survey: %s clean, %s with refusable paths, %s with no METS, %s unreadable, "
                "%s unparseable%s skipped by depositor",
                counts["clean"], counts["hit"], counts["no_mets"], counts["unreadable"],
                counts["unparseable"], f", {len(skipped)}" if not arguments.paths else ", 0")
    if problems or incomplete or slug_check_failed:
        # Incomplete beats findings: exit 1 promises a verdict over the WHOLE population, and an
        # operator (or automation) must not resolve the found items and call the gate passed
        # while an unknown remainder went unchecked. The findings are still reported and in the
        # CSV; rerunning when the platform is healthy converts this into a 0 or a true 1.
        logger.error("The survey is INCOMPLETE (%s group(s) unreadable%s%s). %s finding(s) in "
                     "what WAS checked; rerun before treating the gate as answered.",
                     len(problems),
                     "; stopped early" if incomplete else "",
                     "; the slug check did not run" if slug_check_failed else "",
                     len(findings))
        return 2
    if findings:
        logger.error("%s finding(s): this deployment holds content the current validation rules "
                     "would refuse. Resolve (or consciously accept) before cutting a release.",
                     len(findings))
        return 1
    if not dsn:
        # The METS walk is clean, but this run never looked at the slug population, and the
        # clean message must not overclaim. Deliberately still exit 0: running the SQL
        # separately (a DBA with --fedora-sql's queries) is a supported split, and this run's
        # own remit completed clean. The release gate needs BOTH answers - see the README.
        logger.info("METS survey clean. Repository slugs were NOT checked by this run "
                    "(no FEDORA_DB_DSN): the release gate also needs the --fedora-sql queries "
                    "run against Fedora's database.")
        return 0
    logger.info("Nothing found: the current validation rules refuse nothing this deployment holds.")
    return 0


def _run_fedora_check(dsn: str, findings: list[dict[str, str]]) -> bool:
    """True only when the check RAN to completion; the caller reports an incomplete gate
    otherwise. What the check found is a separate question, answered through ``findings``."""
    try:
        import psycopg2  # optional; not in requirements.txt because only this check wants it
    except ImportError:
        logger.error("FEDORA_DB_DSN is set but psycopg2 is not installed. Either "
                     "`pip install psycopg2-binary`, or run the --fedora-sql queries yourself.")
        return False
    try:
        connection = psycopg2.connect(dsn)
    except psycopg2.Error as error:
        # A wrong password, unreachable host or missing grant must not take the METS survey -
        # the other half of the gate - down with it.
        logger.error("The Fedora slug check FAILED to run (%s). The METS survey continues; run "
                     "the slug check separately - --fedora-sql prints the SQL.", error)
        return False
    try:
        connection.set_session(readonly=True)
        with connection.cursor() as cursor:
            for statement in [s.strip() for s in FEDORA_SQL.split(";") if s.strip()]:
                cursor.execute(statement)
                for (fedora_id,) in cursor.fetchall():
                    logger.error("Fedora id needs a look: %s (decoded: %s)",
                                 fedora_id, _decode(fedora_id))
                    findings.append({"path": fedora_id, "kind": "fedora_id",
                                     "value": _decode(fedora_id), "reason": "slug check"})
    except psycopg2.Error as error:
        # e.g. a role without SELECT on simple_search: same treatment as a failed connect.
        logger.error("The Fedora slug check FAILED mid-run (%s). The METS survey continues; run "
                     "the slug check separately - --fedora-sql prints the SQL.", error)
        return False
    finally:
        connection.close()
    logger.info("Fedora database slug check complete.")
    return True
