"""
Run the canonical Schematron rules (schematron/*.sch) against a METS document.

The .sch files are the single source of the XML-visible tier rules; this module executes them
directly via lxml's ISO Schematron support. The .NET judge executes the same rules as the
compiled XSLT committed under schematron/compiled/ (regenerate with
tools/compile_schematron.py). Each failed assert surfaces as (code, message), where the code is
the assert's @id - shared vocabulary with the native checks and the .NET side.
"""

import copy
import functools
import pathlib

from lxml import etree, isoschematron

SCHEMATRON_DIR = pathlib.Path(__file__).resolve().parent.parent / "schematron"
SVRL_NS = "http://purl.oclc.org/dsdl/svrl"


@functools.cache
def _schema(name: str) -> isoschematron.Schematron:
    return isoschematron.Schematron(
        etree.parse(str(SCHEMATRON_DIR / name)), store_report=True)


def failures(schematron_file: str, mets_element: etree._Element) -> list[tuple[str, str]]:
    """
    Every failed assert of one tier's rules against one mets:mets element, as (code, message).

    The same code can fail many times (once per offending element); callers see every occurrence
    and can count them.
    """
    schema = _schema(schematron_file)
    if mets_element.getroottree().getroot() is not mets_element:
        # The rules use xsl:key, and libxslt's key machinery needs the context node to be the
        # root of its own document - a "tree" wrapped around an inner element (a mets:mets
        # inside a fixture wrapper) makes every key() call fail. Detach it into one.
        mets_element = copy.deepcopy(mets_element)
    schema.validate(etree.ElementTree(mets_element))
    report = schema.validation_report
    found = []
    for failed in report.iter(f"{{{SVRL_NS}}}failed-assert"):
        code = failed.get("id") or "SCHEMATRON"
        text = failed.findtext(f"{{{SVRL_NS}}}text") or ""
        found.append((code, " ".join(text.split())))
    return found
