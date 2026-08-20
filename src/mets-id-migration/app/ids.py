"""
Deciding whether a METS document needs migrating, without changing it.

The predicate is not "was this written before the fix" - which nothing in the document records -
but "does this document contain an ID that is not a legal xs:ID". That is the actual defect of
issue #188, it is decidable from the document alone, and it stops being true once the migration has
run, which is what lets a campaign converge and be re-run safely.

Nothing here writes. The rewriting lives in the platform's own MetsManager, reached through the
Preservation API, so that there is exactly one implementation of how an ID is spelt.
"""

import json
import pathlib
import re

from lxml import etree

METS_NS = "http://www.loc.gov/METS/"
XLINK_NS = "http://www.w3.org/1999/xlink"

#: The mets:agent name the platform writes into documents it created. Only these are ours to
#: migrate: EPrints, Archivematica and Goobi documents carry their own ID schemes, which are legal
#: NCNames and were never affected by #188.
METS_CREATOR_AGENT = "University of Leeds Digital Library Infrastructure Project"

# What counts as a legal NCName character, taken from the platform rather than from the spec.
#
# The distinction matters. .NET's XmlConvert - which is what MetsIds.IsValidId uses, and therefore
# what the platform actually enforces - implements XML 1.0 FOURTH edition name rules, whose letter
# tables are enumerated and have gaps: U+0132, U+0133 and U+017F are all letters that the fifth
# edition's NameStartChar production accepts and XmlConvert does not. Writing the modern production
# here would make this tool call such an ID legal while the platform still rejects it, and the
# survey would then report an Archival Group as conforming when its METS is still invalid - the one
# failure a migration campaign cannot afford, because it looks like success.
#
# So the ranges are generated from XmlConvert itself and checked against it by a test on the .NET
# side, which is where the authority is. See ncname_ranges.json.
_RANGES = json.loads(
    (pathlib.Path(__file__).parent / "ncname_ranges.json").read_text(encoding="utf-8"))


def _char_class(ranges: list[list[int]]) -> str:
    return "".join(
        re.escape(chr(low)) if low == high else f"{re.escape(chr(low))}-{re.escape(chr(high))}"
        for low, high in ranges)


_NCNAME = re.compile(
    f"^[{_char_class(_RANGES['nameStart'])}][{_char_class(_RANGES['nameChar'])}]*$")

#: Attributes holding exactly one ID: ID declares one, FILEID references one.
_SINGLE_ID_ATTRIBUTES = ("ID", "FILEID")

#: Attributes holding IDREFS - a whitespace-separated list of IDs. Ambiguous in a legacy document,
#: because a legacy ID can itself contain spaces; see _idrefs_values.
_IDREFS_ATTRIBUTES = ("ADMID", "DMDID", "STRUCTID")


def is_valid_id(value: str) -> bool:
    """Whether a string may be used as an xs:ID."""
    return bool(value) and _NCNAME.match(value) is not None


def _declared_ids(root) -> set[str]:
    """Every value of an ID attribute in the document - the IDs something could refer to."""
    return {element.get("ID") for element in root.iter()
            if isinstance(element.tag, str) and element.get("ID") is not None}


def _idrefs_values(value: str, declared: set[str]) -> list[str]:
    """
    What one IDREFS attribute actually names.

    This is genuinely ambiguous in a legacy document, and getting it wrong goes both ways:
    `ADMID="ADM_a ADM_b"` is two legal references, while `ADMID="ADM_objects/my file.pdf"` is ONE
    legacy ID that merely looks like several. Testing the whole value would flag the first (a false
    positive); splitting on whitespace would pass the second off as the fragments `ADM_objects/my`
    and `file.pdf` - and where the path has no `/` in it, both fragments are legal and the document
    would be missed entirely.

    Resolve it the way the platform does (issue #213): try the whole value as a single ID first,
    because only a legacy ID can look like that, and accept that reading only if the document
    actually declares such an ID. Otherwise it is a list, and each token stands on its own.
    """
    if value in declared:
        return [value]
    return value.split()


def _id_values(root) -> list[tuple[str, str]]:
    """Every (attribute, value) in the document where an ID may legitimately appear."""
    declared = _declared_ids(root)
    found = []
    for element in root.iter():
        if not isinstance(element.tag, str):
            continue  # comment or processing instruction

        for name in _SINGLE_ID_ATTRIBUTES:
            value = element.get(name)
            if value is not None:
                found.append((name, value))

        for name in _IDREFS_ATTRIBUTES:
            value = element.get(name)
            if value is not None:
                found.extend((name, one) for one in _idrefs_values(value, declared))

        local = etree.QName(element).localname
        if local == "smLink":
            # xlink:from and xlink:to are single IDREFs. (smArcLink has the same attribute names
            # but they hold xlink labels, which are not IDs - so it is not listed here.)
            for end in ("from", "to"):
                value = element.get(f"{{{XLINK_NS}}}{end}")
                if value is not None:
                    found.append((f"xlink:{end}", value))
        elif local == "smLocatorLink":
            href = element.get(f"{{{XLINK_NS}}}href")
            if href and href.startswith("#"):
                found.append(("xlink:href", href[1:]))
    return found


def parse(mets_xml: bytes):
    """Parse a METS document. Raises lxml.etree.XMLSyntaxError if it is not XML."""
    return etree.fromstring(mets_xml)


def creator_agent(root) -> str | None:
    """The name of the CREATOR agent in the METS header, or None if there isn't one."""
    for agent in root.iterfind(f".//{{{METS_NS}}}metsHdr/{{{METS_NS}}}agent"):
        if agent.get("ROLE") != "CREATOR":
            continue
        name = agent.find(f"{{{METS_NS}}}name")
        if name is not None and name.text:
            return name.text.strip()
    return None


def invalid_ids(root) -> list[str]:
    """
    Every ID-typed value in the document that is not a legal xs:ID, deduplicated and in document
    order. An empty list means the document conforms and needs no new version.
    """
    seen = set()
    invalid = []
    for _, value in _id_values(root):
        if value in seen or is_valid_id(value):
            continue
        seen.add(value)
        invalid.append(value)
    return invalid


def offending_characters(invalid: list[str]) -> str:
    """
    The distinct characters that make these IDs illegal, in a short readable form like "/ ( ) SPACE".

    Recorded per document because one of them decides something after the migration. Almost every
    invalid ID is invalid because of `/`; a SPACE is the harder case, and the only reason the joined
    tier in `IdRefs` exists - an ID containing a space arrives from the XmlSerializer already split
    into several tokens. Knowing whether production has any is what says whether that tier can be
    retired once the corpus conforms.
    """
    offenders = set()
    for value in invalid:
        for index, char in enumerate(value):
            legal = is_valid_id(char) if index == 0 else is_valid_id("a" + char)
            if not legal:
                offenders.add(char)
    return " ".join("SPACE" if c == " " else c for c in sorted(offenders))


def file_digests(root) -> dict[str, str]:
    """
    Path to SHA256 for every file the document lists, taken from the fileSec: the FLocat href for
    the path, and the premis:messageDigest of the amdSec its ADMID names for the digest.

    This is the before-and-after fingerprint the migration verifies itself against. A rename of
    identifiers must leave it identical; anything else means the migration did more than it said.
    """
    premis_ns = "http://www.loc.gov/premis/v3"
    digests_by_amd_id = {}
    for amd_sec in root.iterfind(f".//{{{METS_NS}}}amdSec"):
        amd_id = amd_sec.get("ID")
        digest = amd_sec.find(f".//{{{premis_ns}}}messageDigest")
        if amd_id and digest is not None and digest.text:
            digests_by_amd_id[amd_id] = digest.text.strip()

    result = {}
    for file_element in root.iterfind(f".//{{{METS_NS}}}fileSec//{{{METS_NS}}}file"):
        locator = file_element.find(f"{{{METS_NS}}}FLocat")
        if locator is None:
            continue
        href = locator.get(f"{{{XLINK_NS}}}href")
        if not href:
            continue
        # ADMID is compared whole first, for the same reason the platform does: a legacy ID
        # containing spaces is one reference, not several.
        admid = file_element.get("ADMID") or ""
        digest = digests_by_amd_id.get(admid)
        if digest is None:
            for token in admid.split():
                if token in digests_by_amd_id:
                    digest = digests_by_amd_id[token]
                    break
        result[href.strip()] = digest or ""
    return result
