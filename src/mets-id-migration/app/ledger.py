"""
The ledger: one SQLite file recording what was found and what was done to it.

It exists so the campaign can be stopped and resumed, so a rerun does not migrate anything twice,
and so that afterwards there is a single artefact answering "what did this touch, and what did the
identifiers used to be". That last question has no other home: once a document is migrated, the old
IDs are only in the previous OCFL version.
"""

import json
import sqlite3
from contextlib import closing
from datetime import datetime, timezone
from typing import Any

# What a row can be. Anything that is not DONE or NO_CHANGE is still outstanding.
CANDIDATE = "candidate"      # ours, and has at least one invalid ID: to be migrated
CONFORMS = "conforms"        # ours, but every ID is already legal: nothing to do
FOREIGN = "foreign"          # written by someone else: not ours to migrate
NO_METS = "no-mets"          # no METS to look at
DONE = "done"                # migrated and verified
NO_CHANGE = "no-change"      # normalise reported nothing to do, so nothing was preserved
FAILED = "failed"            # see the note column

_SCHEMA = """
CREATE TABLE IF NOT EXISTS meta (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS archival_groups (
    path              TEXT PRIMARY KEY,
    state             TEXT NOT NULL,
    agent             TEXT,
    invalid_id_count  INTEGER DEFAULT 0,
    invalid_id_sample TEXT,
    invalid_id_chars  TEXT,
    deposit           TEXT,
    ids_rewritten     INTEGER,
    refs_rewritten    INTEGER,
    rewrites          TEXT,
    warnings          TEXT,
    from_version      TEXT,
    to_version        TEXT,
    note              TEXT,
    updated           TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS archival_groups_state ON archival_groups (state);
"""


DEPLOYMENT = "preservation_api"


class WrongDeployment(RuntimeError):
    """The ledger was built against a different Preservation API than the one now configured."""


class Ledger:
    def __init__(self, path: str, deployment: str):
        self.connection = sqlite3.connect(path)
        self.connection.row_factory = sqlite3.Row
        with closing(self.connection.cursor()) as cursor:
            cursor.executescript(_SCHEMA)
        self.connection.commit()
        self._bind_to(path, deployment)

    def _bind_to(self, path: str, deployment: str) -> None:
        """
        Tie this ledger to one deployment, and refuse to open it against any other.

        Rows are keyed by Archival Group path alone, and the same paths exist on development and on
        production - the Playwright fixtures, the Goobi tests, and any real object that exists on
        both. Sharing one ledger between deployments would not merely mix the counts: `survey` skips
        paths it already knows before `--limit` counts them, so a production survey would silently
        adopt development's verdict for every path in common and never read the production document
        at all. `migrate` then works from that list. Neither failure announces itself, so the
        cheapest safe thing is to make it impossible.
        """
        recorded = self.get_meta(DEPLOYMENT)
        current = deployment.rstrip("/")
        if recorded is None:
            if self.known_paths():
                # A ledger from before this check existed. It holds verdicts from some deployment,
                # and there is nothing in the file that says which - so adopting it for whichever
                # one happens to be configured now is the very mistake this guards against.
                raise WrongDeployment(
                    f"The ledger '{path}' has rows but does not record which deployment they came "
                    f"from, because it predates this check. If they are from {current}, adopt it "
                    f"with:\n"
                    f"    sqlite3 {path} \"INSERT INTO meta (key, value) "
                    f"VALUES ('{DEPLOYMENT}', '{current}');\"\n"
                    f"If they are from somewhere else, keep it and start a new one with --ledger.")
            self.set_meta(DEPLOYMENT, current)
            return
        if recorded.lower() != current.lower():
            raise WrongDeployment(
                f"The ledger '{path}' was built against {recorded}, but PRESERVATION_API is now "
                f"{current}. Keep one ledger per deployment - pass --ledger, or set LEDGER_PATH in "
                f".env, e.g. --ledger ledger-prod.sqlite. Sharing one would let a verdict reached "
                f"on one system stand for the other.")

    def get_meta(self, key: str) -> str | None:
        with closing(self.connection.cursor()) as cursor:
            cursor.execute("SELECT value FROM meta WHERE key = ?", (key,))
            row = cursor.fetchone()
            return row["value"] if row else None

    def set_meta(self, key: str, value: str) -> None:
        with closing(self.connection.cursor()) as cursor:
            cursor.execute(
                "INSERT INTO meta (key, value) VALUES (?, ?) "
                "ON CONFLICT(key) DO UPDATE SET value = excluded.value", (key, value))
        self.connection.commit()

    def close(self) -> None:
        self.connection.close()

    def record(self, path: str, state: str, **fields: Any) -> None:
        """
        Write (or overwrite) one Archival Group's row. Lists and dicts are stored as JSON, so the
        rewrites a migration made can be read back without a second file.
        """
        for key, value in list(fields.items()):
            if isinstance(value, (list, dict)):
                fields[key] = json.dumps(value)
        fields["state"] = state
        fields["updated"] = datetime.now(timezone.utc).isoformat()
        fields["path"] = path

        columns = ", ".join(fields)
        placeholders = ", ".join(f":{key}" for key in fields)
        updates = ", ".join(f"{key} = excluded.{key}" for key in fields if key != "path")
        with closing(self.connection.cursor()) as cursor:
            cursor.execute(
                f"INSERT INTO archival_groups ({columns}) VALUES ({placeholders}) "
                f"ON CONFLICT(path) DO UPDATE SET {updates}",
                fields)
        self.connection.commit()

    def get(self, path: str) -> sqlite3.Row | None:
        with closing(self.connection.cursor()) as cursor:
            cursor.execute("SELECT * FROM archival_groups WHERE path = ?", (path,))
            return cursor.fetchone()

    def known_paths(self) -> set[str]:
        with closing(self.connection.cursor()) as cursor:
            cursor.execute("SELECT path FROM archival_groups")
            return {row["path"] for row in cursor.fetchall()}

    def in_state(self, state: str, limit: int | None = None) -> list[sqlite3.Row]:
        query = "SELECT * FROM archival_groups WHERE state = ? ORDER BY path"
        parameters: list[Any] = [state]
        if limit is not None:
            query += " LIMIT ?"
            parameters.append(limit)
        with closing(self.connection.cursor()) as cursor:
            cursor.execute(query, parameters)
            return cursor.fetchall()

    def counts(self) -> dict[str, int]:
        with closing(self.connection.cursor()) as cursor:
            cursor.execute("SELECT state, COUNT(*) AS n FROM archival_groups GROUP BY state")
            return {row["state"]: row["n"] for row in cursor.fetchall()}
