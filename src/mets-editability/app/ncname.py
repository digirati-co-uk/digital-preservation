"""
Whether a string is a legal xs:ID (an NCName), by the platform's own rules.

The character tables are generated from .NET's XmlConvert - the authority the platform actually
enforces - not from the XML spec, whose fifth edition accepts letters (U+0132, U+0133, U+017F)
that XmlConvert's fourth-edition tables do not. The ranges file is a copy of
src/mets-id-migration/app/ncname_ranges.json, which is pinned to XmlConvert by
NCNameRangesTests.cs on the .NET side; a test here pins this copy to that one, so all three can
never disagree silently.
"""

import json
import pathlib
import re

_RANGES = json.loads(
    (pathlib.Path(__file__).parent / "ncname_ranges.json").read_text(encoding="utf-8"))


def _char_class(ranges: list[list[int]]) -> str:
    return "".join(
        re.escape(chr(low)) if low == high else f"{re.escape(chr(low))}-{re.escape(chr(high))}"
        for low, high in ranges)


# \Z, not $: in Python, $ also matches just before a trailing newline, so an ID ending in a
# newline (reachable via a character reference in the attribute) would be judged legal here
# while XmlConvert - the authority - rejects it.
_NCNAME = re.compile(
    f"^[{_char_class(_RANGES['nameStart'])}][{_char_class(_RANGES['nameChar'])}]*\\Z")


def is_valid_id(value: str) -> bool:
    return bool(value) and _NCNAME.match(value) is not None
