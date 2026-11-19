#!/usr/bin/env python
"""
Compile the canonical Schematron rules (schematron/*.sch) into plain XSLT 1.0.

Both judges run the compiled form: the Python judge through lxml's etree.XSLT, the .NET judge
through XslCompiledTransform (it embeds schematron/compiled/*.xsl). The .sch files stay the
human-maintained source; the compiled files are committed so that neither judge needs a
Schematron processor at run time, and so that both run the very same artefact.

Run this whenever a .sch file (or chosen-structmap.ent) changes:

    python tools/compile_schematron.py

It also deletes any compiled .xsl whose .sch no longer exists. CI recompiles and fails if the
result differs from what is committed in any way - a changed, new or deleted file - so a stale
compiled/ cannot merge. The Python suite checks the same thing locally.
"""

import pathlib
import sys

from lxml import etree, isoschematron

HERE = pathlib.Path(__file__).resolve().parent
SCHEMATRON = HERE.parent / "schematron"
COMPILED = SCHEMATRON / "compiled"

# The ISO skeleton by default records, for every failed assert, an XPath to the failing node
# (svrl:failed-assert/@location) and an svrl:fired-rule for every node a rule matched. The judges
# use neither, and the location is computed by counting preceding siblings, which makes a
# document with thousands of failing files quadratic. Leave both out.
_COMPILE_PARAMS = {
    "generate-paths": "'false'",
    "generate-fired-rule": "'false'",
}


def compiled_rules() -> dict[str, bytes]:
    """Every .sch compiled to XSLT, as {file name: bytes} - what compiled/ must hold."""
    # load_dtd: the .sch files pull the chosen-structmap entities in from chosen-structmap.ent.
    parser = etree.XMLParser(load_dtd=True, resolve_entities=True, no_network=True)
    result = {}
    for source in sorted(SCHEMATRON.glob("*.sch")):
        schema = isoschematron.Schematron(
            etree.parse(str(source), parser), compile_params=_COMPILE_PARAMS, store_xslt=True)
        result[source.stem + ".xsl"] = etree.tostring(
            schema.validator_xslt, xml_declaration=True, encoding="utf-8")
    return result


def compile_all() -> int:
    COMPILED.mkdir(exist_ok=True)
    wanted = compiled_rules()
    for name, content in wanted.items():
        (COMPILED / name).write_bytes(content)
        print(f"compiled {pathlib.Path(name).stem}.sch -> compiled/{name}")
    for orphan in sorted(COMPILED.glob("*.xsl")):
        if orphan.name not in wanted:
            orphan.unlink()
            print(f"removed compiled/{orphan.name} (no {orphan.stem}.sch)")
    return 0


if __name__ == "__main__":
    sys.exit(compile_all())
