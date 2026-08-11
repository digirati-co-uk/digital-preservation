# Issue #188 — Reassessment against `chore/sonar-cleanup-main` (2026-08-10)

The three earlier documents in this folder (`issue-188-analysis.md`, `issue-188-opus-review.md`,
`issue-188-plan.md`) were written against a base that is now ~280 commits behind
`chore/sonar-cleanup-main`. Since then: the METS code moved into the `DigitalPreservation.Mets`
project, the ad-hoc metadata folder (PR #182) and logical structMap editing landed, PR #209 and
several rounds of Sonar cleanup merged. Every code claim in the plan has been re-verified against
the current branch. This document records the verdict, the deltas, and two new strategic
considerations; `issue-188-plan.md` has been revised in place (v2) to match.

## Verdict: the recommended approach still stands — and is now stronger

**Step 1 (path→div cache on `FullMets`, navigation decoupled from ID format) followed by
Step 2 (mint NCName-safe IDs via `XmlConvert.EncodeLocalName`) remains the right approach.**
Nothing found in re-verification undermines it, and three things now reinforce it:

1. `MetsManager.LocateMetsDivByLocalPath` (MetsManager.cs:363–393) still navigates by
   `PHYS_ + path` ID convention — and now carries an in-code comment (lines 379–380) explicitly
   suggesting the `premis:originalName` alternative. The codebase has independently arrived at
   the same conclusion.
2. `FolderNames.MetadataAdHoc` carries a `// TODO: #188 do not use this as an ID component`
   marker (FolderNames.cs:11) — the ad-hoc work has *widened* the problem (a compile-time
   constant div ID containing `/`, see below) and left a signpost for this fix.
3. **Leeds now wants third-party METS (not created by our platform) to become editable.**
   Third-party METS has arbitrary ID schemes (Goobi `PHYS_0001`, Archivematica `file-<uuid>`,
   EPrints `eprint_10315_370441`). Any navigation that assumes our `PHYS_ + path` convention can
   never work on those files. The Step 1 cache — keyed on real paths, populated from
   `premis:originalName` (directories) and `FLocat/@xlink:href` (files) — is exactly the
   navigation mechanism that generalises to third-party METS. Step 1 is no longer just a
   refactoring enabler for Step 2; it is the foundation of the editable-third-party-METS
   roadmap.

### Alternatives re-examined

The GitHub issue offers two solutions. Re-weighed against the current situation:

- **Simple `/` → `_` replacement** — still rejected: collisions (`objects/my_folder` vs
  `objects_my/folder`), and doesn't handle spaces or the rest of the NCName-invalid set.
- **Fully opaque sequential IDs (`PHYS_1`, `PHYS_2`…)** — the "METS-idiomatic" option. Still
  rejected, and the third-party-editing requirement adds a new argument: when we one day edit a
  Goobi METS *in place*, minting `PHYS_0001`-shaped sequential IDs is exactly the scheme most
  likely to collide with the document's existing IDs. `EncodeLocalName(path)` IDs are
  deterministic (stable across writes, test- and diff-friendly), self-describing, and
  practically collision-proof against every third-party scheme we ingest. When minting into a
  foreign document we should nevertheless add a cheap existence check against the ID maps.
- **`XmlConvert.EncodeLocalName`** — confirmed. Handles the full NCName-invalid set (space,
  `/`, `&`, brackets, leading digits…), preserves Unicode letters, is bijective
  (`XmlConvert.DecodeName` is the inverse; `_x` sequences are self-escaped as `_x005F_x`).

## What re-verification confirmed unchanged

- `LocateMetsDivByLocalPath` walk shape and its `foundDepth`/`totalDepth` contract (five
  callers: EditMets:103, SetRecordInfoByPath:469, SetRightsStatementByPath:490,
  SuppressRightsInheritanceByPath:506, SetAccessRestrictionsByPath:541).
- `LocateMetsDivByDivId` (395–415) / `FindDiv` (417–434) — pure ID-equality, format-agnostic.
- `MetsParser` treats IDs as opaque throughout (`MetsLookupMaps`, lines 20–75), and already
  derives directory paths from `premis:originalName` (332, 340–343) — the read path never
  needed IDs to be paths.
- `ModsManager.GetModsForDiv` still derives `DMD_` from `div.Id.RemoveStart("PHYS_")`
  (now lines 156–173) — the refactor to derive from `localPath` remains necessary.
- The ClamAV digiprov *substring* lookup still exists (MetsParser.cs:499–512) — the exact-match
  fix is still needed and still belongs in Step 1.
- `AddNewDirectory` always writes `premis:originalName = localPath` (196–202); files get it via
  `MetadataManager.GetFileFormatMetadata` (83–109).
- `DeleteDiv` still validates `FLocat[0].Href == operationPath` (325–328).
- iiif-builder (Python) confirmed fully opaque — raw-ID dict keys only, no prefix logic at all.
- `PremisManager.Read` (post-NRE-fix) has no ID-format assumptions.
- No ID construction anywhere in Pipeline.API, Preservation.API, Storage.API, Workspace,
  Deposit.Archiver, Registrant or Builder — all minting is inside `DigitalPreservation.Mets`
  plus `Storage.Repository.Common/Mets/MetsFromArchivalGroup.cs`.

## Deltas — where the old plan was wrong or incomplete

| # | Delta | Plan impact |
|---|---|---|
| 1 | `FullMets` (FullMets.cs) has only `Mets`, `Uri`, `ETag` — no cache yet, as planned. But there are **two** `IMetsStorage` implementations that must populate it: `FileSystemMetsStorage.cs:70` **and `Storage.Repository.Common/Mets/StorageImpl/S3MetsStorage.cs:117`** — the production one, which the old plan's file list omitted. | Cache population goes in one shared helper (`MetsCache.PopulateFrom`) called from both, not in the parser (the `MetsParser` XDocument path never produces a `FullMets`). |
| 2 | `MetadataManager.cs:267` mints `digiprovMD_ClamAV_{admId}` — a **derived** ID the old plan missed. Encoded admIds make it NCName-valid automatically, but it must be in the Step 2 audit, together with its two containment-based readers (MetsParser.cs:499–512, MetadataManager.cs:201). | New row in the Step 2 minting table. |
| 3 | `Constants.MetadataAdHocDivId` = `"PHYS_metadata/ad-hoc"` — a **compile-time constant that is already an invalid NCName** (new since the plan). `GetEmptyMets` now bootstraps **three** originalName AmdSecs (`objects`, `metadata`, `metadata/ad-hoc`), not two. | Step 1 bootstraps three cache entries; Step 2 re-derives the constant as `PhysIdPrefix + Encode("metadata/ad-hoc")`. `FolderNames.MetadataAdHoc` itself stays a raw path — it is a path constant, correctly slash-y; per its TODO it must simply never be used as an ID component. |
| 4 | Logical structMap editing (MetsManager.cs:564–702) accepts **client-supplied range IDs verbatim** (`BuildLogicalDiv:585`), and `ModsManager:168` mints `DMD_ + <that raw ID>`. No NCName validation anywhere — an open injection surface for schema-invalid IDs that didn't exist when the plan was written. | New Step 2 item: `XmlConvert.VerifyNCName` validation at the `SetStructMap` boundary; reject rather than encode (the client owns these IDs; UI-generated `LOG_<epoch-ms>` values already pass). |
| 5 | The `string.Join(' ', …)` IDREFS workaround exists in **five** places, not one: MetadataManager.cs:198; ModsManager.cs:171, 178; MetsManager.cs:330, 336, 345. | All five kept for legacy compatibility, all five commented as such. |
| 6 | Test churn is larger than planned. **`MetsManagerPathFixtureTests.cs` is new** (661 lines, dedicated to special-character paths, hard-coded raw-ID assertions, and a `Manual`-category fixture *generator*). Additional assertion sites in `MetsManagerTests.cs` (ad-hoc divs), `MetsManagerMetadataTests.cs`, `MetsManagerWithPremis.cs`, `Parsing/FileMetadataTests.cs`, and `Test.Helpers/TestData/TestStructure.cs` (JSON expectations). | Step 2 test inventory updated. |
| 7 | Committed fixtures live at **`XmlGen.Tests/Samples/path-fixture-*.xml`**, not `Outputs/path-*.xml` (`Outputs/` is generated, git-ignored bar a marker). The freeze rule applies to `Samples/`, and the Manual generator in `MetsManagerPathFixtureTests.cs:79` must be neutered or repointed so it can never overwrite the frozen legacy fixtures. | Step 2 fixture plan corrected. |
| 8 | The atomic-deploy set is bigger than "Pipeline + Preservation". `DigitalPreservation.Mets` / `Storage.Repository.Common` are embedded in **Preservation.API, Pipeline.API, DigitalPreservation.UI, Storage.API, Storage.API.Importer and Deposit.Archiver**. | Step 2 ships as one release tagging every deployable that embeds the library. |
| 9 | `MetsExtensions.DivId` / `.AdmId` (populated at MetsParser.cs:350–354, 599–603) surface raw METS IDs into the transit model, API responses and UI. Currently treated as opaque everywhere (verified). | Documented as an explicit opaque-string contract in Step 2. |
| 10 | Tests remain in `XmlGen.Tests` (which now references `DigitalPreservation.Mets`) — no `Mets.Tests` project materialised. | File paths in the plan corrected; no action. |

## New consideration: conformance-based editability (and where formal METS Profiles fit)

Today a METS file is editable iff its `mets:agent` name is exactly ours. Two futures change that:

1. Leeds wants to edit deposits whose METS we did not create.
2. Even for our own files, "the agent string matches" is a weaker guarantee than "the document
   actually has the structure our editing code relies on".

The better long-term definition: **a METS file is editable if it conforms to a documented
profile** — the one specified in `02b-METS-Written-by-the-Platform.md` in the docs repo
(mets-profiles branch), generalised to admit files we didn't write. Concretely, editability ≈
"the Step 1 path cache can be built completely and unambiguously, plus the structural invariants
`MetsManager` assumes hold":

- a physical structMap whose Directory divs resolve (via ADMID → `premis:originalName`) to
  unique, normalisable paths, and whose Item divs resolve (via fptr → fileSec →
  `FLocat/@xlink:href`) to unique file paths;
- one OBJECTS file group (or an unambiguous equivalent); SHA256 fixity per file;
- referential integrity of ADMID/DMDID/FILEID;
- no two divs claiming the same path (the cache-build detects this for free).

This reframes issue #188's fix as the first instalment of that conformance story: **the Step 1
cache builder is the core of a future conformance checker** — a conforming file is (to first
order) one whose cache builds cleanly. The plan does not add a conformance checker to Steps 1–2,
but Step 1's cache-population code should be written as a well-factored, diagnosable pass
(collect *why* population failed, don't just return null) so a `.NET` checker can grow out of
it, and the same rules can be ported to Python for iiif-builder / ingest-side validation.

On **formal METS Profiles** (the Library of Congress registered-profile mechanism): a formal
profile is a prose-plus-XML *description* document — it is not machine-enforceable on its own,
and the LoC profile schema brings interop/credibility value rather than tooling value. The
pragmatic route to "both .NET and Python versions of the conformance logic" is:

1. keep `02b` (and its planned "editable METS" generalisation) as the human-readable normative
   spec;
2. express the XML-level structural rules once as **Schematron** (XPath-based rules, executable
   from both .NET and Python via XSLT), giving a single shared rule source;
3. keep the checks Schematron can't express (path normalisation/uniqueness semantics,
   deposit-vs-METS consistency) in native code on each side, specified by the doc.

Registering a formal LoC profile can be layered on later *from* the 02b doc if Leeds wants the
public statement; it should not gate this work.

## Documentation impact (docs repo, `mets-profiles` branch — these docs are living, not frozen)

- **`02b-METS-Written-by-the-Platform.md`** — the "ID conventions" section is rewritten by this
  fix: prefix + `EncodeLocalName(path)` becomes the specified form; the current raw-path form is
  kept, demoted to a "legacy form (read/edit-compatible, never minted after <release>)"
  subsection; the existing NOTE about #188 is replaced by the new normative text. The
  "navigable by path" claim changes: navigation is by `premis:originalName`/`FLocat` (the
  cache), with path-derived IDs retained as a deterministic convenience, not a lookup contract.
  Examples (skeleton, populated structMap, fileSec, digiprovMD, complete example) all need their
  IDs re-rendered in encoded form for the ad-hoc div and any path containing `/` or spaces.
- **`02c-METS-Parsing.md`** — small updates: the virus-scan digiprov match changes from
  "case-insensitively, by containment" to exact (case-insensitive) match; add an explicit
  statement of the parser principle "IDs are opaque; paths come from originalName/FLocat"
  (already true, worth stating normatively since it is what makes both ID generations
  co-readable).
- Both documents become the seed of the future **"what makes a METS file editable"**
  specification for third-party METS (see conformance section above).

## Sequencing note

Code changes start from `chore/sonar-cleanup-main` (assumed to merge to `main` soon), not from
`main`. The plan's branch diagram is updated accordingly. Plan documents continue to live on
`issue/118-invalid-xml-ids`.
