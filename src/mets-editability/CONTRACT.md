# The editability judge: shared contract

One behaviour, two implementations — `src/mets-editability/` (Python) and
`DigitalPreservation.Mets.Conformance` (.NET). Both implement **this contract**, which itself
implements `02e-METS-Editability.md` in the docs repo. When the implementations disagree, this
file and 02e decide which one is wrong; the implementations are written from the spec, not from
each other.

## Verdicts

Exactly one of, in order of testing:

| Verdict | Meaning |
|---|---|
| `editable` | Conforms to the platform tier (02b shape). May carry `LEGACY_IDS` — pre-#214 raw-path IDs do not demote the verdict; the #188 migration retires them. |
| `editable-with-normalisation` | Conforms to the EPrints tier under the declared assumptions; a save will restructure to the 02b shape. The judgement lists the mutations. |
| `navigable-read-only` | Every file referenced by the physical structMap resolves to a unique, deposit-relative path, but neither editable tier is met. |
| `not-editable` | Anything else. |

The judge judges **the document**. Policy that is not decidable from the document — the
living-external-editor principle that keeps Goobi read-only regardless of shape — is applied by
the platform on top of the verdict, not by the judge.

## Judgement shape

```json
{
  "verdict": "editable-with-normalisation",
  "fileCount": 355,
  "reasons":     [ {"code": "...", "message": "..."} ],
  "assumptions": [ {"code": "...", "message": "..."} ],
  "notes":       [ {"code": "...", "message": "..."} ],
  "mutations":   [ "..." ]
}
```

- `reasons` — why the verdict is not higher: tier-rule failures and blockers. Empty for `editable`.
- `assumptions` — what the EPrints tier satisfied by assumption rather than assertion.
- `notes` — informational: legacy IDs, corpus quirks, directory divs without ADMID.
- `mutations` — only for `editable-with-normalisation`: what the first save will do, in order.

## Physical structMap selection

1. The first structMap whose `TYPE` is `physical` case-insensitively. If the spelling is not
   exactly `PHYSICAL`, note assumption `CASE_INSENSITIVE_STRUCTMAP_TYPE`.
2. Otherwise the first structMap with no `TYPE` at all — assumption
   `UNTYPED_STRUCTMAP_ASSUMED_PHYSICAL`.
3. Otherwise `not-editable` with `NO_PHYSICAL_STRUCTMAP`.

More than one candidate in the chosen category: note `MULTIPLE_PHYSICAL_CANDIDATES`; the first is
judged.

## Navigability (native, both sides)

Walk the chosen structMap's div tree; resolve every `fptr` (its `FILEID`, or its `area/@FILEID`)
through the fileSec to an `FLocat/@xlink:href`. Blockers:

| Code | Condition |
|---|---|
| `PARSE_FAILED` | Not well-formed XML, or no `mets:mets` element |
| `NO_ROOT_DIV` | The chosen structMap has no div |
| `FILEID_UNRESOLVED` | An fptr references a FILEID no `mets:file` declares |
| `FILE_NO_HREF` | A referenced file has no `FLocat/@xlink:href` |
| `HREF_NOT_DEPOSIT_RELATIVE` | An href has a URI scheme, starts with `/` or `\`, or contains a `..` segment — the standing guard |
| `DUPLICATE_PATH` | Two referenced files resolve to the same normalised path |
| `DUPLICATE_ID` | Two elements declare the same `ID` |

Navigable ⇔ at least one file resolved and no blocker occurred. Directory divs without an
`ADMID` are **not** blockers (their own paths are unresolvable, but the files' are not):
note `DIRECTORY_DIV_NO_ADMID` with a count.

## The common rules — the whole editable surface

Editing is not only the physical tree: the platform edits logical structMaps (whole-file
pointers, time segments, image regions), file-to-file links, and descriptive metadata.
**Editability means the platform understands the document and can change it** — so both editable
tiers additionally require the Schematron rules in `schematron/common.sch`:

| Code | Rule |
|---|---|
| `C_FILEID_RESOLVES` | Every `fptr/@FILEID` in **every** structMap — logical included — resolves |
| `C_AREA_FILEID_RESOLVES` | Every `area/@FILEID` (time segment, image region) resolves |
| `C_SMLINK_FROM_RESOLVES` / `C_SMLINK_TO_RESOLVES` | Both ends of every `smLink` resolve — to a file (the platform's arcrole style) or a div (Goobi's logical-to-physical style), by raw string |
| `C_LOGICAL_ROOT_HAS_ID` | Every logical structMap's root div has an ID — logical maps are edited *by address* (replaced, reordered, removed by root div ID), and an ID-less one is present but unchangeable |

A common-rule failure blocks both tiers: the document is at best `navigable-read-only`.

Deliberately **not** rules:

- **DMDID resolution.** A dangling DMDID is by design in the platform's own skeleton (dmdSecs are
  created lazily). Whether a *resolved* dmdSec is editable is the `FOREIGN_DMDSEC` note below,
  never a demotion.
- **`smLinkGrp` (`smLocatorLink`/`smArcLink`) resolution.** No document in the corpus uses it,
  the platform never writes it, and edits never touch it (it is preserved as parsed);
  `smLocatorLink/@xlink:href` can legitimately be an external URI, so a resolution rule invented
  without real documents to validate against would be guessing. Revisit if one ever appears.

## The platform tier (`editable`)

Navigable, the common rules pass, **and** the Schematron rules in
`schematron/platform-tier.sch` all pass (`P_PHYSICAL_STRUCTMAP`, `P_AGENT`,
`P_SINGLE_OBJECTS_GROUP`, `P_DIV_TYPED`, `P_ITEM_ONE_FPTR`, `P_DIRECTORY_ADMID`, `P_SHA256` —
every edit ends in an import job, and import jobs require SHA256 fixity, so a platform-shape
document that has lost its digests cannot complete an edit-and-preserve).

IDs that are not legal NCNames are counted and reported as note `LEGACY_IDS`, never a demotion:
a pre-#214 platform document is editable today and the migration, not the judge, retires its IDs.

## The EPrints tier (`editable-with-normalisation`)

Navigable, the common rules pass, **and** the Schematron rules in
`schematron/eprints-tier.sch` all pass (`E_NO_PHYSICAL_CANDIDATE`, `E_NOT_FLAT`,
`E_ITEM_ONE_FPTR`, `E_FILE_HAS_HREF`, `E_HREF_UNDER_OBJECTS`, `E_SHA256`,
`E_MIXED_FILEGRP_USE`), **and** every declared ID is a legal NCName (an invalid ID would need
the #188 normalisation this tier does not perform — failure is reported as `INVALID_IDS` and the
tier is not met).

Assumptions recorded when exercised: `UNTYPED_STRUCTMAP_ASSUMED_PHYSICAL` /
`CASE_INSENSITIVE_STRUCTMAP_TYPE`, `UNTYPED_DIV_ASSUMED_ITEM` (with count),
`IMPLIED_OBJECTS_DIV`.

Mutations, in save order:

1. `set TYPE="PHYSICAL" on the structMap` (when not already exact)
2. `set TYPE="Directory" on the root div` (when untyped)
3. `set TYPE="Item" on N file div(s)` (when untyped)
4. `materialise the objects Directory div (amdSec/techMD with premis:originalName) and re-parent
   N file div(s) under it`
5. `consolidate K fileGrp(s) into one USE="OBJECTS" group` (K > 1, or one group with another USE)
6. `wrap the payload of N mdWrap(s) in the mets:xmlData element the schema requires` (when the
   `NO_XMLDATA_WRAPPER` quirk is present — the payload itself is preserved verbatim)
7. `append the platform agent to metsHdr`

## Quirk notes — recorded for every document, whatever the verdict

| Code | Meaning |
|---|---|
| `FOREIGN_STORAGE_LOCATION` | A `premis:storage` whose `storageMedium` is not the platform agent (the EPrints `file://` server paths). History, never read as the file's location — see #236 |
| `METS_NAMESPACE_RECORD_INFO` | EPrints record identifiers declared in the METS namespace, invisible to the parser — see #237 |
| `FOREIGN_DMDSEC` | A div's DMDID resolves to a dmdSec claiming MODS (`MDTYPE="MODS"`) with no `mods:mods` record — the EPrints root dmdSec shape. **The platform never edits such a section.** A descriptive-metadata edit on that div creates a *new* platform dmdSec and **appends** its ID to the div's `DMDID` (DMDID is IDREFS), leaving the original untouched, byte for byte |
| `NO_XMLDATA_WRAPPER` | An `mdWrap` holds its payload directly, without the `binData`/`xmlData` child the schema requires (EPrints puts `premis:object` straight inside `mdWrap`). A save **normalises** this — mutation 6 — because any typed round-trip would otherwise silently drop the payload; the wrapped content is preserved verbatim |

## navigable-read-only / not-editable

Navigable but neither tier met → `navigable-read-only`, with both tiers' failures in `reasons`.
Not navigable → `not-editable`, with the blockers in `reasons`.

## Acceptance

Both implementations must produce these verdicts over the repository's sample corpus
(`src/DigitalPreservation/XmlGen.Tests/Samples/`):

| Document | Verdict |
|---|---|
| `simple-image.mets.xml` | `editable` |
| `wow.mets.xml` | `editable` |
| `path-fixture-spaces.xml` | `editable` + note `LEGACY_IDS` |
| `EPrints.10315.METS.xml` | `editable-with-normalisation` |
| `archivematica-wc-METS.299eb16f-….xml` | `navigable-read-only` + note `DIRECTORY_DIV_NO_ADMID` |
| `goobi-wc-b29356350.xml` | `navigable-read-only` (relative paths, but neither tier) |
| `goobi-2026.xml` | `not-editable` (`HREF_NOT_DEPOSIT_RELATIVE` — absolute IIIF URLs) |

This is the measured table from #223 re-expressed as verdicts, and it is enforced by tests on
both sides.
