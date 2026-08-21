"""
The editability judge: given a METS document, which 02e tier does it reach, and what would
saving do to it?

This implements CONTRACT.md (and through it, the docs repo's 02e-METS-Editability.md). The
XML-visible tier rules are executed from schematron/*.sch; this module contributes what XPath 1.0
cannot express - path resolution and uniqueness, the deposit-relative guard, NCName legality -
and assembles the verdict. The .NET twin (DigitalPreservation.Mets.Conformance) implements the
same contract; when they disagree, the contract decides which is wrong.

The judge judges the document. Policy that the document cannot show - the living-external-editor
principle that keeps Goobi read-only whatever its shape - is the platform's overlay, not ours.
"""

import posixpath
import re
from dataclasses import dataclass, field

from lxml import etree

from app import ncname, schematron

METS_NS = "http://www.loc.gov/METS/"
XLINK_NS = "http://www.w3.org/1999/xlink"
PREMIS_NS = "http://www.loc.gov/premis/v3"

PLATFORM_AGENT = "University of Leeds Digital Library Infrastructure Project"

EDITABLE = "editable"
EDITABLE_WITH_NORMALISATION = "editable-with-normalisation"
NAVIGABLE_READ_ONLY = "navigable-read-only"
NOT_EDITABLE = "not-editable"

#: Native blocker codes: any of these means the document is not navigable.
_BLOCKERS = frozenset([
    "PARSE_FAILED", "NO_PHYSICAL_STRUCTMAP", "NO_ROOT_DIV", "FILEID_UNRESOLVED",
    "FILE_NO_HREF", "HREF_NOT_DEPOSIT_RELATIVE", "DUPLICATE_PATH", "DUPLICATE_ID",
])

_URI_SCHEME = re.compile(r"^[A-Za-z][A-Za-z0-9+.\-]*:")


@dataclass
class Finding:
    code: str
    message: str

    def as_dict(self) -> dict:
        return {"code": self.code, "message": self.message}


@dataclass
class Judgement:
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


def judge_file(path) -> Judgement:
    try:
        tree = etree.parse(str(path))
    except etree.XMLSyntaxError as bad:
        return Judgement(NOT_EDITABLE, reasons=[Finding("PARSE_FAILED", str(bad))])
    return judge(tree.getroot())


def judge(root: etree._Element) -> Judgement:
    """Judge one document. `root` is the document element; a wrapper holding mets:mets is fine."""
    mets = root if root.tag == f"{{{METS_NS}}}mets" else root.find(f".//{{{METS_NS}}}mets")
    if mets is None:
        return Judgement(NOT_EDITABLE, reasons=[Finding("PARSE_FAILED", "no mets:mets element")])

    reasons: list[Finding] = []
    assumptions: list[Finding] = []
    notes: list[Finding] = []

    struct_map = _choose_physical_struct_map(mets, assumptions, notes, reasons)
    if struct_map is None:
        return Judgement(NOT_EDITABLE, reasons=reasons)

    _check_duplicate_ids(mets, reasons)
    legacy_ids = [value for value in _declared_ids(mets) if not ncname.is_valid_id(value)]
    if legacy_ids:
        notes.append(Finding(
            "LEGACY_IDS",
            f"{len(legacy_ids)} declared ID(s) are not legal NCNames, e.g. {legacy_ids[0]!r}"))

    files_by_id = _file_index(mets)
    resolved = _walk(struct_map, files_by_id, reasons, notes)

    navigable = resolved.file_count > 0 and not any(f.code in _BLOCKERS for f in reasons)

    if navigable:
        platform_failures = schematron.failures("platform-tier.sch", mets)
        if not platform_failures:
            return Judgement(EDITABLE, resolved.file_count,
                             assumptions=assumptions, notes=notes)

        eprints_failures = schematron.failures("eprints-tier.sch", mets)
        if not eprints_failures and not legacy_ids:
            _eprints_assumptions(struct_map, resolved, assumptions)
            _eprints_quirks(mets, notes)
            mutations = _mutations(struct_map, mets, resolved)
            return Judgement(EDITABLE_WITH_NORMALISATION, resolved.file_count,
                             assumptions=assumptions, notes=notes, mutations=mutations)

        if not eprints_failures and legacy_ids:
            reasons.append(Finding(
                "INVALID_IDS",
                "the document matches the EPrints tier but declares IDs that are not legal "
                "NCNames; it needs the #188 normalisation, which this tier does not perform"))

        for code, message in _distinct(platform_failures) + _distinct(eprints_failures):
            reasons.append(Finding(code, message))
        return Judgement(NAVIGABLE_READ_ONLY, resolved.file_count,
                         reasons=reasons, assumptions=assumptions, notes=notes)

    return Judgement(NOT_EDITABLE, resolved.file_count,
                     reasons=reasons, assumptions=assumptions, notes=notes)


@dataclass
class _Resolved:
    file_count: int = 0
    untyped_file_divs: int = 0
    file_divs: int = 0
    referenced_groups: list[etree._Element] = field(default_factory=list)


def _choose_physical_struct_map(mets, assumptions, notes, reasons):
    struct_maps = mets.findall(f"{{{METS_NS}}}structMap")
    explicit = [sm for sm in struct_maps if (sm.get("TYPE") or "").lower() == "physical"]
    untyped = [sm for sm in struct_maps if sm.get("TYPE") is None]

    if explicit:
        chosen, category = explicit, "explicitly physical"
        if explicit[0].get("TYPE") != "PHYSICAL":
            assumptions.append(Finding(
                "CASE_INSENSITIVE_STRUCTMAP_TYPE",
                f"structMap TYPE={explicit[0].get('TYPE')!r} read as physical case-insensitively"))
    elif untyped:
        chosen, category = untyped, "untyped"
        assumptions.append(Finding(
            "UNTYPED_STRUCTMAP_ASSUMED_PHYSICAL",
            "the structMap has no TYPE and is assumed physical"))
    else:
        reasons.append(Finding(
            "NO_PHYSICAL_STRUCTMAP",
            "no structMap is physical or can be assumed physical (untyped)"))
        return None

    if len(chosen) > 1:
        notes.append(Finding(
            "MULTIPLE_PHYSICAL_CANDIDATES",
            f"{len(chosen)} {category} structMaps; the first is judged"))
    return chosen[0]


def _declared_ids(mets):
    return [value for _, value in _iter_id_attributes(mets)]


def _iter_id_attributes(mets):
    for element in mets.iter():
        value = element.get("ID")
        if value is not None:
            yield element, value


def _check_duplicate_ids(mets, reasons):
    seen: set[str] = set()
    duplicates: set[str] = set()
    for _, value in _iter_id_attributes(mets):
        if value in seen:
            duplicates.add(value)
        seen.add(value)
    if duplicates:
        example = sorted(duplicates)[0]
        reasons.append(Finding(
            "DUPLICATE_ID",
            f"{len(duplicates)} ID(s) declared more than once, e.g. {example!r}"))


def _file_index(mets):
    index: dict[str, etree._Element] = {}
    for file_element in mets.findall(f"{{{METS_NS}}}fileSec/{{{METS_NS}}}fileGrp//{{{METS_NS}}}file"):
        file_id = file_element.get("ID")
        if file_id is not None and file_id not in index:
            index[file_id] = file_element
    return index


def _walk(struct_map, files_by_id, reasons, notes):
    resolved = _Resolved()
    root_div = struct_map.find(f"{{{METS_NS}}}div")
    if root_div is None:
        reasons.append(Finding("NO_ROOT_DIV", "the physical structMap has no div"))
        return resolved

    directories_without_admid = 0
    paths_seen: dict[str, int] = {}
    groups_seen: list[etree._Element] = []

    for div in root_div.iter(f"{{{METS_NS}}}div"):
        div_type = div.get("TYPE")
        fptrs = div.findall(f"{{{METS_NS}}}fptr")
        if (div_type or "").lower() == "directory" and div is not root_div:
            if div.get("ADMID") is None:
                directories_without_admid += 1
        if fptrs:
            resolved.file_divs += 1
            if div_type is None:
                resolved.untyped_file_divs += 1
        for fptr in fptrs:
            for file_id in _fptr_file_ids(fptr):
                _resolve_one(file_id, files_by_id, paths_seen, groups_seen, resolved, reasons)

    if directories_without_admid:
        notes.append(Finding(
            "DIRECTORY_DIV_NO_ADMID",
            f"{directories_without_admid} directory div(s) have no ADMID, so their own paths "
            "cannot be anchored in premis:originalName"))

    duplicates = {path: count for path, count in paths_seen.items() if count > 1}
    if duplicates:
        example = sorted(duplicates)[0]
        reasons.append(Finding(
            "DUPLICATE_PATH",
            f"{len(duplicates)} path(s) resolved from more than one file, e.g. {example!r}"))

    resolved.referenced_groups = groups_seen
    return resolved


def _fptr_file_ids(fptr):
    file_id = fptr.get("FILEID")
    if file_id is not None:
        yield file_id
    for area in fptr.iter(f"{{{METS_NS}}}area"):
        area_file_id = area.get("FILEID")
        if area_file_id is not None:
            yield area_file_id


def _resolve_one(file_id, files_by_id, paths_seen, groups_seen, resolved, reasons):
    file_element = files_by_id.get(file_id)
    if file_element is None:
        _append_once(reasons, "FILEID_UNRESOLVED",
                     f"an fptr references FILEID {file_id!r} that no mets:file declares")
        return
    resolved.file_count += 1
    group = file_element.getparent()
    while group is not None and group.tag != f"{{{METS_NS}}}fileGrp":
        group = group.getparent()
    if group is not None and group not in groups_seen:
        groups_seen.append(group)

    href = file_element.find(f"{{{METS_NS}}}FLocat")
    href = None if href is None else href.get(f"{{{XLINK_NS}}}href")
    if not href:
        _append_once(reasons, "FILE_NO_HREF",
                     f"file {file_id!r} has no FLocat href")
        return
    if _URI_SCHEME.match(href) or href.startswith(("/", "\\")) \
            or ".." in href.replace("\\", "/").split("/"):
        _append_once(reasons, "HREF_NOT_DEPOSIT_RELATIVE",
                     f"file {file_id!r} href {href!r} is not a relative path within the deposit")
        return
    normalised = posixpath.normpath(href.replace("\\", "/"))
    paths_seen[normalised] = paths_seen.get(normalised, 0) + 1


def _append_once(findings, code, message):
    """First occurrence carries the example; later ones only bump a count in the message."""
    for finding in findings:
        if finding.code == code:
            head, _, counted = finding.message.rpartition(" [x")
            if counted.endswith("]"):
                count = int(counted[:-1]) + 1
                finding.message = f"{head} [x{count}]"
            else:
                finding.message = f"{finding.message} [x2]"
            return
    findings.append(Finding(code, message))


def _distinct(failures):
    """One (code, message) per code, keeping the first message and appending a count."""
    by_code: dict[str, tuple[str, int]] = {}
    for code, message in failures:
        message_so_far, count = by_code.get(code, (message, 0))
        by_code[code] = (message_so_far, count + 1)
    return [(code, message if count == 1 else f"{message} [x{count}]")
            for code, (message, count) in by_code.items()]


def _eprints_assumptions(struct_map, resolved, assumptions):
    if resolved.untyped_file_divs:
        assumptions.append(Finding(
            "UNTYPED_DIV_ASSUMED_ITEM",
            f"{resolved.untyped_file_divs} untyped div(s) carrying an fptr read as Items"))
    assumptions.append(Finding(
        "IMPLIED_OBJECTS_DIV",
        "no div declares the objects directory; it is implied by every file path sitting "
        "under objects/"))


def _eprints_quirks(mets, notes):
    foreign_storage = 0
    for storage in mets.iter(f"{{{PREMIS_NS}}}storage"):
        mediums = [m.text or "" for m in storage.iter(f"{{{PREMIS_NS}}}storageMedium")]
        if PLATFORM_AGENT not in mediums:
            foreign_storage += 1
    if foreign_storage:
        notes.append(Finding(
            "FOREIGN_STORAGE_LOCATION",
            f"{foreign_storage} premis:storage assertion(s) belong to another system (no "
            "platform storageMedium); kept as history, never read as the file's location (#236)"))

    if mets.find(f".//{{{METS_NS}}}dmdSec//{{{METS_NS}}}recordInfo") is not None:
        notes.append(Finding(
            "METS_NAMESPACE_RECORD_INFO",
            "record identifiers are declared in the METS namespace (an EPrints quirk); "
            "invisible to the platform's parser until #237"))


def _mutations(struct_map, mets, resolved):
    mutations = []
    if struct_map.get("TYPE") != "PHYSICAL":
        mutations.append('set TYPE="PHYSICAL" on the structMap')
    root_div = struct_map.find(f"{{{METS_NS}}}div")
    if root_div is not None and root_div.get("TYPE") is None:
        mutations.append('set TYPE="Directory" on the root div')
    if resolved.untyped_file_divs:
        mutations.append(f'set TYPE="Item" on {resolved.untyped_file_divs} file div(s)')
    mutations.append(
        "materialise the objects Directory div (amdSec/techMD with premis:originalName) and "
        f"re-parent {resolved.file_divs} file div(s) under it")
    groups = resolved.referenced_groups
    if len(groups) > 1 or any(g.get("USE") != "OBJECTS" for g in groups):
        mutations.append(
            f'consolidate {len(groups)} fileGrp(s) into one USE="OBJECTS" group')
    agents = mets.findall(f"{{{METS_NS}}}metsHdr/{{{METS_NS}}}agent/{{{METS_NS}}}name")
    if PLATFORM_AGENT not in [a.text for a in agents]:
        mutations.append("append the platform agent to metsHdr")
    return mutations
