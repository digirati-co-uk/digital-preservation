#!/usr/bin/env python
"""
Bulk migration of preserved METS documents to legal xs:ID values (issue #188 step 3).

    python mets_id_migration.py survey                 # find what needs doing; changes nothing
    python mets_id_migration.py survey --limit 5 --newest-first    # or just look at a few
    python mets_id_migration.py survey --pause 0.5     # ...gently, on a system in use
    python mets_id_migration.py report                 # what the ledger holds
    python mets_id_migration.py list                   # the actual list, for doing by hand
    python mets_id_migration.py migrate --dry-run      # rehearse: everything but the preserve
    python mets_id_migration.py migrate --limit 1      # do one
    python mets_id_migration.py verify                 # re-check what was migrated

`survey` and `list` change nothing anywhere - they answer "how big is this?", which is the question
that decides whether the bulk migration is needed at all. If the list is short, the same job can be
done by hand from the UI; the README says how.

Read `report` between every step. There is no command that does the whole thing unattended, and
that is deliberate: this creates a new OCFL version of preserved material.
"""

import argparse
import csv
import sys

import logzero
from logzero import logger

from app import api, migrate, settings, survey
from app.ledger import CANDIDATE, Ledger, WrongDeployment


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--ledger", default=settings.LEDGER_PATH,
                        help="SQLite file recording what was found and done (default: %(default)s)")
    parser.add_argument("--log-file", help="also write the log here, which is what you want for a "
                                           "run you may need to explain afterwards")
    commands = parser.add_subparsers(dest="command", required=True)

    survey_command = commands.add_parser(
        "survey", help="find the Archival Groups that need migrating. Read-only.")
    survey_command.add_argument("--limit", type=int, help="examine at most this many new groups")
    survey_command.add_argument("--rescan", action="store_true",
                                help="re-examine groups already in the ledger. Without it, groups "
                                     "already recorded are skipped without counting against "
                                     "--limit, so rerunning the same command examines the next "
                                     "batch rather than the same one")
    survey_command.add_argument("--pause", type=float, metavar="SECONDS",
                                help="wait this long between Archival Groups. Reading one costs "
                                     "the platform several Fedora requests, and the walk is "
                                     "otherwise as fast as the API will answer; pace it when other "
                                     "people are using the system (default: SURVEY_PAUSE_SECONDS, "
                                     f"currently {settings.SURVEY_PAUSE_SECONDS})")
    survey_command.add_argument("--check-completeness", action="store_true",
                                help="afterwards, cross-check the surveyed Archival Groups against "
                                     "the Activity Stream, which is the other record of the same "
                                     "population. Answers 'did the deposits query show us "
                                     "everything?'. Only meaningful after a FULL survey")
    # Three ways to look at part of a deployment rather than all of it. Use them freely: the survey
    # writes only to the ledger, so a partial one costs nothing and can be widened later.
    survey_command.add_argument("--newest-first", action="store_true",
                                help="walk the newest deposits first - for sampling a live system "
                                     "(on dev, what the nightly Playwright run just made). Paging "
                                     "is only stable oldest-first, so do not use this for a "
                                     "campaign that has to see everything")
    survey_command.add_argument("--path-prefix",
                                help="only Archival Groups whose path starts with this, e.g. cc-test/")
    survey_command.add_argument("--path", action="append", dest="paths", metavar="PATH",
                                help="examine exactly this Archival Group and skip the deposits "
                                     "query entirely. Repeatable")

    commands.add_parser("report", help="summarise the ledger")

    list_command = commands.add_parser(
        "list", help="print the Archival Groups that need migrating, one per line. Read-only.")
    list_command.add_argument("--csv", help="write the list here instead of logging it")
    list_command.add_argument("--state", default=CANDIDATE,
                              help="which ledger state to list (default: %(default)s)")
    list_command.add_argument("--path-prefix",
                              help="only Archival Groups whose path starts with this")

    migrate_command = commands.add_parser(
        "migrate", help="migrate the Archival Groups the survey marked as candidates")
    migrate_command.add_argument("--limit", type=int, help="migrate at most this many")
    migrate_command.add_argument("--path-prefix",
                                 help="only Archival Groups whose path starts with this")
    migrate_command.add_argument(
        "--dry-run", action="store_true",
        help="do everything up to and including checking the diff, then stop without preserving")

    verify_command = commands.add_parser(
        "verify", help="re-read migrated Archival Groups and check they conform. Read-only.")
    verify_command.add_argument("--limit", type=int)

    arguments = parser.parse_args()
    if arguments.log_file:
        logzero.logfile(arguments.log_file)

    problems = settings.check()
    if problems:
        for problem in problems:
            logger.error(problem)
        return 2

    try:
        ledger = Ledger(arguments.ledger, settings.PRESERVATION_API)
    except WrongDeployment as wrong:
        logger.error(str(wrong))
        return 2

    try:
        return _run(arguments, ledger)
    finally:
        ledger.close()


def _run(arguments, ledger: Ledger) -> int:
    if arguments.command == "survey":
        survey.survey(ledger, limit=arguments.limit, rescan=arguments.rescan,
                      newest_first=arguments.newest_first, path_prefix=arguments.path_prefix,
                      paths=arguments.paths, pause=arguments.pause)
        if arguments.check_completeness:
            narrowed = bool(arguments.limit or arguments.paths or arguments.path_prefix)
            survey.check_completeness(ledger, partial=narrowed)
        return 0

    if arguments.command == "report":
        _report(ledger)
        return 0

    if arguments.command == "list":
        _list(ledger, arguments.state, arguments.csv, arguments.path_prefix)
        return 0

    if arguments.command == "migrate":
        candidates = ledger.in_state(CANDIDATE)
        if arguments.path_prefix:
            candidates = [row for row in candidates if row["path"].startswith(arguments.path_prefix)]
        if arguments.limit is not None:
            candidates = candidates[:arguments.limit]
        if not candidates:
            logger.info("Nothing to migrate. Run `survey` first, or `report` to see the state.")
            return 0
        logger.info("%s Archival Group(s) to migrate against %s%s",
                    len(candidates), settings.PRESERVATION_API,
                    " (DRY RUN - nothing will be preserved)" if arguments.dry_run else "")
        migrate.migrate_all(ledger, candidates, arguments.dry_run)
        _report(ledger)
        return 0

    if arguments.command == "verify":
        migrate.verify_migrated(ledger, arguments.limit)
        return 0

    return 2


def _list(ledger: Ledger, state: str, csv_path: str | None, path_prefix: str | None = None) -> None:
    """
    The list of Archival Groups that need migrating, and nothing else.

    The survey has already established this without changing anything. If the list is short - and on
    production it is expected to be, since almost everything there is EPrints material that was never
    affected - there is no need to run the bulk migration at all: someone can work through it in the
    UI, using "Normalise METS IDs" on the Actions menu of a deposit made against each group. See the
    README for that route.
    """
    rows = ledger.in_state(state)
    if path_prefix:
        rows = [row for row in rows if row["path"].startswith(path_prefix)]
    if not rows:
        logger.info("Nothing in state '%s'. Run `survey` first.", state)
        return

    if csv_path:
        with open(csv_path, "w", newline="", encoding="utf-8") as handle:
            writer = csv.writer(handle)
            writer.writerow(["archivalGroupPath", "invalidIdCount", "invalidIdChars",
                             "invalidIdSample", "ui"])
            for row in rows:
                writer.writerow([row["path"], row["invalid_id_count"], row["invalid_id_chars"],
                                 row["invalid_id_sample"], _ui_link(row["path"])])
        logger.info("Wrote %s row(s) to %s", len(rows), csv_path)
        return

    logger.info("%s Archival Group(s) in state '%s':", len(rows), state)
    for row in rows:
        if row["invalid_id_count"]:
            detail = (f"  ({row['invalid_id_count']} invalid ID(s) [{row['invalid_id_chars']}], "
                      f"e.g. {row['invalid_id_sample']})")
        elif row["state"] == "foreign":
            # The agent is the whole reason the row is in this state, so say it.
            detail = f"  (METS by {row['agent'] or 'no CREATOR agent'})"
        else:
            detail = ""
        link = f"  {_ui_link(row['path'])}" if settings.UI_BASE_URL else ""
        logger.info("  %s%s%s", row["path"], detail, link)

    if state == CANDIDATE and len(rows) <= 25:
        logger.info("That is few enough to do by hand from the UI if you would rather - "
                    "see 'Doing it by hand' in the README.")


def _report_offending_characters(ledger: Ledger) -> None:
    """
    Which characters actually make IDs illegal across the surveyed corpus, and how many Archival
    Groups have each.

    Almost all of it will be `/`. A SPACE is the one worth watching: an ID containing one arrives
    from the XmlSerializer already split into several tokens, which is the only reason the joined
    tier in IdRefs exists. If no document in the corpus has one, that tier can be retired once the
    migration is done; if any does, it cannot be retired until they are all migrated.
    """
    counts: dict[str, int] = {}
    for row in ledger.in_state(CANDIDATE):
        for char in (row["invalid_id_chars"] or "").split():
            counts[char] = counts.get(char, 0) + 1
    if not counts:
        return
    logger.info("  characters making IDs illegal, by Archival Group:")
    for char, count in sorted(counts.items(), key=lambda pair: -pair[1]):
        logger.info("    %-6s %s", char, count)


def _ui_link(path: str) -> str:
    """Where the Archival Group is in the UI, for working through the list by hand."""
    return f"{settings.UI_BASE_URL}/{path}" if settings.UI_BASE_URL else ""


def _report(ledger: Ledger) -> None:
    counts = ledger.counts()
    if not counts:
        logger.info("The ledger is empty. Run `survey`.")
        return
    width = max(len(state) for state in counts)
    for state, count in sorted(counts.items()):
        logger.info("  %-*s  %s", width, state, count)

    _report_offending_characters(ledger)

    failed = ledger.in_state("failed")
    for row in failed[:10]:
        logger.warning("  failed: %s - %s", row["path"], row["note"])
    if len(failed) > 10:
        logger.warning("  ...and %s more; query the ledger for the rest", len(failed) - 10)


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        # Safe to stop here: the ledger is committed after every Archival Group, so a rerun picks
        # up where this left off.
        logger.warning("Interrupted. Rerun the same command to continue.")
        sys.exit(130)
    except api.ApiError as fatal:
        logger.error(str(fatal))
        sys.exit(1)
