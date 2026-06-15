# Effective Metadata Inheritance

This document describes how `EffectiveAccessRestrictions`, `EffectiveRightsStatement`, and
`EffectiveRecordInfo` are computed for physical files, physical directories, and logical ranges
by `MetsParser`. These are the values used downstream — by the IIIF builder, by access-control
checks, and by deposit validation.

See `EffectiveMetadataInheritanceTests` for unit tests of each rule.

---

## Background: METS structure

A METS file has two structMaps that matter here:

- **Physical structMap** (`TYPE="PHYSICAL"`): mirrors the deposit's actual directory layout.
  Every file has a physical `div`, and every directory has a physical `div`. Access conditions
  and record identifiers that are "about" the file as a physical object live here, attached via
  `DMDID` to a MODS descriptor.

- **Logical structMap** (`TYPE="LOGICAL"`): describes the *archival* structure — how the
  archivist conceptually groups the content (interviews, chapters, items, sub-series). A logical
  `div` (called a "logical range" in this codebase) represents an archival entity and may
  reference files either as whole-file `fptr` elements, as time-segment or image-region `area`
  elements, or (in Goobi METS) via a separate `structLink` section.

---

## Inheritance chains

### Physical files — Access Restrictions

| Step | Source | Condition |
|------|--------|-----------|
| A1 | File's own DMDID (`accessCondition type="restriction on access"`) | Own value present |
| A2 | Walk up the physical directory tree | Parent or ancestor has a value |
| A3 | *(physical always beats logical — A4 is only reached if A1+A2 yield nothing)* | |
| A4a | The file's single whole-file fptr logical range | Physical silent; file in exactly **one** range via fptr |
| A4b | The file's deepest structLink-associated logical range | Physical silent; Goobi smLink mapping exists |
| A5 | `[]` (empty list) | No source found — **validation warning** |

**Physical takes precedence (A3).** If the physical tree has any access value — even inherited
from a grandparent directory — the logical structure is ignored entirely. Logical access is
a fallback of last resort, not an override.

**The "exactly one range" condition (A4a).** A file may appear in multiple logical ranges
(e.g. the same page image used by two chapters). If those ranges have different access
conditions there is no safe answer, so the fallback is skipped and the file gets empty access
(A5). This is a data-quality problem that should be surfaced by validation.

**A5 is a warning state.** A file with empty effective access in a deposit where other files
have access declared is almost certainly a mistake — either a file was accidentally omitted
from a logical range, or the physical root was cleared without ensuring full logical coverage.
The Deposit UI should warn on this condition when any other file in the deposit has access set.

### Physical files — Rights Statement

| Step | Source | Condition |
|------|--------|-----------|
| 1 | File's own DMDID (`accessCondition type="use and reproduction"`) | Own value present, or div had the element even if empty/invalid |
| 2 | Walk up the physical directory tree | Parent or ancestor has a value |
| 3 | The file's single whole-file fptr logical range | Physical silent; file in exactly one range via fptr |
| 4 | `null` | No source found |

Rights follow the same physical-first, logical-fallback logic as access. The "explicit null"
edge case (step 1): if a file's physical div has a `use and reproduction` element with an
unrecognised or empty value, the file's own (null) rights are used rather than inheriting —
the explicit presence of the element is treated as an intentional override.

### Physical files — Record Info

| Step | Source | Condition |
|------|--------|-----------|
| R1 | File's own DMDID (`recordInfo`) | Own value present |
| R2 | The file's single whole-file fptr logical range (effective) | File in exactly **one** range via fptr (no `area`) **and that range has a non-null effective RecordInfo** |
| R3 | *(falls through if referenced by area, or by multiple whole-file fptrs)* | |
| R4 | structLink-associated logical range | Goobi smLink mapping exists **and that range has a non-null effective RecordInfo** |
| R5 | Walk up the physical directory tree | |

Record info uses logical inheritance **ahead** of the physical tree walk (unlike access/rights,
where the physical tree always comes first). The reason: a file's RecordInfo identifies the
archival entity the file *represents* — for a single-interview recording, the logical range
IS the catalogue record. For a tape side that spans multiple interviews, no single range can
claim it, so the fallback to physical (`objects/` → collection identifier) is correct.

**Area references (R3).** A `mets:area` element refers to only *part* of a file (a time
segment or image region). A file referenced only via area elements cannot belong to a
single archival entity as a whole, so the logical inheritance step is skipped.

**The logical range must actually have a RecordInfo (R2, R4).** Logical inheritance only
overrides the physical walk when the range has something to assert. If a file's single fptr
range (or its structLink-associated range) has a **null** effective RecordInfo — no record
info of its own and none inherited from a parent logical range — that null does **not**
override a real RecordInfo asserted by a physical ancestor. In that case R2/R4 are skipped
and the file falls through to the physical tree walk (R5). Logical inheritance is a way for a
range to *supply* a more specific archival identity than the physical tree, not a way for an
empty range to *erase* one.

---

## Physical directories

Directories inherit values by simple upward walk:

- `EffectiveAccessRestrictions`: own value if present, else parent's effective value.
- `EffectiveRightsStatement`: own value if present, else parent's effective value.
- `EffectiveRecordInfo`: own value if present, else parent's effective value.

There is no logical fallback for directories.

---

## Logical ranges

A logical range represents an archival entity (e.g. an interview, a digitised item, a
sub-series). Its effective access and rights describe the access policy for that entity —
used when building IIIF manifests, and as the fallback source when files' physical trees
are silent (rules A4a and step 3 for rights above).

| Value | Source |
|-------|--------|
| Access | Own explicit value, else `DMD_PHYS_ROOT` access (the physical structMap root div) |
| Rights | Own explicit value, else `DMD_PHYS_ROOT` rights |
| RecordInfo | Own explicit value, else parent logical range's effective RecordInfo |

**Why `DMD_PHYS_ROOT` and not `objects/`?** `objects/` is a physical layout convention —
it tells you where files live on disk, not what collection they belong to. `DMD_PHYS_ROOT`
is the collection-level descriptor, which is the appropriate default for a logical entity
that has no more specific access declared. Logical ranges do **not** inherit from `objects/`
or any other intermediate physical directory.

---

## Common patterns

### Leeds standard (access on objects/)

All files are Open. Access is declared once on `objects/`.

```
Physical: __ROOT → objects/ [Level1, InC] → file1, file2, ...
Logical:  range → fptr→file1   (inherits RecordInfo from range)
```

`file1.EffectiveAccessRestrictions` = `["Level1"]` via A2 (physical walk).  
`file1.EffectiveRecordInfo` = range's RecordInfo via R2 (single fptr range).

### Women of Westminster (per-file physical access override)

Most files are Open (`objects/`), but one file is Closed (own physical DMDID).

```
Physical: objects/ [Level1] → amber-rudd.m4a, angela-eagle.m4a [Closed]
Logical:  Amber Rudd range → fptr→amber-rudd.m4a
          (angela-eagle.m4a absent from logical map)
```

`amber-rudd.m4a.EffectiveAccessRestrictions` = `["Level1"]` via A2.  
`angela-eagle.m4a.EffectiveAccessRestrictions` = `["Closed"]` via A1 (own value wins).  
`angela-eagle.m4a.EffectiveRecordInfo` = `objects/` RecordInfo via R5 (not in logical map).

### Liddle tapes (area references, no per-file record info)

Each WAV file spans multiple interviews. Logical ranges use `mets:area` time segments.

```
Physical: objects/ [Level1, collection RecordInfo] → tape1.wav, tape2.wav
Logical:  Interview A → area(tape1.wav 00:00–00:35)
          Interview B → area(tape1.wav 00:35–01:10)
```

`tape1.wav.EffectiveRecordInfo` = `objects/` RecordInfo via R5 (area refs skip R2).

### Goobi METS (structLink, access in logical DMDs)

Physical tree has no access. Logical divs declare access. `structLink` connects them.

```
Physical:  PHYS_0001, PHYS_0002 (no access anywhere)
Logical:   LOG_0000 [Open] → smLink→PHYS_0001
           LOG_0001 [Restricted] → smLink→PHYS_0002
structLink: LOG_0000→PHYS_0001, LOG_0001→PHYS_0002
```

`file-in-PHYS_0001.EffectiveAccessRestrictions` = `["Open"]` via A4b (structLink deepest-only).

### Leeds-native logical access (fptr, physical silent)

100 files. No access on `objects/`. All access declared in logical ranges, every file
covered by exactly one range.

```
Physical: objects/ [no access] → file1...file90, file91...file100
Logical:  "Open" range  [dlip-open]   → fptr→file1 ... fptr→file90
          "Closed" range [dlip-4-closed] → fptr→file91 ... fptr→file100
```

`file1.EffectiveAccessRestrictions` = `["dlip-open"]` via A4a (single fptr range, physical silent).  
`file91.EffectiveAccessRestrictions` = `["dlip-4-closed"]` via A4a.

**Risk:** if `file101` is added to the deposit but forgotten in the logical map, it gets
empty effective access (A5) with no safety net. The Deposit UI must warn on this condition.

---

## Impact on existing behaviour

The fptr-based access/rights fallback (A4a) is a new code path added alongside the existing
structLink fallback (A4b). It can only trigger when the entire physical tree from file to
root has no access condition. All existing Leeds-native deposits set access on `objects/` or
on individual file divs, so A4a is never reached in those deposits — **no existing behaviour
changes**.

The only way to reach A4a is to deliberately remove all access from the physical tree and
rely entirely on logical ranges. This is a conscious authoring decision and is exactly the
scenario `leeds-mets-version.xml` is designed to test.
