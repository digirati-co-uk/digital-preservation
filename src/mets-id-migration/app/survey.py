"""
Finding the Archival Groups that need migrating, without changing anything.

Deposits are the index of candidates, not the answer: an Archival Group may have many deposits, and
what matters is the state of its METS now. So the walk collects distinct Archival Group paths from
the deposits query, then asks each group for its own current METS and decides from that.

What this misses, and why it is acceptable: an Archival Group whose deposit rows were hard-deleted
does not appear. The population that matters - documents this platform wrote - always had a deposit,
and the completeness cross-check is the Activity Stream, which records every create and update.
Run `survey --check-completeness` to compare the two counts before trusting the list.
"""

import random
import time
from typing import Iterator

from lxml import etree
from logzero import logger

from app import api, ids, settings
from app.ledger import CANDIDATE, CONFORMS, FOREIGN, NO_METS, SKIPPED, Ledger

#: This many unreadable Archival Groups IN A ROW means the platform is down, not the groups.
_UNREAD_RUN_LIMIT = 3

#: Where the resume cursor lives in the ledger's meta table: the Created timestamp of the last
#: deposit a full, oldest-first survey has completely dealt with. Everything at or before it is in
#: the ledger, so the next run asks the deposits query to start there instead of at the beginning.
_CURSOR = "deposits_cursor"

#: How often (in deposits) to persist the cursor while walking, so that a run killed mid-walk
#: still keeps most of its progress.
_CURSOR_EVERY = 200

#: How often (in deposit rows) to say something during the quiet stretches of a walk.
_PROGRESS_EVERY = 1000


def deposit_rows(
    newest_first: bool = False,
    created_after: str | None = None,
) -> Iterator[tuple[str, str | None, str | None, str | None]]:
    """
    Every deposit that names an Archival Group, as (path, created, creator slug, preserved) - in
    deposit order, one tuple per deposit, duplicates and all. The caller decides what a path means;
    this just turns pages into rows lazily, so a caller that stops early stops the paging too.

    Oldest first by default, because ascending order is stable under concurrent inserts: a deposit
    created during the walk sorts after everything already seen and cannot shift a later page
    underneath us. That is also what makes `created_after` sound as a resume point. `newest_first`
    gives up both guarantees and is for sampling only - on development the newest deposits are the
    ones the nightly Playwright run just made, which is usually exactly what you want.
    """
    page = 1
    total = None
    while True:
        result = api.list_deposits(page, settings.DEPOSIT_PAGE_SIZE, newest_first, created_after)
        deposits = result.get("deposits", [])
        if total is None:
            total = result.get("total")
            logger.info("Walking %s deposits, %s at a time, %s first%s",
                        total, settings.DEPOSIT_PAGE_SIZE,
                        "newest" if newest_first else "oldest",
                        f", resuming from {created_after}" if created_after else "")
        if not deposits:
            return
        for deposit in deposits:
            archival_group = deposit.get("archivalGroup")
            if not archival_group:
                continue  # a deposit that never named an Archival Group
            path = _path_under_root(archival_group)
            if path:
                creator = deposit.get("createdBy")
                yield (path, deposit.get("created"),
                       api.slug(creator) if creator else None, deposit.get("preserved"))
        page += 1


def _path_under_root(archival_group_uri: str) -> str | None:
    marker = "/repository/"
    index = archival_group_uri.find(marker)
    if index < 0:
        return None
    return archival_group_uri[index + len(marker):].strip("/") or None


def survey(
    ledger: Ledger,
    limit: int | None = None,
    rescan: bool = False,
    newest_first: bool = False,
    path_prefix: str | None = None,
    paths: list[str] | None = None,
    pause: float | None = None,
    skip_creators: list[str] | None = None,
) -> None:
    """
    Decide, for each Archival Group, whether it needs migrating - and write that to the ledger.

    Resumable three ways over. Paths already in the ledger are not re-examined unless `rescan`, and
    do not count against `limit`, so the same `--limit 100` command examines the next hundred each
    time. A full, oldest-first survey also keeps a cursor - the Created timestamp of the last
    deposit it has completely dealt with - so its next run asks the deposits query to start there
    instead of re-paging the whole history; the cursor only ever advances past deposits whose
    Archival Groups are actually in the ledger, is untouched by narrowed or newest-first runs, and
    is ignored (not lost) by `rescan`. And the ledger commits after every group, so Ctrl-C loses
    nothing. Read-only against the platform throughout.

    `skip_creators` is what makes 100,000 Archival Groups walkable: depositor identities (bare id
    or agent URI - the same agent differs only in URI base between environments) whose groups are
    recorded as skipped-by-depositor WITHOUT reading their METS. It is a heuristic about the
    depositor, not a verdict about the document, so `verify-skipped` exists to sample the skipped
    rows and check the heuristic held; and a group gains a real verdict the moment any deposit by
    anyone else turns up for it.

    Every narrowing exists for the same reason - trying this out without walking the whole of a
    deployment. `paths` skips the deposits query entirely; `path_prefix` keeps one container;
    `limit` stops after that many groups have actually been examined, and because the walk is lazy
    it stops the paging too. `pause` (see SURVEY_PAUSE_SECONDS) spaces out the reads, for running
    alongside people doing their work.
    """
    already_known = set() if rescan else ledger.known_paths()
    if pause is None:
        pause = settings.SURVEY_PAUSE_SECONDS
    skip = _skip_slugs(skip_creators if skip_creators is not None else settings.SKIP_CREATED_BY)
    # Skipped-by-depositor rows are the one kind of known row a walk still cares about: a deposit
    # by anyone else upgrades them to a real verdict.
    skipped_before = set() if rescan else {row["path"] for row in ledger.in_state(SKIPPED)}
    already_known -= skipped_before

    # The cursor is only sound for a full, oldest-first walk: a narrowed run leaves paths behind
    # that a later full run must still reach, and newest-first breaks the ordering the cursor
    # depends on. rescan re-examines from the beginning, so it reads no cursor and writes none.
    use_cursor = paths is None and path_prefix is None and not newest_first and not rescan
    cursor = ledger.get_meta(_CURSOR) if use_cursor else None
    high_water: str | None = None
    unpersisted = 0
    frozen = False

    def advance(created: str | None) -> None:
        """The cursor may pass this deposit: its Archival Group is accounted for in the ledger."""
        nonlocal high_water, unpersisted
        if frozen or not use_cursor or created is None:
            return
        high_water = created
        unpersisted += 1
        if unpersisted >= _CURSOR_EVERY:
            ledger.set_meta(_CURSOR, high_water)
            unpersisted = 0

    examined = 0
    skipped = 0
    known = 0
    unread = 0
    consecutive_unread = 0
    dealt_with: set[str] = set()

    def progress() -> None:
        """
        A heartbeat for the quiet stretches. Known and depositor-skipped rows are silent one by
        one - a resumed walk can pass thousands of them without a single new group - and a survey
        that says nothing for minutes looks hung rather than busy.
        """
        logger.info("...%s deposit rows in: %s for already-known groups, %s group(s) skipped by "
                    "depositor, %s examined this run", rows_seen, known, skipped, examined)

    rows_seen = 0
    candidates = (((path, None, None, None) for path in paths) if paths is not None
                  else deposit_rows(newest_first, cursor))
    try:
        for path, created, creator, preserved in candidates:
            rows_seen += 1
            if rows_seen % _PROGRESS_EVERY == 0:
                progress()
            if limit is not None and examined >= limit:
                logger.info("Stopping at the requested limit of %s", limit)
                break
            if path_prefix and not path.startswith(path_prefix):
                continue
            if path in dealt_with or path in already_known:
                # A later deposit can still teach us when the group was last preserved.
                known += 1
                ledger.note_preserved(path, preserved)
                advance(created)
                continue
            if creator is not None and creator in skip:
                if path not in skipped_before:
                    ledger.record(path, SKIPPED, agent="",
                                  note=f"every deposit so far created by '{creator}'")
                    logger.info("%s: SKIPPED - deposited by '%s', METS not read", path, creator)
                    skipped_before.add(path)
                    skipped += 1
                ledger.note_preserved(path, preserved)
                advance(created)
                continue
            if path in skipped_before:
                logger.info("%s: previously skipped by depositor, but this deposit is by '%s' - "
                            "surveying it properly", path, creator or "an unknown creator")
            if pause and examined:
                # Between groups, not after the last one - and not for skips, which read nothing.
                time.sleep(pause)
            examined += 1
            dealt_with.add(path)
            if _survey_one(ledger, path):
                ledger.note_preserved(path, preserved)
                consecutive_unread = 0
                advance(created)
            else:
                unread += 1
                consecutive_unread += 1
                # Nothing was recorded for this path, so the cursor must never pass it: stop
                # advancing for the rest of this run, however well the rest of it goes.
                frozen = True
                if consecutive_unread >= _UNREAD_RUN_LIMIT:
                    # One unreadable group is that group's problem; several in a row is the
                    # platform's. Failing on through the rest would take a retry-and-timeout
                    # apiece and record nothing, so stop where we are - the ledger is committed
                    # after every group, and rerunning the same command carries on from here.
                    logger.error(
                        "%s Archival Groups in a row could not be read, so the platform itself is "
                        "probably struggling - stopping the survey here. Nothing was recorded for "
                        "them; investigate, then rerun the same command to continue.",
                        consecutive_unread)
                    break
    finally:
        if use_cursor and high_water is not None and unpersisted:
            ledger.set_meta(_CURSOR, high_water)

    logger.info("Examined %s Archival Group(s) this run", examined)
    if skipped:
        logger.info("Skipped %s Archival Group(s) by depositor without reading their METS; "
                    "run `verify-skipped` to sample-check that heuristic", skipped)
    if unread:
        logger.warning("%s of them could not be read and are NOT in the ledger; "
                       "rerun the same command to try them again", unread)
    counts = ledger.counts()
    logger.info("Survey so far: %s", ", ".join(f"{state}={n}" for state, n in sorted(counts.items())))


def _skip_slugs(values: list[str]) -> frozenset[str]:
    """
    The depositor identities to skip, as bare ids however they were spelt. createdBy comes back as
    an agent URI whose base differs between environments, so matching the last segment is what lets
    one configuration value ('eprints-migration-app', or either environment's URI for it) work
    everywhere.
    """
    return frozenset(api.slug(value.strip()) for value in values if value and value.strip())


def verify_skipped(ledger: Ledger, sample_size: int) -> None:
    """
    Sample the skipped-by-depositor rows and survey them properly, so the heuristic is measured
    rather than trusted. Each sampled row gains its true verdict (overwriting SKIPPED); FOREIGN is
    the heuristic holding, and anything else is shouted about - it means groups deposited by the
    skipped identity can hold METS this migration is actually for, and the skip list should not be
    used on this deployment.
    """
    rows = ledger.in_state(SKIPPED)
    if not rows:
        logger.info("Nothing in state '%s' to verify.", SKIPPED)
        return
    chosen = random.sample(rows, min(sample_size, len(rows)))
    logger.info("Surveying %s of the %s skipped Archival Group(s) properly", len(chosen), len(rows))

    outcomes: dict[str, int] = {}
    for row in chosen:
        path = row["path"]
        state = ledger.get(path)["state"] if _survey_one(ledger, path) else "unread"
        outcomes[state] = outcomes.get(state, 0) + 1

    for state, count in sorted(outcomes.items()):
        logger.info("  %s: %s", state, count)
    surprises = {state: count for state, count in outcomes.items()
                 if state not in (FOREIGN, "unread")}
    if surprises:
        logger.warning(
            "The depositor heuristic mislabelled %s of %s sampled group(s) (%s) - groups deposited "
            "by that identity are not reliably foreign, so survey WITHOUT --skip-created-by here.",
            sum(surprises.values()), len(chosen),
            ", ".join(f"{state}={count}" for state, count in sorted(surprises.items())))
    else:
        logger.info("The heuristic held for every sampled group: all foreign%s.",
                    " (or unreadable this run)" if "unread" in outcomes else "")


def _survey_one(ledger: Ledger, path: str) -> bool:
    """
    Examine one Archival Group and record the verdict. Returns False when the group could not be
    read at all - in which case NOTHING is recorded, deliberately: the ledger holds verdicts about
    documents, and a timeout is not one. Recording it would make a transient failure permanent,
    because a rerun skips paths it already knows.
    """
    try:
        mets_xml = api.get_archival_group_mets(path)
    except api.ApiError as error:
        if error.status_code == 404:
            ledger.record(path, NO_METS, note="No METS in this Archival Group")
            logger.info("%s: NO METS - the Archival Group has no METS file", path)
            return True
        logger.warning("%s: could not read its METS, so it is NOT recorded - "
                       "rerunning the survey will try it again. %s", path, error)
        return False

    try:
        root = ids.parse(mets_xml)
    except etree.XMLSyntaxError as error:
        # Unlike a failed read, this IS a verdict about the document, so it is recorded.
        ledger.record(path, NO_METS, note=f"METS is not well-formed XML: {error}")
        logger.warning("%s: METS is not well-formed XML", path)
        return True

    # Every verdict is logged, not just the interesting one. "Examined 1, foreign=1" tells you the
    # answer without telling you the reason, and the reason is the whole point of the survey.
    agent = ids.creator_agent(root)
    if agent != ids.METS_CREATOR_AGENT:
        # EPrints, Archivematica and Goobi documents carry their own ID schemes, which are legal
        # NCNames. Not ours to renumber, and nothing to fix.
        ledger.record(path, FOREIGN, agent=agent or "")
        logger.info("%s: FOREIGN - METS created by %s, so not ours to migrate",
                    path, f"'{agent}'" if agent else "nobody (no CREATOR agent in the METS header)")
        return True

    invalid = ids.invalid_ids(root)
    if not invalid:
        ledger.record(path, CONFORMS, agent=agent)
        logger.info("%s: CONFORMS - ours, and every ID is already a legal xs:ID", path)
        return True

    offenders = ids.offending_characters(invalid)
    ledger.record(path, CANDIDATE, agent=agent, invalid_id_count=len(invalid),
                  invalid_id_sample=invalid[0], invalid_id_chars=offenders)
    logger.info("%s: CANDIDATE - %s invalid ID(s) [%s], e.g. %s",
                path, len(invalid), offenders, invalid[0])
    return True


def check_completeness(ledger: Ledger, partial: bool = False) -> None:
    """
    Answer a question the survey cannot answer about itself: did the deposits query show us
    everything?

    The survey finds Archival Groups through deposits, and an Archival Group whose deposit rows were
    hard-deleted therefore does not appear. The Activity Stream is the other record of the same
    population - it has an entry for every create and update - so comparing the two says whether
    anything was missed. It reads the stream and nothing else; the survey itself is unaffected by
    whether this runs.

    The two will not match exactly, and the mismatches are expected rather than alarming: the stream
    begins at a seeded backstop event so it does not reach the earliest Archival Groups, and
    suppressed events (the migration's own) are not published. What matters is the other direction -
    something in the stream that the survey never saw, which means the list is not the whole job.
    """
    if partial:
        # Comparing a deliberately partial survey against the whole stream would report thousands
        # of "missing" Archival Groups and mean nothing at all.
        logger.warning(
            "Skipping the completeness check: this survey was narrowed by --limit, --path or "
            "--path-prefix, so it is not meant to have seen everything. Run `survey "
            "--check-completeness` on its own once a full survey has finished.")
        return

    from_deposits = len(ledger.known_paths())
    from_stream = set()
    page_number = 1
    while True:
        page = api.activity_page(page_number)
        for activity in page.get("orderedItems", []):
            object_id = activity.get("object", {}).get("id")
            if object_id:
                from_stream.add(_path_under_root(object_id))
        if not page.get("next"):
            break
        page_number += 1

    logger.info("Archival Groups known from deposits: %s", from_deposits)
    logger.info("Archival Groups mentioned in the activity stream: %s", len(from_stream))
    missing = {path for path in from_stream if path} - ledger.known_paths()
    if missing:
        logger.warning("%s Archival Group(s) appear in the stream but not in the survey, e.g. %s",
                       len(missing), sorted(missing)[:5])
    else:
        logger.info("Every Archival Group in the stream was found by the survey")
