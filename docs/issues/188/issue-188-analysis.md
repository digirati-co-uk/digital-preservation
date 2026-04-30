# Issue #188 — METS XML ID NCName Violation: Codebase Impact Analysis

## Background

METS XML `ID` attributes must conform to `xs:ID` / `NCName`. The codebase constructs IDs
from file paths (e.g. `PHYS_metadata/ad-hoc`, `FILE_objects/my file.pdf`), making them
schema-invalid due to `/` and spaces. The proposed fix is two steps:

- **Step 1** — Make `MetsManager` not dependent on ID form for navigation (IDs become opaque).
- **Step 2** — Mint safe IDs using `XmlConvert.EncodeLocalName(localPath)`.

Step 1 is the prerequisite that makes Step 2 backwards-compatible with existing METS files.

---

## What is already format-agnostic (unaffected by either step)

**`MetsParser.cs`** builds lookup dictionaries keyed by raw ID attribute values (`AmdSecMap`,
`FileMap`, `TechMdMap`, `DmdSecMap`, `PhysicalDivMap`) and resolves cross-references by those
same raw values. It never reconstructs a path from an ID. Already treats IDs as opaque strings.
No changes needed in either step.

**`iiif-builder` (Python)** does the same: `amd_map`, `file_map`, `tech_map` are populated
from raw `ID` attribute values and looked up by the matching `ADMID`/`FILEID` on the referencing
element. It never parses a path from an ID. Unaffected.

**`LocateMetsDivByDivId` / `FindDiv`** (MetsManager lines 378, 400) already performs a
recursive ID-equality search with no assumption about ID format. The `ByDivId` methods are
already format-agnostic. Tests calling `SetRecordInfoByDivId(fullMets, "PHYS_objects", ...)`
use IDs with no `/` — already valid NCNames, unchanged in Step 2.

**Logical structMap IDs** — client-generated `LOG_<timestamp>` values — never contained paths
or `/`. Already valid NCNames. Unaffected.

---

## Step 1: Making navigation format-agnostic

### Core change — `LocateMetsDivByLocalPath`

The single line that must change (MetsManager.cs line 364):
```csharp
// current — ID-based, depends on path encoding
var childDiv = div.Div.SingleOrDefault(d => d.Id == $"{Constants.PhysIdPrefix}{testPath}");

// replacement — navigate by premis:originalName (directories) or FLocat.Href (files)
var childDiv = FindChildDivByPath(div, testPath, fullMets);
```

The incremental walk structure (`split → build testPath → break on first miss`) **must be
preserved** because `EditMets` branches on `foundDepth` vs `totalDepth` to distinguish
add/update/error cases. A global AmdSec search cannot return partial depth information.

### Why premis:originalName is always present for directories

Every directory div added by MetsManager has `Admid` pointing to an AmdSec created with
`OriginalName = localPath`:
- `GetEmptyMets` — `metadata` and `objects` divs have AmdSec entries with `OriginalName =
  "metadata"` / `"objects"`.
- `AddNewDirectory` (line 198) — `OriginalName = localPath` always.

`localPath` is the full deposit-relative path (`"objects/subfolder"`, not just `"subfolder"`),
matching exactly what `testPath` is at each loop step. The mapping is unique — no two
directories share the same `localPath`. MetsManager guards against editing third-party METS
(`Editing_Third_Party_METS_Via_MetsManager_Returns_BadRequest`), so the guarantee holds for
all METS that reach this code.

The PHYS_ROOT div has no `Admid` but is never a *target* of navigation — it is always the
starting point.

### Files use FLocat.Href, not premis:originalName

File divs (`Type = "Item"`) have no `Admid`. Their AmdSec is on the FILE element in FileSec.
`DeleteDiv` already validates `file.FLocat[0].Href == operationPath` (line 308), confirming
that `FLocat.Href` equals `localPath`. The clean lookup for files:
```csharp
var file = fileGroup.File.FirstOrDefault(f => f.FLocat[0].Href == testPath);
var childDiv = parent.Div.FirstOrDefault(d => d.Fptr.Count > 0 && d.Fptr[0].Fileid == file?.Id);
```

### The premis:originalName extraction problem

The XmlGen in-memory typed model must be used for navigation during editing sessions — the
`XDocument` snapshot is stale as soon as any add is made. The exact property path to
`premis:originalName` inside `AmdSecType` needs verification against the XmlGen generated
classes before committing to this approach.

**Alternative — path cache in FullMets**: A `Dictionary<string, DivType>` mapping
`localPath → div`, populated at load time and maintained by `AddNewDirectory`, `AddNewFile`,
`DeleteDiv`. Lookups become O(1) and avoid XmlGen hierarchy traversal entirely. The cost is
keeping the cache in sync with edits.

### Step 1 test impact: none

No IDs change in Step 1, so no test assertions break.

---

## Step 2: Minting safe IDs

### The extra ID-minting sites beyond MetsManager

ID construction is not confined to `MetsManager.cs`. Every one of these must be updated or
the METS produced will still contain invalid IDs:

**`MetadataManager.cs` (lines 28–30):**
```csharp
var fileId = Constants.FileIdPrefix + operationPath;
var admId  = Constants.AdmIdPrefix  + operationPath;
var techId = Constants.TechIdPrefix + operationPath;
```
Mints FILE_, ADM_, TECH_ IDs for files. `operationPath` contains `/` and potentially spaces.

**`MetsFromArchivalGroup.cs` (lines 62–68 and 97–104, in `Storage.Repository.Common`):**
Constructs PHYS_, ADM_, TECH_, FILE_ IDs with the identical `PhysIdPrefix + localPath` pattern.
This is in a *different project* — easy to miss when scoping the change to `DigitalPreservation.Mets`.
It is the path by which existing Fedora/OCFL content gets a METS representation on export.

**`Constants.cs`:**
`ObjectsDivId` and `MetadataDivId` contain no `/` — already valid NCNames, unchanged.
Any `MetadataAdHocDivId` constant (as added in PR #182) must use the encoded form, not a
raw `/`.

### The hidden coupling in ModsManager.cs — most important subtlety

**Line 163:**
```csharp
Constants.DmdIdPrefix + div.Id.RemoveStart(Constants.PhysIdPrefix)
```
Derives the DMD ID from the PHYS ID by stripping `PHYS_` and prepending `DMD_`. So
`PHYS_metadata/ad-hoc` → `DMD_metadata/ad-hoc`.

After Step 2, `div.Id = "PHYS_metadata_x002F_ad-hoc"` → derived `"DMD_metadata_x002F_ad-hoc"`.
This is a valid NCName. The `DmdSec` was created with the same derivation, so the lookup in
`ModsManager.GetModsForDiv` still resolves correctly.

For **old METS files** (unencoded IDs), `div.Id` is still `"PHYS_metadata/ad-hoc"`, the
derivation gives `"DMD_metadata/ad-hoc"`, and the lookup finds the DmdSec with that same ID.
Backwards compatible.

The coupling is real and self-consistent across both formats. The risk is changing PHYS_ minting
without updating the DMD derivation, or vice versa — this must be called out explicitly in the
Step 2 PR.

Line 168 handles logical divs (`DmdIdPrefix + div.Id` directly, no stripping) — LOG_ IDs
are already valid NCNames. No issue.

### structLink FILE_ IDs

`LinkFile`, `UnLinkFile`, `SetFileLinks` (lines 671–708) construct smLink from/to IDs using
`Constants.FileIdPrefix + localPath`. Both the FILE element in FileSec and the smLink from/to
are constructed by the same code — changing the format keeps them mutually consistent.
MetsParser resolves `from`/`to` against its `FileMap` dictionary by exact string match.
No external consumer parses a path from a structLink ID.

### The ByDivId / UI round-trip

The UI receives a div ID from the server (MetsParser reads it from XML and stores it as `DivId`
on the parsed object), holds it as an opaque string, sends it back in an API call, and the server
resolves it via `FindDiv`. This round-trip is completely format-agnostic. Old METS files' divIds
are still the unencoded form; `FindDiv` matches them exactly. New METS files have encoded IDs;
same behaviour. No change required in the UI or API layer.

### Test churn in Step 2 — significant

Step 2 would break assertions across multiple test files:

**`MetsManagerPathTests.cs` (lines 158–177)** — the most revealing. Tests already assert on
IDs containing both `/` *and spaces*:
```
"FILE_objects/my file.pdf"
"ADM_objects/my file.pdf"
"PHYS_objects/my file.pdf"
"TECH_objects/my file.pdf"
```
Space is also an invalid NCName character. After `XmlConvert.EncodeLocalName`:
`FILE_objects_x002F_my_x0020_file.pdf`. Every assertion needs updating. The file covers
complex nested paths and archive structures — significant rewrite.

**`MetsManagerSyncTests.cs` (lines 220–237)** — asserts on full FILE_, PHYS_, ADM_, TECH_ IDs
for path-containing elements.

**`MetsManagerLogicalStructTests.cs` (lines 157, 257, 308)** — asserts on
`FILE_objects/page.tif`, `FILE_objects/audio.wav` inside FILEID attributes of fptr elements
in the logical structMap. The logical structMap uses safe IDs, but its fptrs reference physical
FILE_ IDs which change format.

**`MetsManagerDeepStructureTests.cs` (lines 551–572)** — the `AssertDirectoryDiv` helper
hardcodes `$"PHYS_{localPath}"` and `$"ADM_{localPath}"` with unencoded paths. Must change to
`$"PHYS_{localPath.ToNCName()}"` etc.

**`PhysicalStructureTests.cs` (lines 18–42, 105–106)** — raw XML fixture strings with
`ADM_objects/img.png`, `TECH_objects/img.png`, `PHYS_objects/file.txt`. Must update.

**PR #182 tests** — check ad-hoc div IDs via `AssertBuiltInDirectoryDiv`. Also affected.

Tests asserting on `"PHYS_objects"`, `"PHYS_metadata"` (no path separator) remain valid — these
are already valid NCNames.

---

## Summary table

| Component | Step 1 impact | Step 2 impact |
|---|---|---|
| `MetsManager.cs` — `LocateMetsDivByLocalPath` | **Core change** | ID minting lines also update |
| `MetsManager.cs` — `AddNewFile`, `AddNewDirectory` | None | Update ID minting |
| `MetadataManager.cs` (lines 28–30) | None | **Update ID minting** |
| `ModsManager.cs` line 163 (DMD derivation) | None | Self-consistent after change; must be documented |
| `MetsFromArchivalGroup.cs` — separate project | None | **Must update — easy to miss** |
| `Constants.cs` | None | `MetadataAdHocDivId` if added needs encoding |
| `MetsParser.cs` | None | None — already opaque |
| `iiif-builder` (Python) | None | None — already opaque |
| `LocateMetsDivByDivId` / `FindDiv` | None | None — already format-agnostic |
| Logical structMap / `logical-structmap.js` | None | None — LOG_ IDs already valid |
| structLink FILE_ IDs | None | Update minting; lookups remain consistent |
| Test files with path-containing ID assertions | None | **Significant churn across 5+ files** |
| Test `ByDivId` calls with `"PHYS_objects"` | None | None — no `/` in those IDs |
| UI / API layer | None | None — opaque round-trip |
