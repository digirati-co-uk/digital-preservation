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

from typing import Iterator

from lxml import etree
from logzero import logger

from app import api, ids, settings
from app.ledger import CANDIDATE, CONFORMS, FOREIGN, NO_METS, Ledger


def archival_group_paths(newest_first: bool = False) -> Iterator[str]:
    """
    Every distinct Archival Group path any deposit has ever pointed at.

    A generator rather than a list, so that a caller which stops early stops the paging too. That is
    what makes `--limit` cheap enough to use against development, where there are far more deposits
    than anyone wants to walk to look at five Archival Groups.

    Oldest first by default, because ascending order is stable under concurrent inserts: a deposit
    created during the walk sorts after everything already seen and cannot shift a later page
    underneath us. `newest_first` gives up that guarantee, and is for sampling only - on development
    the newest deposits are the ones the nightly Playwright run just made, which is usually exactly
    what you want to test against.
    """
    seen: set[str] = set()
    page = 1
    total = None
    while True:
        result = api.list_deposits(page, settings.DEPOSIT_PAGE_SIZE, newest_first)
        deposits = result.get("deposits", [])
        if total is None:
            total = result.get("total")
            logger.info("Walking %s deposits, %s at a time, %s first",
                        total, settings.DEPOSIT_PAGE_SIZE, "newest" if newest_first else "oldest")
        if not deposits:
            return
        for deposit in deposits:
            archival_group = deposit.get("archivalGroup")
            if not archival_group:
                continue  # a deposit that never named an Archival Group
            path = _path_under_root(archival_group)
            if path and path not in seen:
                seen.add(path)
                yield path
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
) -> None:
    """
    Decide, for each Archival Group, whether it needs migrating - and write that to the ledger.

    Resumable: paths already in the ledger are skipped unless `rescan`. Read-only against the
    platform; the only thing that changes is the ledger.

    Every narrowing here exists for the same reason - trying this against development without
    walking the whole of it. `paths` skips the deposits query entirely and examines exactly what it
    is given; `path_prefix` keeps only Archival Groups under one container; `limit` stops after that
    many, and because the walk is lazy it stops the paging too.
    """
    already_known = set() if rescan else ledger.known_paths()
    examined = 0

    candidates = iter(paths) if paths is not None else archival_group_paths(newest_first)
    for path in candidates:
        if path_prefix and not path.startswith(path_prefix):
            continue
        if path in already_known:
            continue
        if limit is not None and examined >= limit:
            logger.info("Stopping at the requested limit of %s", limit)
            break
        examined += 1
        _survey_one(ledger, path)

    logger.info("Examined %s Archival Group(s) this run", examined)
    counts = ledger.counts()
    logger.info("Survey so far: %s", ", ".join(f"{state}={n}" for state, n in sorted(counts.items())))


def _survey_one(ledger: Ledger, path: str) -> None:
    try:
        mets_xml = api.get_archival_group_mets(path)
    except api.ApiError as error:
        if error.status_code == 404:
            ledger.record(path, NO_METS, note="No METS in this Archival Group")
            return
        ledger.record(path, NO_METS, note=str(error))
        logger.warning("%s: %s", path, error)
        return

    try:
        root = ids.parse(mets_xml)
    except etree.XMLSyntaxError as error:
        ledger.record(path, NO_METS, note=f"METS is not well-formed XML: {error}")
        logger.warning("%s: METS is not well-formed XML", path)
        return

    agent = ids.creator_agent(root)
    if agent != ids.METS_CREATOR_AGENT:
        # EPrints, Archivematica and Goobi documents carry their own ID schemes, which are legal
        # NCNames. Not ours to renumber, and nothing to fix.
        ledger.record(path, FOREIGN, agent=agent or "")
        return

    invalid = ids.invalid_ids(root)
    if not invalid:
        ledger.record(path, CONFORMS, agent=agent)
        return

    offenders = ids.offending_characters(invalid)
    ledger.record(path, CANDIDATE, agent=agent, invalid_id_count=len(invalid),
                  invalid_id_sample=invalid[0], invalid_id_chars=offenders)
    logger.info("%s: %s invalid ID(s) [%s], e.g. %s", path, len(invalid), offenders, invalid[0])


def check_completeness(ledger: Ledger) -> None:
    """
    Compare the number of distinct Archival Groups the survey found against the number the Activity
    Stream has ever mentioned. They will not match exactly - the stream starts at a seeded backstop
    event, and suppressed events are not published - but a large shortfall means the deposits query
    is not seeing everything, and the list should not be trusted until that is understood.
    """
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
