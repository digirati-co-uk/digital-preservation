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


class Ledger:
    def __init__(self, path: str):
        self.connection = sqlite3.connect(path)
        self.connection.row_factory = sqlite3.Row
        with closing(self.connection.cursor()) as cursor:
            cursor.executescript(_SCHEMA)
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
