#!/usr/bin/env python
"""
Is this METS file editable by the platform - and what would saving do to it?

    python mets_editability.py judge path/to/mets.xml
    python mets_editability.py judge path/to/folder            # every *.xml in it
    python mets_editability.py judge path/to/mets.xml --json

Implements the contract in CONTRACT.md, which implements 02e-METS-Editability.md in the docs
repo. Read-only: nothing here writes, anywhere. The verdicts:

    editable                       the platform's own shape (02b); legacy IDs noted, not fatal
    editable-with-normalisation    the EPrints shape; the first save restructures to 02b -
                                   the judgement lists exactly what that changes
    navigable-read-only            files all resolve to deposit-relative paths, but no
                                   editable tier is met
    not-editable                   the document does not resolve

The judge judges the document. Policy the document cannot show - Goobi is read-only because
Goobi still edits its own documents, however conformant they look - is applied on top.
"""

import argparse
import json
import pathlib
import sys

from app import judge


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)
    judge_command = commands.add_parser("judge", help="judge one METS file, or every .xml in a folder")
    judge_command.add_argument("target")
    judge_command.add_argument("--json", action="store_true",
                               help="machine-readable output, one JSON object per document")
    arguments = parser.parse_args()

    target = pathlib.Path(arguments.target)
    files = sorted(target.glob("*.xml")) if target.is_dir() else [target]
    if not files:
        print(f"nothing to judge in {target}", file=sys.stderr)
        return 2

    worst = 0
    for path in files:
        judgement = judge.judge_file(path)
        if arguments.json:
            print(json.dumps({"document": str(path), **judgement.as_dict()}))
        else:
            _print_human(path, judgement)
        if judgement.verdict == judge.NOT_EDITABLE:
            worst = max(worst, 1)
    return worst


def _print_human(path: pathlib.Path, judgement) -> None:
    print(f"{path.name}: {judgement.verdict}  ({judgement.file_count} file(s))")
    for label, findings in (("reason", judgement.reasons),
                            ("assumed", judgement.assumptions),
                            ("note", judgement.notes)):
        for finding in findings:
            print(f"  {label:8} {finding.code}: {finding.message}")
    for mutation in judgement.mutations:
        print(f"  on save  {mutation}")


if __name__ == "__main__":
    sys.exit(main())
