"""
Run the compiled Schematron rules (schematron/compiled/*.xsl) against a METS document.

The rules are authored as Schematron (schematron/*.sch) and compiled once, by
tools/compile_schematron.py, into plain XSLT 1.0 that emits SVRL. Both judges run that same
compiled XSLT - this module through lxml, the .NET judge through XslCompiledTransform - so the
two cannot drift apart through different Schematron processors. Each failed assert carries the
assert's @id, a finding code shared with the native checks and the .NET side.
"""

import copy
import pathlib
import threading

from lxml import etree

from app.judgement import Finding, add

COMPILED_DIR = pathlib.Path(__file__).resolve().parent.parent / "schematron" / "compiled"
SVRL_NS = "http://purl.oclc.org/dsdl/svrl"

# One compiled transform per rule set per thread. Each call below produces its own result
# document, so nothing is shared between judgements; keeping the transforms per thread as well
# means the judge is safe to call from several threads without relying on lxml's guarantees
# about sharing one XSLT object.
_per_thread = threading.local()


def _transform(rule_set: str) -> etree.XSLT:
    transforms = getattr(_per_thread, "transforms", None)
    if transforms is None:
        transforms = _per_thread.transforms = {}
    if rule_set not in transforms:
        transforms[rule_set] = etree.XSLT(etree.parse(str(COMPILED_DIR / f"{rule_set}.xsl")))
    return transforms[rule_set]


def failures(rule_set: str, mets: etree._Element) -> list[Finding]:
    """
    The failed asserts of one rule set ("common", "platform-tier" or "eprints-tier") against one
    mets:mets element: one Finding per code, counting every element that failed it.
    """
    if mets.getroottree().getroot() is not mets:
        # The rules use xsl:key, and libxslt's key machinery needs the context node to be the
        # root of its own document - a mets:mets inside a wrapper element makes every key()
        # call fail. Detach it into one.
        mets = copy.deepcopy(mets)
    report = _transform(rule_set)(etree.ElementTree(mets))
    found: list[Finding] = []
    for failed in report.getroot().iter(f"{{{SVRL_NS}}}failed-assert"):
        text = failed.findtext(f"{{{SVRL_NS}}}text") or ""
        add(found, failed.get("id") or "SCHEMATRON", " ".join(text.split()))
    return found
