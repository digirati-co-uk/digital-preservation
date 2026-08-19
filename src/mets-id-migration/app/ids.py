"""
Deciding whether a METS document needs migrating, without changing it.

The predicate is not "was this written before the fix" - which nothing in the document records -
but "does this document contain an ID that is not a legal xs:ID". That is the actual defect of
issue #188, it is decidable from the document alone, and it stops being true once the migration has
run, which is what lets a campaign converge and be re-run safely.

Nothing here writes. The rewriting lives in the platform's own MetsManager, reached through the
Preservation API, so that there is exactly one implementation of how an ID is spelt.
"""

import re

from lxml import etree

METS_NS = "http://www.loc.gov/METS/"
XLINK_NS = "http://www.w3.org/1999/xlink"

#: The mets:agent name the platform writes into documents it created. Only these are ours to
#: migrate: EPrints, Archivematica and Goobi documents carry their own ID schemes, which are legal
#: NCNames and were never affected by #188.
METS_CREATOR_AGENT = "University of Leeds Digital Library Infrastructure Project"

# XML 1.0 (5th edition) NameStartChar and NameChar, less the colon: an xs:ID must be an NCName.
# Spelt out rather than approximated because the difference matters in both directions - an
# accented letter is legal and must not be flagged, an ampersand is not and must be.
_NAME_START = (
    "A-Z_a-z"
    "\u00c0-\u00d6\u00d8-\u00f6\u00f8-\u02ff\u0370-\u037d\u037f-\u1fff"
    "\u200c-\u200d\u2070-\u218f\u2c00-\u2fef\u3001-\ud7ff\uf900-\ufdcf\ufdf0-\ufffd"
)
_NAME_CHAR = _NAME_START + "\\-.0-9\u00b7\u0300-\u036f\u203f-\u2040"
_NCNAME = re.compile(f"^[{_NAME_START}][{_NAME_CHAR}]*$")

#: The attributes METS types as ID or IDREF(S). ID declares one; the rest reference one.
_ID_ATTRIBUTES = ("ID", "ADMID", "DMDID", "FILEID", "STRUCTID")


def is_valid_id(value: str) -> bool:
    """Whether a string may be used as an xs:ID."""
    return bool(value) and _NCNAME.match(value) is not None


def _id_values(root) -> list[tuple[str, str]]:
    """
    Every (attribute, value) in the document where an ID may legitimately appear.

    IDREFS values are whitespace-separated lists, but a legacy ID contains spaces, so splitting
    them here would turn one invalid ID into several fragments that each look valid. The whole
    value is tested instead: for a genuine list of legal IDs the joined form is legal too, so this
    only ever reports the thing it should.
    """
    found = []
    for element in root.iter():
        if not isinstance(element.tag, str):
            continue  # comment or processing instruction
        for name in _ID_ATTRIBUTES:
            value = element.get(name)
            if value is not None:
                found.append((name, value))

        local = etree.QName(element).localname
        if local == "smLink":
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
