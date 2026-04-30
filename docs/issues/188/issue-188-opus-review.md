# Issue #188 — Opus Review of METS ID Analysis

Second-pass review by Claude Opus 4.7 of the Sonnet 4.6 analysis in `issue-188-analysis.md`.
Focus: gaps, errors, risks, and alternatives not covered in the original.

---

## 1. Errors in the Original Analysis

### 1.1 `XmlConvert.EncodeLocalName` round-trip story is underspecified

The analysis treats `XmlConvert.EncodeLocalName` as a settled solution without addressing the
round-trip. `EncodeLocalName` escapes literal `_x` sequences as `_x005F_x` to prevent ambiguity
with its own escape sequences, so the encoding *is* bijective and `XmlConvert.DecodeName` is
the correct inverse. No consumer currently decodes IDs (all treat them as opaque), so this is not
an immediate problem. But the analysis should state this explicitly: if any future consumer ever
needs to recover a path from an ID, `XmlConvert.DecodeName` is the correct and safe inverse.

### 1.2 The ModsManager DMD derivation perpetuates invalid IDs in legacy METS

The analysis correctly identifies that the DMD/PHYS coupling is self-consistent across both
formats. What it misses: when a user edits an old deposit and `GetModsForDiv(..., createDmd:true)`
is triggered on a div that has no existing DMDID, the code mints `DMD_metadata/ad-hoc` (with
the raw `/`). This **adds new schema-invalid XML to a previously legacy file**. The analysis
treats this as backward-compatible; it is, for navigation purposes, but it is a fixity-perpetuating
regression — the act of editing a legacy deposit makes it *more* invalid.

### 1.3 File div navigation is glossed over

The analysis acknowledges that file divs have no `Admid` and proposes using `FLocat.Href` for
the lookup. What it glosses over: `LocateMetsDivByLocalPath` is called for both files and
directories at every step of the walk. The implementation must explicitly branch on `Type` —
try directory check (Admid → premis:originalName) first, then file check (Fptr → file →
FLocat.Href). The proposed `FindChildDivByPath` sketch in the analysis is incomplete for this
branching logic, and the type-based branching is non-trivial when the final segment could be
either.

### 1.4 Step 1 changes error semantics, not just implementation

The analysis states "no test assertions break" in Step 1. True for attribute-value assertions.
But the navigation change shifts error behaviour: if two divs ever share the same
`premis:originalName` (malformed METS), the old `SingleOrDefault` would throw; the new code
silently picks one. Conversely, if `premis:originalName` doesn't exactly match `localPath`
(e.g. a leading `./` or BagIt `data/` prefix not stripped), the new code finds nothing where
the old code would succeed. Step 1 must include defensive normalisation of `premis:originalName`
before comparison, and tests covering these edge cases.

---

## 2. Missing Considerations

### 2.1 OCFL versioning impact — the biggest gap

This is entirely absent from the analysis. The METS file is preserved into OCFL as a binary.
Changing the ID minting scheme produces a different METS file for the same logical content.
Consequences:

- Any existing Archival Group, when next edited, writes a METS with completely different IDs.
  The diff against the previous OCFL version shows a massive change for what is logically a
  no-op ID rename plus the actual edit. This OCFL version artefact is permanent.
- The Activity Stream fires for every such edit, and iiif-builder rebuilds every IIIF manifest
  touched post-deploy. Manageable, but should be sequenced (quiet period or accepted as a
  known cost).
- Without a one-shot migration, schema validation of the historical corpus is permanently
  impossible. The analysis treats legacy tolerance as a feature; it is also a permanent liability.
  A bulk rewrite option — a new OCFL version per AG that updates the METS in-place — should
  at least be assessed and explicitly declined or deferred, not ignored.

### 2.2 The persistent mixed-format universe

Once Step 2 deploys, any single METS file edited after deployment may contain a mixture of:
- Legacy raw-`/` IDs (pre-existing structure)
- New encoded IDs (additions made post-deploy)

An XML schema validator will still reject the file. The fix is only partial on a per-file basis
until every element in that file has been through the new minting code. This is not called out
as a long-term cost.

### 2.3 `VirusProvEventPrefix` substring lookup

`MetsParser` (around line 506) uses a case-insensitive substring search:
```csharp
var matchingKey = lookupMaps.DigiprovMdMap.Keys
    .FirstOrDefault(k => k.ToLower().Contains(lowerKey));
```
where `lowerKey` contains the AdmId. With encoded paths, one path's encoded form could be a
prefix of another's (e.g. `"ADM_a"` is a substring of `"ADM_a_x002F_b"`), causing cross-talk
where the wrong digiprov entry is matched. Step 2 doesn't introduce this bug but it was
already latent and this work is an opportunity to fix it.

### 2.4 Diff logic and other Storage.Repository.Common readers

The analysis's "format-agnostic" list covers `MetsParser` and `iiif-builder` but does not
verify the Storage.Repository.Common diff logic and `WorkspaceManager`. The diff engine
generates `BinariesToAdd`/`BinariesToPatch` etc. from `CombinedDirectory` — it uses
`LocalPath`, not METS IDs, so it should be unaffected. But this should be stated explicitly,
not assumed. A grep across `Storage.Repository.Common` for the prefix constants would close
this.

### 2.5 Pipeline API as a downstream writer

The analysis flags `MetadataManager` for Step 2 but does not state explicitly that
`Pipeline.API` calls into `MetadataManager` to write characterisation metadata back into
METS (FILE_, ADM_, TECH_ IDs for files). The Pipeline service is a separate deployment that
must ship the Step 2 change at the same time — or before — as `Preservation.API`. A stale
Pipeline service writing old-format IDs alongside new-format IDs from MetsManager would
produce the mixed-format problem immediately rather than only when editing old deposits.

### 2.6 `MetadataManager` ADMID-join workaround becomes redundant

`MetadataManager` contains a `string.Join(' ', ctx.File.Admid)` workaround for the case where
an IDREFS attribute is split on whitespace — which today can happen because file paths with
spaces produce IDs like `"ADM_objects/my file.pdf"` that are split by the XML processor into
`["ADM_objects/my", "file.pdf"]`. After Step 2, all IDs have no spaces, so the workaround
is unnecessary for new METS. It must be **kept** for backward compatibility with legacy files.
The analysis does not call this out as a now-redundant-but-keep-it-for-legacy artefact; without
an explicit note it may be deleted by a well-meaning developer.

### 2.7 Fixture files must be frozen

The committed test fixtures designed to exercise paths with spaces and special characters
(e.g. `path-fixture-spaces.xml`) are valuable as **legacy-METS regression fixtures** after
Step 2. Their fixture-generator methods would produce *different* output after Step 2 (encoded
IDs). These fixtures must be explicitly frozen — not regenerated. Otherwise a developer
regenerating them erases the legacy-tolerance test coverage. This should be documented in the
Step 2 PR.

### 2.8 `MetadataManager`-generated `DivId` and `AdmId` surface to clients

`MetsParser` populates `MetsExtensions.DivId` and `AdmId` on parsed objects (lines 353-354,
601-602). If any UI code or test does a substring match (e.g. `AdmId.Contains("/")` to detect
"needs migration"), that check silently flips post-Step-2. The analysis says the UI uses these
as opaque strings — true for the current code — but it should be stated as a constraint that
must be enforced going forward.

---

## 3. Risks and Concerns

### 3.1 Step 1 is untestable in production until Step 2 ships

Between Step 1 deploy and Step 2 deploy, the path-cache or premis:originalName navigation code
runs but produces identical results to the old ID-walk (since all existing IDs are still
path-based). The new code paths are essentially dead code in production. If the path-cache
approach is taken, the cache invariants need exhaustive unit tests because there is no
production traffic exercising the new path until Step 2.

### 3.2 Encoding must cover far more than `/`

The analysis frames this as a `/`-fix in several places. A reader could reach for
`Replace('/', '_')` and consider it done. The full set of NCName-invalid characters that can
appear in user filenames includes: space, `&`, `<`, `>`, `"`, `'`, `,`, `;`, `(`, `)`, `[`,
`]`, `{`, `}`, `#`, `?`, `*`, `!`, `@`, `$`, `%`, `^`, `+`, `=`, `~`, and leading digits.
`XmlConvert.EncodeLocalName` handles all of these, but this must be stated explicitly and
tested with representative samples — not implied by the choice of encoding function.

### 3.3 DMD-from-PHYS derivation in `ModsManager` is an unnecessary and fragile coupling

The analysis says this coupling is "self-consistent" and safe. It is — today. But it is safe
only because: (a) `PHYS_` never appears inside an encoded path segment, and (b) `EncodeLocalName`
never produces output that begins with characters that would survive the `RemoveStart("PHYS_")`
incorrectly. These are coincidental properties. The fix is to derive the DMD ID from the same
`localPath` that was used to mint the PHYS ID, rather than by stripping a prefix from `div.Id`.
This eliminates the coupling entirely and removes the need to document it as "must be kept in sync."

### 3.4 Mixed Pipeline / Preservation API deployment window

If Pipeline API and Preservation API are deployed at different times during Step 2 rollout,
there is a window where one service mints old-format IDs and the other mints new-format IDs
into the same METS file. Given both services write via `MetadataManager`, the change must be
coordinated as a single atomic deploy.

---

## 4. Better Alternatives

### 4.1 Promote the path-cache to primary recommendation

The analysis presents the `Dictionary<string, DivType>` path-cache as a passing alternative
to premis:originalName navigation. It should be the primary recommendation:

- Populated by `MetsParser` in a single pass over the PHYSICAL structMap (using premis:originalName
  for directories, `FLocat.Href` for files)
- Maintained by every `MetsManager` mutation (`AddNewDirectory`, `AddNewFile`, `DeleteDiv`)
- O(1) lookup, no XmlGen typed-hierarchy traversal required
- Survives any future ID format change with zero further navigation code changes
- Testable in isolation
- Handles the partial-depth case naturally (lookup returns null → break, as now)

The "needs verification against XmlGen typed object hierarchy" caveat disappears entirely.

### 4.2 Derive DMD ID from `localPath`, not from `div.Id`

Replace ModsManager line 163:
```csharp
// current — fragile, implicit coupling to PHYS prefix
Constants.DmdIdPrefix + div.Id.RemoveStart(Constants.PhysIdPrefix)

// proposed — explicit, decoupled
Constants.DmdIdPrefix + XmlConvert.EncodeLocalName(localPath)
```
This requires passing `localPath` into the call site, but eliminates the implicit PHYS/DMD
coupling entirely.

### 4.3 Schema validation as a merge gate for Step 2

Add a test that validates the produced METS against the METS XSD. Today nothing in the test
suite catches invalid IDs because the round-trip goes through XmlSerializer, which does not
enforce `xs:ID`. A schema validation pass on any generated METS would have caught the original
bug and would catch future regressions of this class. This should be a merge condition for
Step 2.

### 4.4 A third step: one-shot legacy migration

Steps 1 and 2 leave the existing corpus in a permanently mixed state. For a preservation
system, consider a Step 3: a one-shot administrative operation that, for each Archival Group,
reads the METS, rewrites all IDs to the encoded form, and writes a new OCFL version. The OCFL
version provides the audit trail. This is the only way to make the statement "our METS files
are schema-valid" true for the full archive, not just for deposits created after the deploy.

### 4.5 Fix `VirusProvEventPrefix` substring lookup independently

The substring-based digiprov lookup in `MetsParser` is a latent bug independent of this work.
It should be fixed separately (exact-prefix match rather than substring) to avoid the risk of
cross-talk in archives with many files whose encoded paths share common prefixes.

---

## Summary of Biggest Gaps

| # | Gap | Severity |
|---|---|---|
| 1 | OCFL versioning impact not discussed — permanent artefact per edited AG | High |
| 2 | Path-cache presented as alternative, not primary — premis:originalName approach has unresolved XmlGen traversal question | High |
| 3 | Schema validation not required as merge gate | High |
| 4 | Mixed Pipeline/Preservation deploy window not flagged | High |
| 5 | ModsManager DMD derivation from `div.Id` should be rewritten to use `localPath` directly | Medium |
| 6 | Encoding framed as `/`-fix; full NCName-invalid character set not called out | Medium |
| 7 | Persistent mixed-format universe presented as success, not also as liability | Medium |
| 8 | `VirusProvEventPrefix` substring lookup fragility | Medium |
| 9 | ADMID-join workaround must be kept but flagged as legacy-only | Low |
| 10 | Fixture files must be frozen, not regenerated | Low |
| 11 | Step 1 dead code window — untestable in production until Step 2 | Low |
