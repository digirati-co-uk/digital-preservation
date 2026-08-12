# Issue #188 — METS XML ID Fix: Sequenced Implementation Plan (v2)

> **Status (2026-08-11):** Step 1 is IMPLEMENTED — PR #211 (`feat/188-physical-divs-cache`),
> with two design deltas noted in commit messages: lazy cache population replaced the
> GetEmptyMets bootstrap, and navigation gained a three-tier fallback (cache → per-child
> metadata resolution → legacy ID convention). The IDREFS companion
> (`issue-188-idrefs-plan.md`) is also implemented, on `feat/188-idrefs-resolution`, stacked
> on #211. Step 2 is not started. `chore/sonar-cleanup-main` has since merged; "the mainline"
> below now simply means `main`.
>
> **Status addendum (2026-08-12, after PR #211 merge + independent whole-feature review):**
> further deltas the review found unrecorded — the "pure refactor / zero assertion changes"
> claim below no longer holds precisely: (a) `IMetsManager` Set* methods now return `Result`
> instead of `void` (failures surface to callers; `SetModsInformation` propagates); (b)
> `EditMets`/`LinkFile`/`UnLinkFile`/`SetFileLinks` normalise incoming paths (`./` strip,
> trailing-`/` trim, BagIt `data/` strip), changing produced XML for variant path inputs;
> (c) FLocat comparisons in update/delete/metadata paths are normalised the same way. All
> deliberate improvements from the review rounds.
>
> **Step 2 checklist addition (2026-08-12, from the whole-feature review):**
> `BuildFptr` and `LinkFile`/`UnLinkFile`/`SetFileLinks` must switch from MINTING
> `FILE_`+path to RESOLVING the actual FILE element's ID (via the cached div's `Fptr`, or a
> FLocat lookup) — on legacy documents the fileSec keeps raw IDs, so minting encoded IDs
> would create dangling FILEID/smLink references and make raw-ID smLinks unremovable. The
> §2.x "structLink FILE_ IDs — just re-mint" assumption (and its counterpart in
> `issue-188-analysis.md`, now corrected there) was wrong for legacy content. Add a legacy
> structLink/logical-fptr regression test on `Samples/path-fixture-spaces.xml` with it.

> v2, 2026-08-10. Revised against `chore/sonar-cleanup-main` after full re-verification of every
> code claim — see `issue-188-reassessment.md` for what changed and why the approach stands.
> v1 (Claude Opus 4.7, based on `issue-188-analysis.md` and `issue-188-opus-review.md`) is in
> git history. All file paths and line numbers below refer to `chore/sonar-cleanup-main`.

## Goal

Make every METS `xs:ID` attribute the platform mints schema-valid (NCName-conformant) without
breaking navigation, round-trip parsing, or backward compatibility with already-deposited METS
files. **Backward compatibility is a hard constraint: every METS file we have already created —
raw `/`-and-space IDs included — must remain fully readable and editable forever (or until a
deliberate Step 3 migration).**

The fix is sequenced into two mandatory steps plus one deferred step:

1. **Step 1 PR — `feat/188-physical-divs-cache`**: introduce a path→div cache on `FullMets`,
   populated at load and maintained by every mutation. Decouples navigation from ID format.
   Pure refactor: zero change in produced XML, zero test assertion changes.
2. **Step 2 PR — `feat/188-encoded-mets-ids`** (branched off Step 1, NOT off the mainline):
   switch all ID minting to `XmlConvert.EncodeLocalName`. Test assertions for path-containing
   IDs change. All deployables embedding the METS code ship as one release.
3. **Step 3 (deferred, decision documented)**: bulk legacy migration.

Step 2 cannot ship before Step 1 is in production: without the cache, `LocateMetsDivByLocalPath`
would silently fail to find encoded-ID divs.

Side changes carried in Step 1 (latent bugs that become real under encoded IDs):
- ClamAV digiprov substring lookup → exact match (`MetsParser.cs:499–512`).

Side change carried in Step 2 (new surface found in re-verification):
- NCName validation of client-supplied logical structMap div IDs (`MetsManager.SetStructMap`).

**Base branch**: code changes start from `chore/sonar-cleanup-main` (expected to merge to `main`
shortly; it contains the Mets project extraction, ad-hoc metadata, logical structMap editing and
PR #209). Rebase onto `main` once that merge happens.

**Strategic context** (see reassessment doc): Leeds wants third-party METS to become editable,
with editability decided by *conformance to our profile* rather than only the `mets:agent` name.
The Step 1 cache builder — navigation by `premis:originalName` (directories) and
`FLocat/@xlink:href` (files) — is the core of that future conformance check. Build it as a
diagnosable pass (report *why* population failed), not a silent one.

---

## Step 1 — Decouple navigation from ID format

### 1.1 Approach

Introduce a per-`FullMets` path cache:

```csharp
// FullMets.cs — currently has only Mets, Uri, ETag
public Dictionary<string, DivType> PhysicalDivsByPath { get; } = new();
```

Keys are the same `localPath` strings the rest of the system already uses (deposit-relative,
BagIt `data/` already stripped — see 1.6). Values are the typed XmlGen `DivType` instances in
the physical structMap. Lookups are O(1); partial-depth resolution is preserved (missing key →
break, same shape as today). The PHYS_ROOT div is NOT in the cache — it is always the starting
point of navigation, never a target.

The in-code comment at `MetsManager.cs:379–380` ("we can use the premis:originalName for the
directory") already points at exactly this design.

### 1.2 Files to change

| File | Change |
|---|---|
| `DigitalPreservation.Mets/FullMets.cs` | Add `PhysicalDivsByPath` |
| `DigitalPreservation.Mets/MetsCache.cs` *(new)* | `PopulateFrom(Mets mets, Dictionary<string, DivType> cache)` — single shared populator, plus a diagnosable variant that reports unresolvable/duplicate paths (seed of the future conformance checker) |
| `DigitalPreservation.Mets/StorageImpl/FileSystemMetsStorage.cs` | Populate after deserialization (`FullMets` construction at lines 70–75) |
| `Storage.Repository.Common/Mets/StorageImpl/S3MetsStorage.cs` | Same, at lines 117–122 — **the production read path; omitted from plan v1** |
| `DigitalPreservation.Mets/MetsManager.cs` | (1) Rewrite `LocateMetsDivByLocalPath` (363–393). (2) `AddNewFile`/`AddNewDirectory` add to cache. (3) `DeleteDiv` removes from cache. (4) `GetEmptyMets` bootstraps **three** well-known entries: `objects`, `metadata`, `metadata/ad-hoc` (the ad-hoc div is new since plan v1). |
| `Storage.Repository.Common/Mets/MetsFromArchivalGroup.cs` | Populate/maintain the cache when building METS from an Archival Group (`AddResourceToMets`, `AddBinariesToMets`) |
| `DigitalPreservation.Mets/MetsParser.cs` | ClamAV digiprov lookup fix only (1.8) — the parser's XDocument path never produces a `FullMets` and does NOT populate the cache (correction to plan v1 §1.4) |

### 1.3 Population (typed model, not XDocument)

Single recursive descent over the in-memory typed `Mets` object. The XmlGen model exposes the
PREMIS payload only as `XmlElement[]` inside `MdSecTypeMdWrapXmlData.Any` (there is no fully
typed PREMIS read path on the METS graph — `PremisManager` serialises typed PREMIS *to*
`XmlElement` on write), so `premis:originalName` extraction is a small descendant query on that
`XmlElement`:

```csharp
void Walk(DivType div, Mets mets, Dictionary<string, DivType> cache)
{
    foreach (var child in div.Div)
    {
        string? key = null;

        if (child.Type == Constants.DirectoryType && child.Admid.Count > 0)
        {
            // directory: Admid → AmdSec → techMD → mdWrap → xmlData/Any → premis:originalName
            var amdSec = mets.AmdSec.FirstOrDefault(a => a.Id == child.Admid[0]);
            key = ExtractPremisOriginalName(amdSec);   // XmlElement descendant query
        }
        else if (child.Type == Constants.ItemType && child.Fptr.Count > 0)
        {
            // file: fptr → FILE in the OBJECTS fileGrp → FLocat href
            var fileId = child.Fptr[0].Fileid;
            var grp = mets.FileSec?.FileGrp.FirstOrDefault(g => g.Use == "OBJECTS");
            var file = grp?.File.FirstOrDefault(f => f.Id == fileId);
            key = file?.FLocat.FirstOrDefault()?.Href;
        }

        if (key != null)
        {
            key = NormalisePathKey(key);   // see 1.6
            cache[key] = child;            // duplicate key → diagnosable failure, see below
        }

        Walk(child, mets, cache);
    }
}
```

Duplicate keys (two divs resolving to the same path) and Directory divs with unresolvable
originalName are conformance failures — collect them rather than throwing blindly, so the same
pass can later answer "is this METS editable?".

### 1.4 Where the cache is built / refreshed

| Entry point | Action |
|---|---|
| `S3MetsStorage.GetFullMets` / `FileSystemMetsStorage.GetFullMets` | Build after XmlSerializer deserialization — the only two read paths producing a `FullMets` |
| `MetsManager.GetEmptyMets` | Bootstrap three entries: `objects`, `metadata`, `metadata/ad-hoc` |
| `MetsFromArchivalGroup.CreateStandardMets` | Bootstrap then inline maintenance in `AddResourceToMets`/`AddBinariesToMets`; debug-build assertion that a final `PopulateFrom` matches |

### 1.5 `LocateMetsDivByLocalPath` rewrite

Replace the body of `MetsManager.cs:363–393`, preserving the
`(contextDiv, parent, foundDepth, totalDepth)` contract that `EditMets` (caller at line 103)
branches on:

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

        // Guard against a malformed source that re-uses the same premis:originalName
        // in two unrelated subtrees: only accept a direct child of the current div.
        if (!div.Div.Contains(childDiv))
            break;

        counter++;
        parent = div;
        div = childDiv;
    }

    return (div, parent, counter, elements.Length);
}
```

Other callers: `SetRecordInfoByPath:469`, `SetRightsStatementByPath:490`,
`SuppressRightsInheritanceByPath:506`, `SetAccessRestrictionsByPath:541`.

Error-semantics note (from the Opus review, still valid): the old `SingleOrDefault` threw on
duplicate IDs; the cache returns the recorded div or nothing. The direct-child guard restores
safe behaviour ("not all parts of the path have been added" error, same as today).

### 1.6 Path normalisation before comparison

`premis:originalName` may come from legacy or third-party sources. Before inserting into or
looking up from the cache:

- Strip a leading `data/` prefix (`FolderNames.RemovePathPrefix` already exists)
- Strip a leading `./`; trim a trailing `/`
- Reject empty strings (no-op)

`MetsManager` already normalises the incoming `localPath` the same way, so writes and lookups
converge. The populator applies identical normalisation to extracted `originalName` values.
(Archivematica-style `%transferDirectory%objects/…` values do NOT need handling here — those
files are not editable and never reach `MetsManager`; the conformance checker will simply report
them as non-conforming.)

### 1.7 Cache maintenance on mutations

| Mutation | Cache update |
|---|---|
| `AddNewFile` (~line 157) | After adding the item div: `cache[localPath] = childItemDiv` |
| `AddNewDirectory` (~line 180) | After adding the directory div: `cache[localPath] = childDirectoryDiv` |
| `DeleteDiv` (313–353) | Before removing: `cache.Remove(operationPath)` |
| `UpdateExistingFile` / `UpdateExistingDirectory` | No-op — div identity unchanged |
| Logical structMap operations (`SetStructMap`, `RemoveStructMap`, `SetStructMapOrder`, `LinkFile`/`UnLinkFile`/`SetFileLinks`) | No-op — never touch the physical structMap (verified) |
| `MetsFromArchivalGroup.AddResourceToMets` / `AddBinariesToMets` | Inline maintenance + debug assertion |

Audit note: there is no rename mutation in `MetsManager` (renames surface as label edits or
delete+add; ImportJob renames are a Storage-side concept) — re-verify this during
implementation; any future rename operation must update the cache.

Add a `[Conditional("DEBUG")]` assertion at entry to `LocateMetsDivByLocalPath` that the cache
equals a fresh rebuild — catches any mutation path that forgets maintenance.

### 1.8 ClamAV digiprov lookup: substring → exact match

`MetsParser.cs:499–512` currently falls back to a case-insensitive **containment** search:

```csharp
var matchingKey = lookupMaps.DigiprovMdMap.Keys
    .FirstOrDefault(k => k.ToLower().Contains(lowerKey));
```

After Step 2, one encoded admId can be a prefix of another (`ADM_a` ⊂ `ADM_a_x002F_b`), making
cross-talk real. Fix now: exact case-insensitive equality
(`string.Equals(k, clamavKey, StringComparison.OrdinalIgnoreCase)`), plus a regression test with
one digiprov key a substring of another. The related containment check at
`MetadataManager.cs:201` (`x.Id.Contains(Constants.VirusProvEventPrefix)`) matches on the
*prefix*, not the admId, and is safe — but verify while in there.

Docs impact: `02c-METS-Parsing.md` says digiprov IDs are "matched case-insensitively, by
containment" — update to exact match when this ships.

### 1.9 Step 1 acceptance criteria

- All existing tests pass with zero assertion changes; METS output byte-identical for all
  fixtures (`XmlGen.Tests/Samples/*.xml` round-trips included)
- New tests: cache population from typed load (file, directory, nested, ad-hoc div); cache
  maintained through `AddNewFile`/`AddNewDirectory`/`DeleteDiv` (mutate → cache equals fresh
  rebuild); path normalisation (`data/` prefix fixture); duplicate-originalName fixture resolves
  via the direct-child guard; population diagnostics reported for unresolvable directories
- ClamAV exact-match regression test
- Code-review checklist: every mutation audited for cache maintenance

### 1.10 Step 1 risks (unchanged from v1)

- **Dead-code window**: until Step 2 ships, cache navigation produces results identical to the
  old ID walk — no production traffic exercises the encoded path. Exhaustive unit tests are the
  only guard.
- **Cache drift**: any future code mutating `StructMap`/`FileSec`/`AmdSec` outside
  `MetsManager`/`MetsFromArchivalGroup` goes stale. Mitigations: keep `MetsCache.PopulateFrom`
  public and documented; debug assertion.

---

## Step 2 — Mint safe IDs

### 2.1 Approach

A single extension method wraps `XmlConvert.EncodeLocalName`; every minting site uses it.
Handles the full NCName-invalid set (space, `/`, `&`, `<`, `>`, quotes, `,`, `;`, brackets,
`#?*!@$%^+=~`, leading digits); Unicode letters pass through; bijective
(`XmlConvert.DecodeName` inverse; literal `_x` self-escaped as `_x005F_x`). No current consumer
decodes IDs — the inverse exists for completeness and is documented.

```csharp
// new: src/DigitalPreservation/DigitalPreservation.Utils/MetsIdEncoding.cs
public static class MetsIdEncoding
{
    /// Encodes a local path for use inside an xs:ID / NCName attribute.
    /// Does NOT add a prefix — callers concatenate Constants.PhysIdPrefix etc.
    public static string ToMetsId(this string localPath)
        => System.Xml.XmlConvert.EncodeLocalName(localPath);

    /// Round-trip inverse of ToMetsId.
    public static string DecodeMetsId(this string id)
        => System.Xml.XmlConvert.DecodeName(id);
}
```

`PHYS_objects/my file.pdf` becomes `PHYS_objects_x002F_my_x0020_file.pdf`.

When minting into a document that may contain foreign IDs (future third-party editing), add an
existence check against the ID maps before insertion — deterministic path encoding makes
collisions with third-party schemes (Goobi `PHYS_0001`, Archivematica `file-<uuid>`) practically
impossible, but the check is cheap.

### 2.2 Every ID-minting site (re-verified inventory)

| File | Lines | Site |
|---|---|---|
| `MetsManager.cs` | 160–161 | `AddNewFile`: `PhysIdPrefix`/`FileIdPrefix` + localPath |
| `MetsManager.cs` | 183–185 | `AddNewDirectory`: `PhysIdPrefix`/`AdmIdPrefix`/`TechIdPrefix` + localPath |
| `MetsManager.cs` | 252–304 | `GetEmptyMets`: `PHYS_ROOT` literal (252, already valid); `MetadataDivId` (259); `DMD_`/`ADM_` + `FolderNames.Metadata` (262–263); **`MetadataAdHocDivId` (268) and `ADM_`/`DMD_` + `FolderNames.MetadataAdHoc` (271–272) — contain `/` today**; objects ids (278–282); three AmdSec/TechMd id pairs (294, 299, 304) incl. `ADM_metadata/ad-hoc` |
| `MetsManager.cs` | 618 | `BuildFptr`: `FileIdPrefix + fp.LocalPath` (logical structMap fptrs reference physical FILE_ ids) |
| `MetsManager.cs` | 709–710, 719–720, 734 | `LinkFile`/`UnLinkFile`/`SetFileLinks`: `FileIdPrefix + path` into `smLink` from/to |
| `MetadataManager.cs` | 28–30 | `ProcessAllFileMetadata`: `FILE_`/`ADM_`/`TECH_` + operationPath |
| `MetadataManager.cs` | 267 | `AddVirusXml`: `VirusProvEventPrefix + ctx.FileAdmId` — **derived ID, new in v2**; valid automatically once admId is encoded, but include in audit with its readers (MetsParser 499–512, MetadataManager 201) |
| `ModsManager.cs` | 156–173 | `GetModsForDiv` DMD derivation — refactor, see 2.3 |
| `Storage.Repository.Common/Mets/MetsFromArchivalGroup.cs` | 62–63, 68; 97–99, 104 | `AddResourceToMets` / `AddBinariesToMets`: all four prefixes + localPath — different project, ships in the same PR |
| `DigitalPreservation.Mets/Constants.cs` | 17–19 | `ObjectsDivId`, `MetadataDivId` already valid. **`MetadataAdHocDivId` (19) = `"PHYS_metadata/ad-hoc"` — invalid NCName constant, becomes `PhysIdPrefix + "metadata/ad-hoc".ToMetsId()`** (as a static readonly or the literal encoded form with a comment). `FolderNames.MetadataAdHoc` stays a raw path — honour its `// TODO: #188 do not use this as an ID component` |

Confirmed non-sites (state in PR description): no ID construction in Pipeline.API,
Preservation.API, Storage.API/Importer, Workspace, Deposit.Archiver, Registrant, Builder, or
the UI other than `LOG_<epoch-ms>` generation (`Deposit.cshtml.cs:764`,
`logical-structmap.js:369`) which is already NCName-safe. `PremisManager` mints no METS IDs.
The `startsWith('LOG_')` discriminator at `logical-structmap.js:556` is unaffected (logical IDs
keep their form).

### 2.3 ModsManager refactor — eliminate the PHYS→DMD string coupling

Current (`ModsManager.cs:156–173`): derives `DMD_` by `div.Id.RemoveStart("PHYS_")` for
physical divs, `DmdIdPrefix + div.Id` for others (line 168 — logical divs). Replace with
explicit derivation from `localPath`:

```csharp
public static ModsDefinition? GetModsForDiv(
    Mets mets, DivType div, bool createDmd = false, string? localPath = null)
{
    if (div.Dmdid.Count == 0 && createDmd)
    {
        // physical divs: mint from the same encoded localPath used for PHYS_;
        // logical divs (localPath == null): div.Id is a client-supplied NCName (validated
        // at SetStructMap boundary) — DMD_ + div.Id as today
        var idPart = localPath != null ? localPath.ToMetsId() : div.Id;
        div.Dmdid.Add(Constants.DmdIdPrefix + idPart);
    }
    var normalised = string.Join(' ', div.Dmdid);   // keep: legacy IDREFS workaround, see 2.6
    return GetModsForDmdId(mets, normalised, createDmd);
}
```

Thread `localPath` through callers (all MetsManager.cs): `SetRecordInfoForDiv:481`,
`SuppressRightsInheritanceForDiv:518`, `SetRightsStatementForDiv:527`,
`SetAccessRestrictionsForDiv:553` (each reachable from `…ByPath` — has path — and `…ByDivId` —
localPath null, div.Id already valid), and `BuildLogicalDiv:594` (localPath null).
Read path (`createDmd:false`) unchanged: resolves via existing `div.Dmdid` values, so legacy
raw-`/` DMD ids keep working.

### 2.4 NCName validation for client-supplied logical structMap IDs *(new in v2)*

`SetStructMap` → `BuildLogicalDiv` (`MetsManager.cs:585`) uses `range.Id` verbatim as the div
ID, and `ModsManager:168` mints `DMD_ + range.Id`. Nothing validates these — a client can inject
schema-invalid IDs today. Add at the `SetStructMap` boundary (and any other entry accepting
caller-supplied div IDs): `XmlConvert.VerifyNCName(range.Id)` → `BadRequest` on failure.
**Reject, don't encode** — the client owns these IDs and round-trips them; silently changing
them would break the client's references. UI-generated `LOG_<epoch-ms>` IDs already pass.
Legacy tolerance: validation applies to *setting* structMaps, not to reading existing files.

### 2.5 Tests — what changes, what is frozen, what is added

Do NOT inline literal encoded strings in assertions — compute via `.ToMetsId()` (or a
`MetsId(prefix, path)` helper in Test.Helpers) so an encoding change breaks tests explicitly.

#### Assertion sites that change (re-verified, all in `XmlGen.Tests` unless noted)

| File | Lines (current) |
|---|---|
| `MetsManagerPathTests.cs` | 158–177, 257–258, 290–291, 315–324, 380–382 (+ stale doc comments 22, 147, 183 referencing "MetadataManager line 195" — now 198) |
| `MetsManagerSyncTests.cs` | 142–149, 220–240, 287–294, 366–371, 408–410 |
| `MetsManagerLogicalStructTests.cs` | 148, 186–198, 248, 299, 398–404, 462, 688–690, 775–776, 801–802, 834 |
| `MetsManagerDeepStructureTests.cs` | 551–552, 570–572 (helpers build `$"PHYS_{localPath}"` etc. → add `.ToMetsId()`) |
| `MetsManagerPathFixtureTests.cs` | **new file since v1, 661 lines** — hard-coded raw-ID assertions at 470–490, 656–658; explicitly documents current invalid-NCName behaviour (lines 224, 312); contains the `Manual` fixture generator (79–169). Splits into: legacy-fixture tests (frozen inputs, unchanged assertions) + new-format tests (encoded assertions) |
| `MetsManagerTests.cs` | 601, 613–614, 627, 653, 684, 694–696, 702, 707, 715, 728–729 (ad-hoc div IDs) |
| `MetsManagerMetadataTests.cs` | 148, 185–189, 230–235, 268–269, 285–286, 304–310, 344–345, 386–391 |
| `MetsManagerWithPremis.cs` | 133, 138, 198, 267, 322, 372, 442, 492, 572, 702 |
| `Parsing/PhysicalStructureTests.cs` | inline XML fixtures + assertions at 120–125 (`MetsExtensions.DivId`/`.AdmId`) |
| `Parsing/FileMetadataTests.cs` | 26–27, 46, 55, 94, 110, 125 |
| `Test.Helpers/TestData/TestStructure.cs` | 190–191, 204–205 (JSON `divId`/`admId` expectations) |

#### Assertions that do NOT change

- `…ByDivId` calls with slash-free IDs (`PHYS_objects`, `PHYS_metadata`, `LOG_…`)
- `LocalPath` assertions (FLocat href stays a raw path — paths are NOT encoded, only IDs)
- Logical structMap ID assertions (`LOG_…`)
- UI/API round-trip tests treating div IDs as opaque

#### Fixtures FROZEN (correction: they live in `Samples/`, not `Outputs/`)

`XmlGen.Tests/Samples/path-fixture-spaces.xml` and `path-fixture-special.xml` are the
legacy-raw-ID regression corpus; `liddle.mets.xml`, `wow.mets.xml`, `mets-sample-001.xml`,
`response-book.mets.xml`, `simple-image.mets.xml` also contain raw path IDs and stay as-is
(`Outputs/` is generated and git-ignored — plan v1 was wrong about this).

Actions:
- Mark the path fixtures as frozen: README note in `Samples/` + comment on the `Manual`
  generator in `MetsManagerPathFixtureTests.cs:79` — **"regeneration forbidden; legacy
  regression corpus; remove only after a Step 3 migration"**. Repoint or duplicate the generator
  so new-format fixtures are additional files, never overwrites.
- `MetsManagerLegacyFixtureTests`: load each frozen fixture → parse → navigate by path (cache!)
  → mutate via `MetsManager` → write → re-parse. Assert round-trip and that pre-existing raw IDs
  are untouched by the edit.

#### New tests

- **Schema-validation merge gate** — `MetsSchemaValidationTests`: validate produced METS against
  the METS XSD (`XmlSchemaSet`). Nothing in the suite catches invalid IDs today (XmlSerializer
  doesn't enforce `xs:ID`); this test would have caught #188 at birth. Fixtures: baseline,
  spaces, `&`/unicode/leading-digit names, 3+-deep nesting, ad-hoc div.
- **Bijection test**: `path == path.ToMetsId().DecodeMetsId()` for the representative
  character set.
- **Mixed-format integration test**: legacy raw-ID fixture + fresh encoded additions in one
  METS; `…ByPath` and `…ByDivId` navigation and ClamAV digiprov resolution all work.
- **NCName rejection test**: `SetStructMap` with an invalid range ID → BadRequest.

### 2.6 Legacy-compatibility artefacts — keep, and comment as kept

The `string.Join(' ', …)` IDREFS workaround (spaces in legacy IDs make the XML processor split
one ID into several tokens) exists in **five places** — all stay, each gaining a comment
"required for legacy raw-ID METS; remove only after Step 3 migration":

- `MetadataManager.cs:198` (the TODO comment at 195–197 is this issue — update it to reference
  the fix)
- `ModsManager.cs:171` and `:178`
- `MetsManager.cs:330, 336, 345` (DeleteDiv's conditional `Count > 1 ? Join : [0]` forms)

`MetsExtensions.DivId`/`.AdmId` (populated `MetsParser.cs:350–354`, `599–603`) surface raw METS
IDs into the transit model, API responses and UI. Verified opaque today; document the
opaque-string contract in the type's xmldoc so no consumer ever substring-matches them.

### 2.7 Deployment constraint — one release for every embedder *(expanded in v2)*

`DigitalPreservation.Mets` and `Storage.Repository.Common` are compiled into
**Preservation.API, Pipeline.API, DigitalPreservation.UI, Storage.API, Storage.API.Importer and
DigitalPreservation.Deposit.Archiver**. A mixed-version window means one service minting raw IDs
while another mints encoded IDs into the same METS. Step 2 ships as a single release tagging all
of them; document as a release-engineering precondition in the PR.

### 2.8 Step 2 acceptance criteria

- Every site in 2.2 uses `.ToMetsId()`; grep for prefix + raw-path concatenation across all
  projects comes back clean (justified test hits listed)
- Schema-validation merge gate green for all current and new fixtures
- Frozen `Samples/` fixtures untouched; `MetsManagerLegacyFixtureTests` green
- Mixed-format integration test green
- ModsManager derives DMD from `localPath`; NCName validation live on `SetStructMap`
- All embedders tagged on one release
- PR documents: five kept IDREFS workarounds, frozen-fixture rule, atomic deploy, merge gate,
  opaque-ID contract on `MetsExtensions`

---

## Step 3 — Legacy migration: defer (decision unchanged)

Options and reasoning unchanged from v1 (see git history for the long form):

- **Defer (recommended)**: mixed-format universe is a schema-validation liability, not a runtime
  one; Steps 1+2 stop new invalid IDs, which is what #188 demands; bulk migration is
  high-blast-radius (new OCFL version per AG, Activity Stream storm, full IIIF rebuild) and
  should be motivated by a concrete audit/policy need.
- Reserved branch: `feat/188-bulk-legacy-migration`. Requirements if executed: idempotent,
  dry-run mode, opt-in admin endpoint, OCFL commit message tagged `xml-id-migration:#188`,
  scheduled in a quiet period with iiif-builder capacity planned.
- On execution: unfreeze fixtures, remove the five IDREFS workarounds and ModsManager legacy
  read tolerance.

One addition in v2: a future **conformance checker** (see reassessment doc) would make Step 3
measurable — "N of M Archival Groups conform" is a better trigger for the migration decision
than aesthetics.

---

## Documentation deliverables (docs repo, `mets-profiles` branch — living documents)

Ship with Step 2 (drafted during Step 1):

- **`02b-METS-Written-by-the-Platform.md`**: rewrite the "ID conventions" section — prefix +
  `EncodeLocalName(path)` normative; legacy raw form documented as read/edit-compatible
  historical form with the release boundary stated; replace the current #188 NOTE; re-render
  example IDs (ad-hoc div, any path with `/` or space); revise "navigable by path" — navigation
  is by `premis:originalName`/`FLocat` (the cache), path-derived IDs remain a deterministic
  convenience.
- **`02c-METS-Parsing.md`**: digiprov match wording (containment → exact, ships with Step 1);
  add the normative parser principle "IDs are opaque; paths come from originalName/FLocat".
- These two documents are the seed of the **editable-METS conformance spec** (third-party
  editing, Leeds requirement). Out of scope for Steps 1–2, but Step 1's diagnosable cache
  builder is designed to grow into its .NET implementation; a Schematron rule file (usable from
  both .NET and Python) is the recommended vehicle for the XML-level rules when that work
  starts.

---

## Branch and PR ordering

```
chore/sonar-cleanup-main            (→ main, expected soon)
 └── feat/188-physical-divs-cache       (Step 1)
      ├── PR #1 → mainline
      └── feat/188-encoded-mets-ids     (Step 2, branched from Step 1)
           └── PR #2 → mainline (after PR #1)

(reserved, not opened)
 └── feat/188-bulk-legacy-migration     (Step 3)
```

Plan documents live on `issue/118-invalid-xml-ids`. PR #2 is never opened against the mainline
until PR #1 has merged; Step 2 does not *deploy* until Step 1 has survived at least one full
deposit/edit cycle in production.
