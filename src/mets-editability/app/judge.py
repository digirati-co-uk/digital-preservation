"""
The editability judge: given a METS document, which tier does it reach, and what would saving do
to it?

This implements CONTRACT.md. The XML-visible tier rules run as the compiled Schematron
(schematron/compiled/*.xsl); this module contributes what XPath 1.0 cannot express - reading the
document the way the platform's parser does, path checks, NCName legality - and assembles the
verdict. The .NET twin (DigitalPreservation.Mets.Conformance) implements the same contract
function for function; when they disagree, the contract decides which is wrong.

The judge judges the document. Policy that the document cannot show - the living-external-editor
principle that keeps Goobi read-only whatever its shape - is the platform's overlay, not ours.
"""

import re
import urllib.parse

from lxml import etree

from app import ncname, schematron
from app.judgement import (EDITABLE, EDITABLE_WITH_NORMALISATION, NAVIGABLE_READ_ONLY,
                           NOT_EDITABLE, Finding, Judgement, add)

METS_NS = "http://www.loc.gov/METS/"
XLINK_NS = "http://www.w3.org/1999/xlink"
PREMIS_NS = "http://www.loc.gov/premis/v3"
MODS_NS = "http://www.loc.gov/mods/v3"

#: Constants.MetsCreatorAgent on the .NET side (a test pins the copy).
PLATFORM_AGENT = "University of Leeds Digital Library Infrastructure Project"
#: The LABEL the platform writes on its root div (WorkingDirectory.DefaultRootName).
ROOT_LABEL = "__ROOT"
#: Deeper documents are refused before anything walks them: the .NET XSLT engine recurses
#: with the tree and a deep enough document overflows its stack.
MAX_DEPTH = 256

#: Any of these means the platform cannot open the document: not-editable.
_BLOCKERS = frozenset([
    "PARSE_FAILED", "DOCTYPE_DECLARED", "TOO_DEEP", "SEVERAL_METS_ELEMENTS",
    "NO_PHYSICAL_STRUCTMAP", "NO_ROOT_DIV", "DUPLICATE_ID",
    "FPTR_NO_FILEID", "FILEID_UNRESOLVED", "FILE_REFERENCED_TWICE", "FILE_SEVERAL_FLOCATS",
    "FILE_NO_HREF", "HREF_NOT_DEPOSIT_RELATIVE", "HREF_NOT_NORMALISED", "DUPLICATE_PATH",
    "DIRECTORY_NO_LABEL", "DIRECTORY_PATH_INVALID", "DIRECTORY_LOCATION_NOT_URI",
    "PREMIS_REPEATED", "RECORD_IDENTIFIER_NO_SOURCE", "TIME_VALUE_BLANK",
])

#: What the parser reads, with SingleOrDefault, from a file's technical metadata - PREMIS
#: elements, and the platform's own un-namespaced ExifMetadata.
_READ_FROM_A_FILE_SECTION = ("premis:fixity", "premis:size", "premis:originalName",
                             "premis:format", "premis:formatName", "premis:formatRegistryKey",
                             "ExifMetadata")
#: What the parser reads, the same way, from a directory's.
_READ_FROM_A_DIRECTORY_SECTION = ("premis:originalName",)

_XML_WHITESPACE_RUN = re.compile(r"[ \t\r\n]+")
_URI_SCHEME = re.compile(r"^[A-Za-z][A-Za-z0-9+.\-]*:")
#: A deliberately narrow absolute URI - scheme://host[:port][/path] - that the platform's parser
#: (System.Uri) is known to accept. Narrower than what it accepts, never wider.
_ABSOLUTE_URI = re.compile(
    r"^[A-Za-z][A-Za-z0-9+.\-]*://[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?"
    r"(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)*(?::[0-9]{1,4})?(?:/\S*)?\Z")


def judge_file(path) -> Judgement:
    """
    Judge one METS file. Only a local file is read - a URL is not fetched - and anything that
    stops it being read or parsed is a judgement (PARSE_FAILED), never a crash.
    """
    try:
        with open(path, "rb") as stream:
            refusal = _refuse_while_reading(stream)
            if refusal is not None:
                return Judgement(NOT_EDITABLE, reasons=[refusal])
            stream.seek(0)
            tree = etree.parse(stream, _parser())
    except (etree.XMLSyntaxError, OSError, ValueError) as bad:
        return Judgement(NOT_EDITABLE, reasons=[Finding("PARSE_FAILED", str(bad))])
    return judge(tree.getroot())


def judge(root: etree._Element) -> Judgement:
    """Judge one document. `root` is the document element; a wrapper holding mets:mets is fine."""
    refusal = _refuse_parsed(root)
    if refusal is not None:
        return Judgement(NOT_EDITABLE, reasons=[refusal])
    mets = root if root.tag == f"{{{METS_NS}}}mets" else root.find(f".//{{{METS_NS}}}mets")
    if mets is None:
        return Judgement(NOT_EDITABLE, reasons=[Finding("PARSE_FAILED", "no mets:mets element")])
    mets_elements = sum(1 for _ in root.iter(f"{{{METS_NS}}}mets"))
    if mets_elements > 1:
        return Judgement(NOT_EDITABLE, reasons=[Finding(
            "SEVERAL_METS_ELEMENTS",
            f"the document holds {mets_elements} mets:mets elements; the platform's parser "
            "searches the whole document by element name, so it would read the others' "
            "structMaps, files and sections as this one's")])

    reasons: list[Finding] = []
    assumptions: list[Finding] = []
    notes: list[Finding] = []

    struct_map = _choose_physical_struct_map(mets, assumptions, notes, reasons)
    if struct_map is None:
        return Judgement(NOT_EDITABLE, reasons=reasons, assumptions=assumptions, notes=notes)

    _check_duplicate_ids(mets, reasons)
    legacy_ids = [value for value in _declared_ids(mets) if not ncname.is_valid_id(value)]
    if legacy_ids:
        notes.append(Finding(
            "LEGACY_IDS",
            f"{len(legacy_ids)} declared ID(s) are not legal NCNames, e.g. '{legacy_ids[0]}'",
            len(legacy_ids)))

    index = _Index(mets)
    file_count = _walk(struct_map, index, reasons, notes)
    _check_values_the_parser_converts(mets, index, reasons)
    unwrapped_md_wraps = _quirk_notes(mets, index, notes)

    if any(finding.code in _BLOCKERS for finding in reasons):
        return Judgement(NOT_EDITABLE, file_count,
                         reasons=reasons, assumptions=assumptions, notes=notes)
    if file_count == 0:
        # The platform's own freshly-created deposit skeleton is this: structure, no files yet.
        notes.append(Finding("NO_FILES", "the physical structMap references no files yet"))

    # The common rules guard the linkage every edit operation relies on, so they gate both
    # tiers: the platform cannot safely change what it cannot resolve. Shared editable dmdSecs
    # gate the same way, natively (DMDID token-splitting is not safe in XPath 1.0).
    shared_dmd_secs = _shared_editable_dmd_secs(mets, index)
    if shared_dmd_secs:
        reasons.append(Finding(
            "SHARED_DMDSEC",
            f"{shared_dmd_secs} MODS dmdSec(s) are referenced from more than one div; editing "
            "metadata on one div would silently rewrite the other's", shared_dmd_secs))
    common_failures = schematron.failures("common", mets)
    platform_failures = schematron.failures("platform-tier", mets)
    if not shared_dmd_secs and not common_failures and not platform_failures:
        return Judgement(EDITABLE, file_count, assumptions=assumptions, notes=notes)

    eprints_failures = schematron.failures("eprints-tier", mets)
    if not shared_dmd_secs and not common_failures and not eprints_failures:
        if not legacy_ids:
            _eprints_assumptions(struct_map, assumptions)
            return Judgement(EDITABLE_WITH_NORMALISATION, file_count,
                             assumptions=assumptions, notes=notes,
                             mutations=_mutations(struct_map, mets, unwrapped_md_wraps))
        reasons.append(Finding(
            "INVALID_IDS",
            "the document matches the EPrints tier but declares IDs that are not legal "
            "NCNames; it needs the #188 normalisation, which this tier does not perform"))

    reasons.extend(common_failures + platform_failures + eprints_failures)
    return Judgement(NAVIGABLE_READ_ONLY, file_count,
                     reasons=reasons, assumptions=assumptions, notes=notes)


# --- Refusals before any rule runs -------------------------------------------------------------

def _parser() -> etree.XMLParser:
    # A new parser per call: lxml parsers must not be shared between threads. huge_tree lifts
    # libxml2's own size and depth limits so that only the judge's MAX_DEPTH decides; with no
    # DTD (refused before this parse) there are no entities to resolve.
    return etree.XMLParser(huge_tree=True, resolve_entities=False, no_network=True)


def _refuse_while_reading(stream) -> Finding | None:
    """A streaming pass that refuses a DTD or excessive depth before the document is built."""
    depth = 0
    for event, element in etree.iterparse(stream, events=("start", "end"), huge_tree=True,
                                          resolve_entities=False, no_network=True):
        if event == "end":
            depth -= 1
            continue
        if depth == 0 and element.getroottree().docinfo.doctype:
            return _doctype_declared()
        depth += 1
        if depth > MAX_DEPTH:
            return _too_deep()
    return None


def _refuse_parsed(root) -> Finding | None:
    """The same refusals, for a document the caller has already parsed."""
    if root.getroottree().docinfo.doctype:
        return _doctype_declared()
    stack = [(root, 1)]
    while stack:
        element, depth = stack.pop()
        if depth > MAX_DEPTH:
            return _too_deep()
        stack.extend((child, depth + 1) for child in element if isinstance(child.tag, str))
    return None


def _doctype_declared() -> Finding:
    return Finding(
        "DOCTYPE_DECLARED",
        "the document declares a DTD; the judge does not accept one, because XML parsers "
        "disagree on what its entity declarations expand to")


def _too_deep() -> Finding:
    return Finding("TOO_DEEP", f"the document nests elements more than {MAX_DEPTH} deep")


# --- Reading the document the way the platform's parser does ------------------------------------

def _reads_as(value, word) -> bool:
    """
    Whether an attribute value is `word` ignoring ASCII case. The parser lowercases TYPE values
    with ToLowerInvariant, which, for these all-ASCII words, matches exactly the values that
    are the word in some ASCII case.
    """
    return value is not None and value.isascii() and value.lower() == word


def _choose_physical_struct_map(mets, assumptions, notes, reasons):
    """
    The map the platform's parser walks: the first structMap typed physical, otherwise the first
    one not typed logical. The judge accepts that choice only if the map is physical or untyped -
    the parser would also walk a map typed something else entirely (OTHER, or an empty TYPE),
    but nothing says such a map is the physical one.
    """
    struct_maps = mets.findall(f"{{{METS_NS}}}structMap")
    physical = [sm for sm in struct_maps if _reads_as(sm.get("TYPE"), "physical")]
    if physical:
        candidates, category = physical, "explicitly physical"
        if physical[0].get("TYPE") != "PHYSICAL":
            assumptions.append(Finding(
                "CASE_INSENSITIVE_STRUCTMAP_TYPE",
                f"structMap TYPE='{physical[0].get('TYPE')}' read as physical case-insensitively"))
    else:
        not_logical = [sm for sm in struct_maps if not _reads_as(sm.get("TYPE"), "logical")]
        if not not_logical:
            reasons.append(Finding("NO_PHYSICAL_STRUCTMAP",
                                   "no structMap is physical or can be assumed physical (untyped)"))
            return None
        if not_logical[0].get("TYPE") is not None:
            reasons.append(Finding(
                "NO_PHYSICAL_STRUCTMAP",
                f"the platform's parser would walk the structMap with TYPE="
                f"'{not_logical[0].get('TYPE')}' as the physical one; only a physical or "
                "untyped structMap can be judged as physical"))
            return None
        candidates, category = [sm for sm in struct_maps if sm.get("TYPE") is None], "untyped"
        assumptions.append(Finding(
            "UNTYPED_STRUCTMAP_ASSUMED_PHYSICAL",
            "the structMap has no TYPE and is assumed physical"))

    if len(candidates) > 1:
        notes.append(Finding(
            "MULTIPLE_PHYSICAL_CANDIDATES",
            f"{len(candidates)} {category} structMaps; the first is judged", len(candidates)))
    return candidates[0]


def _declared_ids(mets):
    return [element.get("ID") for element in mets.iter() if element.get("ID") is not None]


def _check_duplicate_ids(mets, reasons):
    declared: dict[str, int] = {}
    for value in _declared_ids(mets):
        declared[value] = declared.get(value, 0) + 1
    duplicates = [value for value, count in declared.items() if count > 1]
    if duplicates:
        reasons.append(Finding(
            "DUPLICATE_ID",
            f"{len(duplicates)} ID(s) declared more than once, e.g. '{duplicates[0]}'",
            len(duplicates)))


class _Index:
    """
    The lookups by ID the walk needs, built once. The first element wins where an ID is
    declared twice - DUPLICATE_ID has already refused such a document.
    """

    def __init__(self, mets):
        self.files = _by_id(mets.findall(
            f"{{{METS_NS}}}fileSec/{{{METS_NS}}}fileGrp//{{{METS_NS}}}file"))
        self.sections = _by_id(mets.iter(f"{{{METS_NS}}}techMD", f"{{{METS_NS}}}amdSec"))
        self.dmd_secs = _by_id(mets.findall(f"{{{METS_NS}}}dmdSec"))


def _by_id(elements) -> dict:
    index = {}
    for element in elements:
        element_id = element.get("ID")
        if element_id is not None and element_id not in index:
            index[element_id] = element
    return index


def _resolve_idrefs(value, by_id) -> list:
    """
    The elements an IDREFS attribute (ADMID, DMDID) names, as the platform's IdRefs.ResolveAll
    reads it: the whole value first (a legacy platform ID can contain spaces), otherwise each
    XML-whitespace-separated token that names something, each element once.
    """
    if value in by_id:
        return [by_id[value]]
    resolved = []
    for token in _XML_WHITESPACE_RUN.split(value):
        element = by_id.get(token) if token else None
        if element is not None and element not in resolved:
            resolved.append(element)
    return resolved


def _name(element) -> str:
    element_id = element.get("ID")
    return "(no ID)" if element_id is None else f"'{element_id}'"


def _text(element) -> str:
    """The element's string value - all its text, comments excluded - as XElement.Value reads it."""
    return element.xpath("string()")


def _walk(struct_map, index, reasons, notes) -> int:
    """
    Walk the chosen structMap as the platform's parser does - every div, in document order -
    refusing whatever would stop the parser loading the document, and resolving every file the
    divs point at. Returns the number of distinct files referenced.
    """
    if struct_map.find(f"{{{METS_NS}}}div") is None:
        reasons.append(Finding("NO_ROOT_DIV", "the physical structMap has no div"))
        return 0

    seen_file_ids: set[str] = set()
    paths_seen: dict[str, int] = {}
    file_sections: list = []
    directories_without_admid = 0

    for div in struct_map.iter(f"{{{METS_NS}}}div"):
        if _reads_as(div.get("TYPE"), "directory"):
            _check_directory_div(div, index, reasons)
            if div.getparent() is not struct_map and div.get("ADMID") is None:
                directories_without_admid += 1
        for fptr in div.findall(f"{{{METS_NS}}}fptr"):
            _resolve_fptr(fptr, div, index, seen_file_ids, paths_seen, file_sections, reasons)

    for section in file_sections:
        _check_read_once(section, _READ_FROM_A_FILE_SECTION, reasons)

    if directories_without_admid:
        notes.append(Finding(
            "DIRECTORY_DIV_NO_ADMID",
            f"{directories_without_admid} directory div(s) have no ADMID, so their own paths "
            "cannot be anchored in premis:originalName", directories_without_admid))

    duplicates = [path for path, count in paths_seen.items() if count > 1]
    if duplicates:
        reasons.append(Finding(
            "DUPLICATE_PATH",
            f"{len(duplicates)} path(s) resolved from more than one file, e.g. '{duplicates[0]}'",
            len(duplicates)))
    return len(seen_file_ids)


def _check_directory_div(div, index, reasons):
    """A Directory div must have a LABEL; the section its ADMID names supplies its path."""
    if not div.get("LABEL"):
        add(reasons, "DIRECTORY_NO_LABEL",
            f"directory div {_name(div)} has no LABEL; the platform's parser requires one on "
            "every directory")
    admid = div.get("ADMID")
    if not admid:
        return
    for section in _resolve_idrefs(admid, index.sections):
        _check_directory_section(div, section, reasons)


def _check_directory_section(div, section, reasons):
    """
    One section a Directory div's ADMID names: what the parser reads from it must be there
    once, and must be a path or location it can use.
    """
    _check_read_once(section, _READ_FROM_A_DIRECTORY_SECTION, reasons)
    for original_name in section.iter(f"{{{PREMIS_NS}}}originalName"):
        value = _text(original_name)
        if _path_problem(value) is not None:
            add(reasons, "DIRECTORY_PATH_INVALID",
                f"directory div {_name(div)} has premis:originalName '{value}', which is "
                "not a normalised path within the deposit")
    # The parser reads a directory's location only from the platform's own storage (one
    # whose storageMedium is the platform agent), and turns it into a Uri unguarded.
    for storage in section.iter(f"{{{PREMIS_NS}}}storage"):
        if not _is_platform_storage(storage):
            continue
        for location in storage.iter(f"{{{PREMIS_NS}}}contentLocationValue"):
            value = _text(location)
            if value.strip() and not _ABSOLUTE_URI.match(value):
                add(reasons, "DIRECTORY_LOCATION_NOT_URI",
                    f"directory div {_name(div)} has the storage location '{value}', which "
                    "the platform's parser cannot read as an absolute URI")


def _resolve_fptr(fptr, div, index, seen_file_ids, paths_seen, file_sections, reasons):
    file_id = fptr.get("FILEID")
    if file_id is None:
        add(reasons, "FPTR_NO_FILEID",
            f"an fptr in div {_name(div)} has no FILEID; the platform's parser requires one on "
            "every fptr in the physical structMap (area-only pointers belong in logical ones)")
        return
    file_element = index.files.get(file_id)
    if file_element is None:
        add(reasons, "FILEID_UNRESOLVED",
            f"an fptr references FILEID '{file_id}' that no mets:file declares")
        return
    if file_id in seen_file_ids:
        add(reasons, "FILE_REFERENCED_TWICE",
            f"file '{file_id}' is referenced from more than one fptr in the physical "
            "structMap; the platform's parser would list it twice and fail")
        return
    seen_file_ids.add(file_id)
    _collect_file_sections(div, file_element, index, file_sections)

    locations = file_element.findall(f"{{{METS_NS}}}FLocat")
    if len(locations) > 1:
        add(reasons, "FILE_SEVERAL_FLOCATS",
            f"file '{file_id}' has {len(locations)} FLocat elements; the platform's parser "
            "reads exactly one")
        return
    href = locations[0].get(f"{{{XLINK_NS}}}href") if locations else None
    if not href:
        add(reasons, "FILE_NO_HREF", f"file '{file_id}' has no FLocat href")
        return
    _check_href(file_id, href, paths_seen, reasons)


def _collect_file_sections(div, file_element, index, file_sections):
    """
    The sections the parser reads a file's technical metadata from: those its div's ADMID names
    and those its own ADMID names, each once.
    """
    for admid in (div.get("ADMID"), file_element.get("ADMID")):
        for section in _resolve_idrefs(admid, index.sections) if admid else []:
            if section not in file_sections:
                file_sections.append(section)


def _check_href(file_id, href, paths_seen, reasons):
    problem = _path_problem(href)
    if problem == "HREF_NOT_DEPOSIT_RELATIVE":
        add(reasons, problem,
            f"file '{file_id}' href '{href}' is not a relative path within the deposit")
    elif problem == "HREF_NOT_NORMALISED":
        add(reasons, problem,
            f"file '{file_id}' href '{href}' contains an empty or '.' segment, which the "
            "platform's path cache does not normalise - the entry would be unreachable")
    else:
        paths_seen[href] = paths_seen.get(href, 0) + 1


def _path_problem(path) -> str | None:
    """
    What is wrong with a path taken from the document, or None.

    HREF_NOT_DEPOSIT_RELATIVE: a URI scheme, a leading '/', a backslash anywhere, or a segment
    that is '..' or holds a separator once percent-decoded - the standing guard, and everything
    the parser refuses outright.
    HREF_NOT_NORMALISED: an empty or '.' segment, as given or percent-decoded. The platform's
    path cache does not normalise paths, so such an entry is real but unreachable - and the
    judge must not be more tolerant than the machinery it vouches for.
    """
    segments = path.split("/")
    decoded = [urllib.parse.unquote(segment) for segment in segments]
    if (_URI_SCHEME.match(path) or path.startswith("/") or "\\" in path
            or any(part == ".." or "/" in part or "\\" in part for part in decoded)):
        return "HREF_NOT_DEPOSIT_RELATIVE"
    if any(segment == "" or part == "." for segment, part in zip(segments, decoded)):
        return "HREF_NOT_NORMALISED"
    return None


def _is_platform_storage(storage) -> bool:
    """A premis:storage the platform wrote: its storageMedium is the platform agent."""
    return PLATFORM_AGENT in [_text(medium)
                              for medium in storage.iter(f"{{{PREMIS_NS}}}storageMedium")]


def _check_read_once(section, names, reasons):
    """The parser reads each of these once from the section, and throws if there are two."""
    for name in names:
        prefix, _, local_name = name.rpartition(":")
        count = sum(1 for _ in section.iter(f"{{{PREMIS_NS}}}{local_name}" if prefix else name))
        if count > 1:
            add(reasons, "PREMIS_REPEATED",
                f"section {_name(section)} holds {count} {name} elements; the platform's "
                "parser reads exactly one")


def _check_values_the_parser_converts(mets, index, reasons):
    """
    Values the platform's parser converts without a guard, so that a bad one stops it loading
    the document at all. Each check covers at least every value the parser reads, and sometimes
    more: a stricter judge is safe, a more tolerant one is not.
    """
    _check_record_identifiers(mets, index, reasons)
    _check_time_values(mets, reasons)


def _check_record_identifiers(mets, index, reasons):
    for dmd_id in _dmd_referents(mets, index):
        for identifier in index.dmd_secs[dmd_id].iter(f"{{{MODS_NS}}}recordIdentifier"):
            if identifier.get("source") is None:
                add(reasons, "RECORD_IDENTIFIER_NO_SOURCE",
                    f"dmdSec '{dmd_id}' holds a mods:recordIdentifier without a source "
                    "attribute, which the platform's parser requires")


def _check_time_values(mets, reasons):
    for area in mets.iter(f"{{{METS_NS}}}area"):
        for attribute in ("BEGIN", "END"):
            value = area.get(attribute)
            if value is not None and not value.strip():
                add(reasons, "TIME_VALUE_BLANK",
                    f"an area has a blank {attribute}; the platform's parser fails on a blank "
                    "time code")
    for properties in mets.iter(f"{{{PREMIS_NS}}}significantProperties"):
        kind = properties.find(f"{{{PREMIS_NS}}}significantPropertiesType")
        value = properties.find(f"{{{PREMIS_NS}}}significantPropertiesValue")
        if kind is not None and _text(kind) == "Duration" and value is not None \
                and not _text(value).strip():
            add(reasons, "TIME_VALUE_BLANK",
                "a premis:significantProperties Duration is blank; the platform's parser "
                "fails on a blank time code")


# --- Notes and the EPrints tier ---------------------------------------------------------------

def _eprints_assumptions(struct_map, assumptions):
    root_div = struct_map.find(f"{{{METS_NS}}}div")
    file_divs = root_div.findall(f"{{{METS_NS}}}div")
    untyped = sum(1 for div in file_divs if div.get("TYPE") is None)
    if untyped:
        assumptions.append(Finding(
            "UNTYPED_DIV_ASSUMED_ITEM",
            f"{untyped} untyped div(s) carrying an fptr read as Items", untyped))
    case_variants = (int(root_div.get("TYPE") not in (None, "Directory"))
                     + sum(1 for div in file_divs if div.get("TYPE") not in (None, "Item")))
    if case_variants:
        assumptions.append(Finding(
            "CASE_INSENSITIVE_DIV_TYPE",
            f"{case_variants} div TYPE(s) read as Directory or Item case-insensitively",
            case_variants))
    assumptions.append(Finding(
        "IMPLIED_OBJECTS_DIV",
        "no div declares the objects directory; it is implied by every file path sitting "
        "directly under objects/"))


def _quirk_notes(mets, index, notes) -> int:
    """
    Corpus quirks reported for every document, whatever the verdict. Returns the count of
    mdWrap elements needing the xmlData repair, which the EPrints tier turns into a mutation.
    """
    foreign_storage = sum(1 for storage in mets.iter(f"{{{PREMIS_NS}}}storage")
                          if not _is_platform_storage(storage))
    if foreign_storage:
        notes.append(Finding(
            "FOREIGN_STORAGE_LOCATION",
            f"{foreign_storage} premis:storage assertion(s) belong to another system (no "
            "platform storageMedium); the platform reads only its own, so these are kept as "
            "history and never read as a location", foreign_storage))

    if mets.find(f".//{{{METS_NS}}}dmdSec//{{{METS_NS}}}recordInfo") is not None:
        notes.append(Finding(
            "METS_NAMESPACE_RECORD_INFO",
            "record identifiers are declared in the METS namespace (an EPrints quirk); the "
            "platform reads them because the dmdSec claims MODS, and its own edits add MODS "
            "ones alongside"))

    foreign_dmd_secs = sum(1 for dmd_id in _dmd_referents(mets, index)
                           if _is_foreign_dmd_sec(index.dmd_secs[dmd_id]))
    if foreign_dmd_secs:
        notes.append(Finding(
            "FOREIGN_DMDSEC",
            f"{foreign_dmd_secs} referenced dmdSec(s) claim MODS but hold no mods:mods record; "
            "the platform never edits these - an edit on such a div creates a platform dmdSec "
            "and appends its ID to the div's DMDID, leaving the original untouched",
            foreign_dmd_secs))

    unwrapped = _unwrapped_md_wraps(mets)
    if unwrapped:
        notes.append(Finding(
            "NO_XMLDATA_WRAPPER",
            f"{unwrapped} mdWrap(s) hold their payload directly, without the mets:xmlData "
            "element the schema requires (an EPrints quirk); a save wraps them - the payload "
            "itself is preserved verbatim", unwrapped))

    unusual_admids = sum(
        1 for name in ("fileGrp", "area", "stream")
        for element in mets.iter(f"{{{METS_NS}}}{name}") if element.get("ADMID"))
    if unusual_admids:
        notes.append(Finding(
            "ADMID_OUTSIDE_SURVIVAL_INDEX",
            f"{unusual_admids} ADMID(s) sit on fileGrp/area/stream elements, which the "
            "platform's deletion reasoning does not consult; sections referenced only from "
            "there could be swept by an edit", unusual_admids))
    return unwrapped


def _dmd_referents(mets, index) -> dict[str, int]:
    """
    How many divs reference each dmdSec. A DMDID resolving to nothing is IGNORED, because
    dangling DMDIDs are by design in the platform's own skeleton; a div naming the same dmdSec
    twice still counts once.
    """
    referents: dict[str, int] = {}
    for div in mets.iter(f"{{{METS_NS}}}div"):
        dmd_id = div.get("DMDID")
        for dmd_sec in _resolve_idrefs(dmd_id, index.dmd_secs) if dmd_id else []:
            referents[dmd_sec.get("ID")] = referents.get(dmd_sec.get("ID"), 0) + 1
    return referents


def _is_foreign_dmd_sec(dmd_sec) -> bool:
    """Claims MODS (mdWrap MDTYPE="MODS") but holds no mods:mods - the EPrints root shape."""
    md_wrap = dmd_sec.find(f"{{{METS_NS}}}mdWrap")
    return md_wrap is not None and md_wrap.get("MDTYPE") == "MODS" \
        and md_wrap.find(f".//{{{MODS_NS}}}mods") is None


def _shared_editable_dmd_secs(mets, index) -> int:
    """
    MODS-editable dmdSecs referenced from more than one div. Editing metadata on one such div
    rewrites the section in place and silently changes the other div's metadata too - so a
    document with one is not safely editable. A shared FOREIGN dmdSec is fine: the platform
    never edits those (it appends its own alongside).
    """
    return sum(1 for dmd_id, count in _dmd_referents(mets, index).items()
               if count > 1 and not _is_foreign_dmd_sec(index.dmd_secs[dmd_id]))


def _unwrapped_md_wraps(mets) -> int:
    """mdWraps whose payload sits directly inside them rather than in binData/xmlData."""
    allowed = {f"{{{METS_NS}}}binData", f"{{{METS_NS}}}xmlData"}
    return sum(
        1 for md_wrap in mets.iter(f"{{{METS_NS}}}mdWrap")
        if any(isinstance(child.tag, str) and child.tag not in allowed for child in md_wrap))


def _mutations(struct_map, mets, unwrapped_md_wraps):
    """What the first save does to an EPrints-tier document, in order (CONTRACT.md)."""
    mutations = []
    root_div = struct_map.find(f"{{{METS_NS}}}div")
    file_divs = root_div.findall(f"{{{METS_NS}}}div")
    if struct_map.get("TYPE") != "PHYSICAL":
        mutations.append('set TYPE="PHYSICAL" on the structMap')
    if root_div.get("TYPE") != "Directory":
        mutations.append('set TYPE="Directory" on the root div')
    if not root_div.get("LABEL"):
        mutations.append(f'set LABEL="{ROOT_LABEL}" on the root div')
    retyped = sum(1 for div in file_divs if div.get("TYPE") != "Item")
    if retyped:
        mutations.append(f'set TYPE="Item" on {retyped} file div(s)')
    mutations.append(
        "materialise the objects Directory div (amdSec/techMD with premis:originalName) and "
        f"re-parent {len(file_divs)} file div(s) under it")
    groups = mets.findall(f"{{{METS_NS}}}fileSec/{{{METS_NS}}}fileGrp")
    if len(groups) > 1 or any(group.get("USE") != "OBJECTS" for group in groups):
        mutations.append(f'consolidate {len(groups)} fileGrp(s) into one USE="OBJECTS" group')
    if unwrapped_md_wraps:
        mutations.append(
            f"wrap the payload of {unwrapped_md_wraps} mdWrap(s) in the mets:xmlData element "
            "the schema requires")
    agents = mets.findall(f"{{{METS_NS}}}metsHdr/{{{METS_NS}}}agent/{{{METS_NS}}}name")
    if PLATFORM_AGENT not in [_text(agent) for agent in agents]:
        mutations.append("append the platform agent to metsHdr")
    return mutations
