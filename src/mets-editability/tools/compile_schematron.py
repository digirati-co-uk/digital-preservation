#!/usr/bin/env python
"""
Compile the canonical Schematron rules into plain XSLT 1.0, for the .NET judge.

The Python judge runs the .sch files directly through lxml's ISO Schematron support. .NET has no
Schematron implementation, but it has XslCompiledTransform - so the .sch is compiled once, here,
into the validating XSLT (which emits SVRL), and the result is committed under
schematron/compiled/. The .NET judge runs that XSLT and reads the svrl:failed-assert elements.

Run this whenever a .sch file changes:

    python tools/compile_schematron.py

A test on each side pins the compiled form to the source, so a drifted checkout fails loudly
rather than judging with stale rules.
"""

import pathlib
import sys

from lxml import etree, isoschematron

HERE = pathlib.Path(__file__).resolve().parent
SCHEMATRON = HERE.parent / "schematron"
COMPILED = SCHEMATRON / "compiled"


def compile_all() -> int:
    COMPILED.mkdir(exist_ok=True)
    for source in sorted(SCHEMATRON.glob("*.sch")):
        schema = isoschematron.Schematron(
            etree.parse(str(source)), store_xslt=True)
        target = COMPILED / (source.stem + ".xsl")
        target.write_bytes(etree.tostring(
            schema.validator_xslt, xml_declaration=True, encoding="utf-8"))
        print(f"compiled {source.name} -> {target.relative_to(SCHEMATRON.parent)}")
    return 0


if __name__ == "__main__":
    sys.exit(compile_all())
