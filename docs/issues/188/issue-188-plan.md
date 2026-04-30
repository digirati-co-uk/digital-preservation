# Issue #188 — METS XML ID Fix: Sequenced Implementation Plan

> Produced by Claude Opus 4.7, based on analyses in `issue-188-analysis.md` and `issue-188-opus-review.md`.

## Goal

Make every METS `xs:ID` attribute schema-valid (NCName-conformant) without breaking navigation,
round-trip parsing, or backward compatibility with already-deposited METS files. The fix is
sequenced into three steps with explicit branch dependencies, deploy coordination, and acceptance
criteria.

## Top-level sequencing

1. **Step 1 PR — `feat/188-physical-divs-cache`**: introduce `PhysicalDivsByPath` on `FullMets`,
   populated by the parser and maintained by every mutation. Decouples navigation from ID format.
   Pure refactor: zero behavioural change in the produced XML, zero test assertion changes.
2. **Step 2 PR — `feat/188-encoded-mets-ids`** (branched off Step 1, NOT off main): switch all ID
   minting to `XmlConvert.EncodeLocalName`. Test assertions for path-containing IDs change. Deploy
   `Pipeline.API` and `Preservation.API` atomically.
3. **Step 3 (optional, post-Step 2)**: bulk legacy migration. Recommendation: **defer, with a
   documented decision**.

The two PRs MUST be merged in order. Step 2 cannot ship before Step 1 is in production because
`LocateMetsDivByLocalPath` would silently miss the encoded IDs of any in-flight new content
otherwise. Both must ship before any bulk migration.

A side change is also covered:
- `VirusProvEventPrefix` substring lookup fragility — fix in Step 1 PR (small, one-line, latent
  bug exposed by encoded IDs).

---

## Step 1 — Decouple navigation from ID format

### 1.1 Approach

Introduce a per-`FullMets` path cache:

```csharp
public Dictionary<string, DivType> PhysicalDivsByPath { get; } = new();
```

Keys are the same `localPath` strings the rest of the system already uses (deposit-relative,
BagIt `data/` already stripped — see 1.6). Values are the typed XmlGen `DivType` instances inside
the physical structMap. Lookups are O(1); partial-depth resolution is preserved naturally (a
missing key returns null, the walk breaks — same shape as the current code).

The PHYS_ROOT div is intentionally NOT in the cache. It is always the implicit starting point of
`LocateMetsDivByLocalPath`, never a target.

### 1.2 Files to change

| File | Change |
|---|---|
| `DigitalPreservation.Mets/FullMets.cs` | Add `PhysicalDivsByPath` (Dictionary<string, DivType>, default `new()`) |
| `DigitalPreservation.Mets/MetsParser.cs` | New helper `BuildPhysicalDivsByPath(...)` populated during the typed-model load path |
| `DigitalPreservation.Mets/StorageImpl/FileSystemMetsStorage.cs` | After deserialization, call the populator and assign `fullMets.PhysicalDivsByPath` |
| `DigitalPreservation.Mets/MetsManager.cs` | (1) Rewrite `LocateMetsDivByLocalPath`. (2) `AddNewFile`/`AddNewDirectory` add to cache. (3) `DeleteDiv` removes from cache. (4) `GetEmptyMets` bootstraps the two well-known entries. |
| `Storage.Repository.Common/Mets/MetsFromArchivalGroup.cs` | After building the structMap, call the same `PopulateCache` helper. |

### 1.3 Population (parser side, typed model)

Single recursive descent over the in-memory typed `Mets` object — NOT XDocument. XDocument is a
snapshot at load time; any adds made during an editing session are in the XmlGen model but not yet
reflected in XDocument. Navigation during `EditMets` must use the typed model.

Note: the XmlGen typed model exposes the PREMIS payload inside `AmdSecType.TechMd[0].MdWrap.XmlData`
only as `XmlElement[] Any` (raw XML). `premis:originalName` must therefore be extracted via a
small XPath/descendant query on the `XmlElement` — not a fully typed property chain. This is why
the path-cache approach is preferred over inline premis:originalName traversal at navigation time.

```csharp
void Walk(DivType div, FullMets full)
{
    foreach (var child in div.Div)
    {
        string? key = null;

        if (child.Type == Constants.DirectoryType)
        {
            // follow Admid → AmdSec → premis:originalName
            if (child.Admid.Count > 0)
            {
                var amdSec = full.Mets.AmdSec.FirstOrDefault(a => a.Id == child.Admid[0]);
                key = ExtractPremisOriginalName(amdSec);   // XmlElement descendant query
            }
        }
        else if (child.Type == Constants.ItemType && child.Fptr.Count > 0)
        {
            // files: FLocat.Href on the matching FILE in the OBJECTS fileGrp
            var fileId = child.Fptr[0].Fileid;
            var grp = full.Mets.FileSec?.FileGrp.FirstOrDefault(g => g.Use == "OBJECTS");
            var file = grp?.File.FirstOrDefault(f => f.Id == fileId);
            key = file?.FLocat.FirstOrDefault()?.Href;
        }

        if (key != null)
        {
            key = NormalisePathKey(key);   // see 1.6
            full.PhysicalDivsByPath[key] = child;
        }

        Walk(child, full);
    }
}
```

### 1.4 Where the cache is built / refreshed

| Entry point | Action |
|---|---|
| `IMetsStorage.GetFullMets` | After XmlSerializer deserialization, build the cache. This is the single non-mutation read path that turns a stored METS into a `FullMets`. |
| `MetsManager.GetEmptyMets` (called from `CreateStandardMets`) | Bootstrap with the two well-known entries (`"objects"` → child div, `"metadata"` → child div). |
| `MetsFromArchivalGroup.CreateStandardMets` | Same bootstrap then `PopulateCache(fullMets)` once after `AddResourceToMets` returns. |

A `MetsCache.PopulateFrom(Mets mets, Dictionary<string, DivType> cache)` static helper keeps the
populator in one place. `IMetsParser` does NOT need a new public method — populating happens at
the storage boundary.

### 1.5 `LocateMetsDivByLocalPath` rewrite

Replace the current body (MetsManager.cs lines 346–376) with:

```csharp
private static (DivType contextDiv, DivType? parent, int foundDepth, int totalDepth)
    LocateMetsDivByLocalPath(FullMets fullMets, string localPath)
{
    var elements = localPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
    var div = fullMets.Mets.StructMap.Single(sm => sm.Type == Constants.Physical).Div!;
    DivType? parent = null;
    var testPath = string.Empty;
    var counter = 0;

    foreach (var element in elements)
    {
        if (testPath.HasText()) testPath += "/";
        testPath += element;

        if (!fullMets.PhysicalDivsByPath.TryGetValue(testPath, out var childDiv))
            break;

        // Guard against cache drift from a malformed source that re-uses the same
        // premis:originalName in two unrelated subtrees.
        if (!div.Div.Contains(childDiv))
            break;

        counter++;
        parent = div;
        div = childDiv;
    }

    return (div, parent, counter, elements.Length);
}
```

The direct-child guard defends against the edge case flagged in the Opus review: if two divs ever
share the same `premis:originalName`, the old `SingleOrDefault` would throw; the new code returns
null safely (foundDepth < totalDepth → "not all parts of the path have been added" error, same
message as today).

### 1.6 Path normalisation before comparison

`premis:originalName` may come from legacy sources. Before inserting into or looking up from the
cache:

- Strip a leading `data/` prefix (`FolderNames.RemovePathPrefix` already exists, FolderNames.cs:21)
- Strip a leading `./`
- Trim a trailing `/`
- Reject empty strings (treat as no-op)

`MetsManager` already calls `FolderNames.RemovePathPrefix` on the incoming `localPath` (line 101),
so cache lookups and writes go through the same normalisation. The populator must do the same on
the value extracted from `premis:originalName` BEFORE inserting into the dictionary.

### 1.7 Cache maintenance on mutations

| Mutation | Cache update |
|---|---|
| `AddNewFile` (line 157) | After `parentDiv.Div.Add(childItemDiv)`: `fullMets.PhysicalDivsByPath[localPath] = childItemDiv;` |
| `AddNewDirectory` (line 180) | After `parentDiv.Div.Add(childDirectoryDiv)`: `fullMets.PhysicalDivsByPath[localPath] = childDirectoryDiv;` |
| `DeleteDiv` (line 296) | Before `parent!.Div.Remove(div)`: `fullMets.PhysicalDivsByPath.Remove(operationPath!);` |
| `UpdateExistingFile`, `UpdateExistingDirectory` | No-op — div identity does not change |
| `MetsFromArchivalGroup.AddResourceToMets` | Inline maintenance preferred; plus a debug-build assertion that a final `PopulateCache` matches |

Add a `[Conditional("DEBUG")]` assert at the entry to `LocateMetsDivByLocalPath` that the cache
equals a re-build. This catches any mutation path that forgets to update the cache.

### 1.8 VirusProvEventPrefix substring lookup (fix in Step 1 PR)

`MetsParser.cs` (around line 506):

```csharp
// current — fragile substring match
var matchingKey = lookupMaps.DigiprovMdMap.Keys
    .FirstOrDefault(k => k.ToLower().Contains(lowerKey));

// fix — exact match
var matchingKey = lookupMaps.DigiprovMdMap.Keys
    .FirstOrDefault(k => string.Equals(k, clamavKey, StringComparison.OrdinalIgnoreCase));
```

After Step 2, encoded paths can be substrings of each other (`ADM_a` is a substring of
`ADM_a_x002F_b`), making this a real cross-talk risk. Fix in Step 1 so it ships before Step 2.

Add a regression test constructing a digiprovMd map where one key is a substring of another,
asserting the lookup picks the exact entry.

### 1.9 Step 1 acceptance criteria

- All existing unit tests pass with zero assertion changes (Step 1 produces byte-identical METS
  output to today)
- New unit tests cover:
  - Cache populated correctly from a parser load (file div, directory div, nested directory)
  - Cache survives `AddNewFile`, `AddNewDirectory`, `DeleteDiv` (round-trip: parse → cache equals
    freshly built cache)
  - Path normalisation: a fixture METS with `premis:originalName="data/objects/foo"` resolves
    correctly under the deposit path `objects/foo`
  - Defensive lookup: a fixture with two divs sharing the same `premis:originalName` returns the
    direct child of the current div, not a sibling
- New regression test for `VirusProvEventPrefix` exact-match lookup
- No change in produced XML for any existing fixture
- Code review checklist: every mutation method updates the cache; every existing mutation has been
  audited

### 1.10 Step 1 risks

- **Dead-code window**: between Step 1 deploy and Step 2 deploy, the new lookup produces results
  identical to the old `Id == "PHYS_" + path` walk (all existing IDs are still path-based). The
  cache invariants must be exhaustively unit-tested because production traffic does not exercise
  the encoded-ID path until Step 2.
- **Cache drift**: if any future code path mutates `Mets.StructMap` / `Mets.FileSec` /
  `Mets.AmdSec` outside `MetsManager` and `MetsFromArchivalGroup`, the cache will go stale.
  Mitigation: keep `MetsCache.PopulateFrom` public, document its use, and add the debug-build
  assertion described in 1.7.

---

## Step 2 — Mint safe IDs

### 2.1 Approach

A single `ToMetsId` extension method wraps `XmlConvert.EncodeLocalName`. Every ID-minting site
uses it. The encoding handles spaces, `/`, ampersands, and any other NCName-invalid character.
The round-trip inverse is `XmlConvert.DecodeName` — no current consumer needs it, but it is
documented in the same class.

```csharp
// new file: src/DigitalPreservation/DigitalPreservation.Utils/MetsIdEncoding.cs
namespace DigitalPreservation.Utils;

public static class MetsIdEncoding
{
    /// <summary>
    /// Encodes a local path into a string safe for use inside an xs:ID / NCName attribute.
    /// Handles '/', spaces, ampersands, leading digits, and all other NCName-invalid characters.
    /// Bijective with <see cref="DecodeMetsId"/>. Does NOT add a prefix — callers concatenate
    /// Constants.PhysIdPrefix etc. as today.
    /// </summary>
    public static string ToMetsId(this string localPath)
        => System.Xml.XmlConvert.EncodeLocalName(localPath);

    /// <summary>Round-trip inverse of <see cref="ToMetsId"/>.</summary>
    public static string DecodeMetsId(this string id)
        => System.Xml.XmlConvert.DecodeName(id);
}
```

### 2.2 Every ID-minting site that must change

| File | Lines | Change |
|---|---|---|
| `MetsManager.cs` | 159–160 (`AddNewFile`) | `physId = Constants.PhysIdPrefix + localPath.ToMetsId();` `fileId = Constants.FileIdPrefix + localPath.ToMetsId();` |
| `MetsManager.cs` | 182–184 (`AddNewDirectory`) | `physId/admId/techId = <prefix> + localPath.ToMetsId();` |
| `MetsManager.cs` | 261–270 (`GetEmptyMets`, metadata/objects bootstrap) | `Dmdid` and `Admid` use `.ToMetsId()`. Note: `"objects"` and `"metadata"` are already valid NCNames so this is a no-op for these specific values — use `.ToMetsId()` anyway for consistency. |
| `MetsManager.cs` | 282, 287 (`AmdSec` for objects/metadata) | Same — encode `FolderNames.Objects` / `FolderNames.Metadata`. |
| `MetsManager.cs` | 601 (`BuildFptr`) | `fileId = Constants.FileIdPrefix + fp.LocalPath.ToMetsId();` |
| `MetsManager.cs` | 676–677, 686–687, 701 (`LinkFile`, `UnLinkFile`, `SetFileLinks`) | All `Constants.FileIdPrefix + path` constructions use `.ToMetsId()`. |
| `MetadataManager.cs` | 28–30 (`ProcessAllFileMetadata`) | `fileId/admId/techId = <prefix> + operationPath.ToMetsId();` |
| `ModsManager.cs` | 161–169 (`GetModsForDiv`) | **Refactor** — see 2.3 below. |
| `MetsFromArchivalGroup.cs` | 62–68, 97–104 | All four prefix concatenations use `.ToMetsId()`. |
| `Constants.cs` | 17–18 | `ObjectsDivId` and `MetadataDivId` already valid NCNames — no functional change. Add a comment that any new path-derived constant MUST use `.ToMetsId()`. If `MetadataAdHocDivId` exists by the time Step 2 lands, encode it. |

### 2.3 ModsManager refactor — eliminate the PHYS/DMD coupling

**Current (fragile — derives DMD ID by stripping PHYS_ from `div.Id`):**
```csharp
div.Dmdid.Add(Constants.DmdIdPrefix + div.Id.RemoveStart(Constants.PhysIdPrefix));
```

**Replace with (derive from `localPath` directly):**
```csharp
// Physical structMap divs pass localPath — mint DMD_ from the same encoded localPath used
// for PHYS_, decoupling DMD from the textual form of div.Id.
// Logical structMap divs pass localPath=null and use div.Id (LOG_… already a valid NCName).
public static ModsDefinition? GetModsForDiv(
    Mets mets, DivType div, bool createDmd = false, string? localPath = null)
{
    if (div.Dmdid.Count == 0 && createDmd)
    {
        var encoded = localPath != null ? localPath.ToMetsId() : div.Id;
        div.Dmdid.Add(Constants.DmdIdPrefix + encoded);
    }
    ...
}
```

Thread `localPath` through from call sites. Audit:

- `PopulateDmdFromResource` in MetsManager is called from `AddNewFile`, `AddNewDirectory`,
  `UpdateExistingFile`, `UpdateExistingDirectory`, `EditMets` — all have the path. Add a
  `localPath` parameter and thread it through.
- `SetRecordInfoForDiv`, `SetRightsStatementForDiv`, `SetAccessRestrictionsForDiv`,
  `SuppressRightsInheritanceForDiv` — reached from both `…ByPath` (has path) and `…ByDivId`
  (operates on logical divs, `localPath` stays null, `div.Id` is a valid LOG_… NCName).
- `BuildLogicalDiv` — `localPath` stays null; `div.Id` used directly (already valid NCName).

For backwards compatibility: the read path (when `createDmd:false`) resolves via `div.Dmdid`
exactly as today — no change. Legacy METS with raw-`/` DMD IDs continue to be readable.

### 2.4 Tests — what changes, what stays, what is frozen, what is added

#### Assertions that change (path-containing IDs)

Do NOT inline literal encoded strings in assertions — always compute via `.ToMetsId()` so the test
breaks if the encoding ever changes.

| File | Lines | Old form | New form |
|---|---|---|---|
| `MetsManagerPathTests.cs` | 158, 159, 167, 171, 175, 177, 256–259, 287–292, 314–325, 380–383 | `"FILE_objects/my file.pdf"` | `$"FILE_{"objects/my file.pdf".ToMetsId()}"` |
| `MetsManagerSyncTests.cs` | 220–237, 287–294, 366–371, 408–410 | `"PHYS_objects/doc.tif"` | Via `MetsId(localPath, prefix)` test helper |
| `MetsManagerLogicalStructTests.cs` | 157, 257, 308 | `"FILE_objects/page.tif"` etc. | Encoded form |
| `MetsManagerDeepStructureTests.cs` | 551–572 | `$"PHYS_{localPath}"`, `$"ADM_{localPath}"` | `$"PHYS_{localPath.ToMetsId()}"` etc. |
| `PhysicalStructureTests.cs` | 18–42, 105–106 | Raw XML fixture strings | Update fixture strings to encoded form |
| PR #182 ad-hoc div tests | TBD | `"PHYS_metadata/ad-hoc"` | Encoded |

#### Assertions that do NOT change

- `…ByDivId` calls passing no-`/` IDs (`"PHYS_objects"`, `"PHYS_metadata"`, `"LOG_…"`)
- `files.Single(f => f.LocalPath == "objects/foo")` — `LocalPath` is FLocat HREF, always raw path
- All logical structMap ID assertions (`LOG_…`)
- UI/API round-trip tests that treat div IDs as opaque strings

#### Fixtures that must be FROZEN (not regenerated)

The existing fixtures exercising spaces and special characters in paths are the only test coverage
for legacy raw-ID METS. After Step 2, regenerating them from the same code would produce encoded
IDs and erase this coverage.

Action:
- Move `Outputs/path-*.xml` fixtures to a new folder `XmlGen.Tests/LegacyFixtures/` and commit
  them as static XML.
- Add `MetsManagerLegacyFixtureTests`: load each frozen fixture, parse it, navigate to a div by
  path, mutate via `MetsManager`, write, re-parse. Assert the round-trip works.
- Add a `README.md` inside `LegacyFixtures/` and a comment in the old generator method:
  **"Regenerating these fixtures is forbidden. They are the regression corpus for legacy raw-ID
  METS files. Remove only after a confirmed bulk migration (Step 3)."**

#### New tests to add

**Schema validation merge gate** — `MetsSchemaValidationTests`:

A test class that validates produced METS XML against the METS XSD using `XmlSchemaSet`. This is
the merge gate for Step 2. Fixtures must include:
- Single file in `objects/` (baseline)
- Spaces in directory and filename
- Ampersand, Unicode, leading-digit filename (verify the encoder handles these)
- Deep nesting (3+ levels)
- Fixtures from `PhysicalStructureTests` re-validated

**Bijection test**:
```csharp
Assert.Equal(localPath, localPath.ToMetsId().DecodeMetsId());
```
for: space, slash, ampersand, `(`, `)`, leading digit, Unicode letter, `#`, `?`.

**Mixed-format integration test**: a fixture containing BOTH legacy raw-`/` IDs (the frozen ones)
AND a freshly added encoded ID. Assert both `…ByPath` and `…ByDivId` navigation work correctly on
the same METS.

### 2.5 Deployment constraint — atomic Pipeline + Preservation release

`Pipeline.API` calls into `MetadataManager.ProcessAllFileMetadata` to write characterisation
metadata back into METS. `Preservation.API` calls into `MetsManager` for structural changes. If
they deploy at different times during Step 2 rollout, one service mints raw IDs and the other
mints encoded IDs — a single deposit edited in that window gets a maximally mixed METS.

**Requirement**: Step 2 must be deployable as a single release tagging both `Pipeline.API` and
`Preservation.API` container images. Document this in the PR description as a release-engineering
precondition.

### 2.6 Other concerns — resolved

| Concern | Resolution |
|---|---|
| `MetadataManager` ADMID-join workaround (join on space for IDREFS-split legacy IDs) | **Keep**. After Step 2 it is a no-op for new METS. For legacy METS with spaces in IDs it is still required. Add comment: "Required for backwards compatibility with legacy raw-ID METS. Do not remove until bulk migration (Step 3) eliminates all such files." |
| Storage.Repository.Common diff logic | Verified: no consumer of BinariesToAdd/BinariesToPatch reads METS IDs; everything keys on LocalPath. State in PR description. |
| iiif-builder (Python) | Already opaque — reads IDs as dict keys, never parses paths from them. State in PR description. |
| MetsExtensions DivId / AdmId surfaced to clients | Document as an opaque-string contract in the type's xmldoc. Enforce going forward. |
| Mixed-format universe post-deploy | Accepted. Legacy METS are readable; new METS are schema-valid; mixed METS (edited legacy) are readable but still not fully schema-valid. This is the known cost of not doing Step 3. |

### 2.7 Step 2 acceptance criteria

- Every ID-minting call site listed in 2.2 uses `.ToMetsId()`
- No assertion computes an encoded ID by inlining a literal — all use `.ToMetsId()` so future
  encoding changes break tests explicitly
- Schema validation test (`MetsSchemaValidationTests`) passes for all current and new fixtures
- Frozen legacy fixtures in `LegacyFixtures/` pass `MetsManagerLegacyFixtureTests`
- Mixed-format integration test passes
- ModsManager DMD derivation refactored to use `localPath` directly
- `Pipeline.API` and `Preservation.API` tagged on the same release
- A grep across `Pipeline.API`, `Preservation.API`, `Storage.Repository.Common`,
  `DigitalPreservation.Workspace`, `DigitalPreservation.UI` for raw `PHYS_`, `FILE_`, `ADM_`,
  `TECH_`, `DMD_` concatenations shows no unencoded path construction (expected grep hits in
  tests are listed and justified)
- PR description documents: kept-for-legacy ADMID-join workaround, frozen-fixture rule, atomic
  deploy requirement, schema-validation merge gate

---

## Step 3 — Legacy migration decision

### 3.1 Options

**A. Defer (recommended)**. Accept that any METS file edited post-Step-2 will be in mixed-format
until every element has been re-minted by the new code. Schema validation of the historical corpus
remains impossible; legacy fixtures remain a permanent regression-test asset.

**B. One-shot bulk migration**. For each Archival Group: read the METS, rewrite all IDs through
`XmlConvert.EncodeLocalName`, write a new OCFL version. After bulk migration, the entire corpus
is schema-valid.

### 3.2 OCFL impact (applies either way, for edited AGs)

Editing any AG post-Step-2 produces a new OCFL version whose METS diff is dominated by ID renames,
not the meaningful edit. This artefact is permanent in OCFL history. Activity Stream fires;
iiif-builder rebuilds all touched IIIF manifests. Accepted as a known cost.

For option B additionally: every AG gets a new OCFL version in a short window. Activity Stream
and IIIF rebuild amplify by the total number of Archival Groups. Schedule during a quiet period;
capacity-plan iiif-builder.

### 3.3 Recommendation: defer (option A)

Reasons:
- The mixed-format universe is not a runtime correctness problem — only a schema-validation one.
  Our tooling (`MetsParser`, iiif-builder) works fine with both forms.
- Steps 1 + 2 stop NEW invalid IDs from being minted, which is what Issue #188 demands.
- A bulk migration is a high-blast-radius operation that should be motivated by a concrete need
  (audit, schema-validity policy) rather than aesthetic completeness.
- The legacy corpus is finite and shrinks naturally as deposits are edited over time.

**Plan but do not execute Step 3.** Reserved branch name: `feat/188-bulk-legacy-migration`.
Acceptance criteria for when it IS executed:
- Idempotent, dry-run mode
- Opt-in admin endpoint (not run on deploy)
- OCFL commit message tagged: `xml-id-migration:#188`
- Re-evaluate when an audit or schema-validity policy forces the issue

When/if option B executes:
- Unfreeze the legacy fixtures in `LegacyFixtures/`
- Remove the `MetadataManager` ADMID-join workaround
- Remove the legacy-tolerance read paths in `ModsManager.GetModsForDiv`

---

## Branch and PR ordering

```
main
 └── feat/188-physical-divs-cache       (Step 1)
      ├── PR #1 → merge to main
      └── feat/188-encoded-mets-ids     (Step 2, branched from Step 1)
           └── PR #2 → merge to main (after PR #1 merged)

(reserved, not opened)
main (post PR #2)
 └── feat/188-bulk-legacy-migration     (Step 3)
```

PR #2 must NOT be opened off `main` directly — it depends on `PhysicalDivsByPath` to navigate
encoded IDs, which only exists after PR #1. If PR #1 is unmerged but PR #2 needs CI, target PR #2
at the Step 1 branch and rebase after PR #1 merges.

Step 2 must NOT deploy until Step 1 has been in production long enough to be confident in the
cache invariants. Practical guideline: at least one full deposit/edit cycle in production after
Step 1 before tagging the Step 2 release.

---

## Acceptance summary

**Step 1 done when:**
- Cache populated correctly from parser load; cache maintained by all mutations
- All existing tests pass with no assertion changes
- New cache-coverage and normalisation tests pass
- VirusProvEventPrefix exact-match test passes
- METS file output byte-identical to current main for representative fixtures
- Deployed; one full deposit/edit cycle in production without regression

**Step 2 done when:**
- Every ID-minting call site uses `.ToMetsId()`
- Schema validation merge-gate test passes for all fixtures
- Legacy fixtures frozen in `LegacyFixtures/` and validated by `MetsManagerLegacyFixtureTests`
- Mixed-format integration test passes
- ModsManager DMD derivation refactored to use `localPath` directly
- Pipeline.API and Preservation.API tagged on the same release
- PR description documents all legacy-compat decisions

**Step 3 done when (if executed):**
- Idempotent, dry-run-able admin operation exists
- New OCFL version per AG with tagged commit message
- Legacy fixtures, ADMID-join workaround, and ModsManager legacy read path removed
- Full corpus passes the schema validation test

---

## Critical files for implementation

**Must change in Step 1:**
- `src/DigitalPreservation/DigitalPreservation.Mets/FullMets.cs`
- `src/DigitalPreservation/DigitalPreservation.Mets/MetsManager.cs`
- `src/DigitalPreservation/DigitalPreservation.Mets/MetsParser.cs`
- `src/DigitalPreservation/DigitalPreservation.Mets/StorageImpl/FileSystemMetsStorage.cs`
- `src/DigitalPreservation/Storage.Repository.Common/Mets/MetsFromArchivalGroup.cs`

**Must change in Step 2:**
- All Step 1 files (ID minting lines)
- `src/DigitalPreservation/DigitalPreservation.Mets/MetadataManager.cs`
- `src/DigitalPreservation/DigitalPreservation.Mets/ModsManager.cs`
- `src/DigitalPreservation/DigitalPreservation.Mets/Constants.cs`
- `src/DigitalPreservation/DigitalPreservation.Utils/MetsIdEncoding.cs` *(new)*
- All test files listed in 2.4

**Read-only (no code changes, verify in PR description):**
- `src/DigitalPreservation/DigitalPreservation.Mets/MetsParser.cs` (opaque ID lookup — confirmed)
- `src/iiif-builder/app/mets_parser/mets_parser.py` (opaque — confirmed)
- `src/DigitalPreservation/DigitalPreservation.UI/wwwroot/js/logical-structmap.js` (opaque round-trip — confirmed)
- `src/DigitalPreservation/DigitalPreservation.Workspace/` (keys on LocalPath, not METS IDs — confirm by grep)
