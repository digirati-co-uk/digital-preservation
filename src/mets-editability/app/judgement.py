"""
What the editability judge concludes about one document - the shape CONTRACT.md defines, and
the twin of Judgement.cs in DigitalPreservation.Mets.Conformance.
"""

from dataclasses import dataclass, field

EDITABLE = "editable"
EDITABLE_WITH_NORMALISATION = "editable-with-normalisation"
NAVIGABLE_READ_ONLY = "navigable-read-only"
NOT_EDITABLE = "not-editable"


@dataclass
class Finding:
    """
    One judged fact about the document. The codes are CONTRACT.md's shared vocabulary, identical
    in both judges. A finding that occurs more than once is reported once: `message` describes
    the first occurrence and `count` says how many there were.
    """
    code: str
    message: str
    count: int = 1

    def as_dict(self) -> dict:
        return {"code": self.code, "message": self.message, "count": self.count}


@dataclass
class Judgement:
    """
    The verdict, why it is not higher (`reasons`), what the EPrints tier satisfied by assumption
    rather than assertion (`assumptions`), informational `notes`, and - for
    editable-with-normalisation only - the `mutations` the first save would perform, in order.
    A dry run, never an action: the judge is read-only.
    """
    verdict: str
    file_count: int = 0
    reasons: list[Finding] = field(default_factory=list)
    assumptions: list[Finding] = field(default_factory=list)
    notes: list[Finding] = field(default_factory=list)
    mutations: list[str] = field(default_factory=list)

    def as_dict(self) -> dict:
        return {
            "verdict": self.verdict,
            "fileCount": self.file_count,
            "reasons": [f.as_dict() for f in self.reasons],
            "assumptions": [f.as_dict() for f in self.assumptions],
            "notes": [f.as_dict() for f in self.notes],
            "mutations": list(self.mutations),
        }


def add(findings: list[Finding], code: str, message: str) -> None:
    """Record one occurrence: the first carries the message, later ones only add to the count."""
    for finding in findings:
        if finding.code == code:
            finding.count += 1
            return
    findings.append(Finding(code, message))
