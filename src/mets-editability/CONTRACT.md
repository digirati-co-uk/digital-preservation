# The editability judge: shared contract

One behaviour, two implementations — `src/mets-editability/` (Python) and
`DigitalPreservation.Mets.Conformance` (.NET). Both implement **this contract**, which itself
implements the docs site's "METS editability" page. When the implementations disagree, this file
decides which one is wrong; the implementations are written from the spec, not from each other.

Agreement is tested, not assumed: `fixtures/judgements.json` holds the expected judgement of one
curated document per rule and per parity edge, and **both** test suites assert every entry.

## Verdicts

Exactly one of, in order of testing:

| Verdict | Meaning |
|---|---|
| `editable` | Conforms to the platform tier (the shape the platform writes). May carry `LEGACY_IDS` — pre-#214 raw-path IDs do not demote the verdict; the #188 migration retires them. |
| `editable-with-normalisation` | Conforms to the EPrints tier under the declared assumptions; a save will restructure to the platform's shape. The judgement lists the mutations. |
| `navigable-read-only` | The platform can open the document and every file the physical structMap references resolves to a unique, deposit-relative path, but neither editable tier is met. |
| `not-editable` | Anything else — above all, any document the platform's parser cannot load. |

The judge judges **the document**. Policy that is not decidable from the document — the
living-external-editor principle that keeps Goobi read-only regardless of shape — is applied by
the platform on top of the verdict, not by the judge.

## Judgement shape

```json
{
  "verdict": "editable-with-normalisation",
  "fileCount": 355,
  "reasons":     [ {"code": "...", "message": "...", "count": 1} ],
  "assumptions": [ {"code": "...", "message": "...", "count": 1} ],
  "notes":       [ {"code": "...", "message": "...", "count": 1} ],
  "mutations":   [ "..." ]
}
```

- `reasons` — why the verdict is not higher: blockers and tier-rule failures. Empty for `editable`.
- `assumptions` — what the EPrints tier satisfied by assumption rather than assertion.
- `notes` — informational: legacy IDs, corpus quirks, directory divs without ADMID, no files yet.
- `mutations` — only for `editable-with-normalisation`: what the first save will do, in order.

Each code appears at most once per list. A finding that occurs several times is reported once:
`message` describes the first occurrence (or, for document-wide counts, says how many) and
`count` says how many there were. Codes, counts and order are identical in both judges, and so
is every message except `PARSE_FAILED`'s, which quotes the XML parser's own error.

## Before anything is walked

Reading a file, the judge reads **only a local file** (never a URL) and refuses, in a streaming
pass before the document is built:

| Code | Condition |
|---|---|
| `PARSE_FAILED` | The file cannot be read, or is not well-formed XML; or (later) it has no `mets:mets` element. A missing file is a judgement, never a crash |
| `DOCTYPE_DECLARED` | The document declares a DTD. XML parsers disagree on what entity declarations expand to (lxml refused an external entity that .NET expanded to nothing), so the judge accepts none |
| `TOO_DEEP` | Elements nest more than 256 deep (the document element is depth 1). .NET's XSLT engine recurses with the tree, and a deep enough document overflows its stack — an exception no caller can catch |
| `SEVERAL_METS_ELEMENTS` | The document holds more than one `mets:mets` — for instance a METS document embedded in an `xmlData` payload. The platform's parser searches the whole document by element name, so it would read the other document's structMaps, files and sections as this one's |

A document the caller has already parsed is checked for the same DTD and depth conditions.

## Physical structMap selection

The judge walks the map the platform's parser walks:

1. The first structMap whose `TYPE` is `physical` in any ASCII case. If the spelling is not
   exactly `PHYSICAL`, note assumption `CASE_INSENSITIVE_STRUCTMAP_TYPE`.
2. Otherwise the first structMap whose `TYPE` is not `logical` (in any ASCII case). If it has no
   `TYPE`, note assumption `UNTYPED_STRUCTMAP_ASSUMED_PHYSICAL`. If it has some other `TYPE`
   (`OTHER`, an empty string, ...) the parser would still walk it, but nothing says it is the
   physical map: `not-editable` with `NO_PHYSICAL_STRUCTMAP`.
3. Otherwise `not-editable` with `NO_PHYSICAL_STRUCTMAP`.

TYPE values are compared in ASCII case only (`physıcal`, with a dotless i, is not physical): for
these all-ASCII words that is exactly what the parser's `ToLowerInvariant` comparison accepts.

More than one candidate in the chosen category: note `MULTIPLE_PHYSICAL_CANDIDATES` (with the
count); the first is judged. **Only the chosen map is judged, everywhere**: the Schematron
structural rules are scoped to it too (`schematron/chosen-structmap.ent`), so an unchosen
sibling map — preserved as parsed, never edited — can neither demote the verdict nor satisfy a
tier on the walked map's behalf.

## Navigability (native, both sides)

Walk every div of the chosen structMap in document order, as the parser does, and resolve every
physical `fptr/@FILEID` through `fileSec/fileGrp//file` to its single `FLocat/@xlink:href`.

The judge must never be more tolerant than the machinery it vouches for. **Any document the
platform's parser would throw on while loading is `not-editable`** — never one of the three
openable verdicts — and each such case has its own code. Every one of these codes is a blocker:

| Code | Condition | What the parser does |
|---|---|---|
| `NO_ROOT_DIV` | The chosen structMap has no div | — |
| `DUPLICATE_ID` | Two elements declare the same `ID` | builds its lookups by ID with `ToDictionary` and throws (the judge refuses a duplicate on any element, slightly more than the parser indexes) |
| `FPTR_NO_FILEID` | A physical `fptr` has no `FILEID` (an area-only pointer: legal in a logical map, where the platform writes them) | `GetRequiredFileId` dereferences null |
| `FILEID_UNRESOLVED` | A physical `fptr` references a FILEID no `mets:file` in a fileGrp declares (a `mets:file` directly under `fileSec` is not a file — METS requires the fileGrp) | `FileMap[id]` throws |
| `FILE_REFERENCED_TWICE` | One file is referenced from more than one physical `fptr` | lists it twice, then indexes the list by path and throws |
| `FILE_SEVERAL_FLOCATS` | A referenced file has more than one `FLocat` | `.Single()` throws |
| `FILE_NO_HREF` | A referenced file has no `FLocat`, or no (or an empty) `xlink:href` | `.Single()` / `!` throws |
| `HREF_NOT_DEPOSIT_RELATIVE` | An href has a URI scheme, starts with `/`, contains a backslash anywhere, or has a segment that is `..` or holds `/` or `\` — as given or percent-decoded once. The standing guard | refuses backslashes, dot segments and encoded separators |
| `HREF_NOT_NORMALISED` | An href has an empty or `.` segment, as given or percent-decoded (`objects//a.jpg`, `objects/%2e/a.jpg`, a trailing `/`). The platform's path cache does not normalise paths, so such an entry is real but unreachable | refuses `.` segments |
| `DUPLICATE_PATH` | Two **distinct** referenced files resolve to the same path | indexes files by path and throws |
| `DIRECTORY_NO_LABEL` | A div whose TYPE is `directory` (any ASCII case) has no `LABEL`, or an empty one — in either tier | throws |
| `DIRECTORY_PATH_INVALID` | A directory div's ADMID resolves to a section whose `premis:originalName` fails the two href checks above | refuses dot segments; a URL becomes a nonsense directory |
| `DIRECTORY_LOCATION_NOT_URI` | A directory's section holds the platform's own `premis:storage` (its `storageMedium` is the platform agent) with a non-blank `contentLocationValue` that is not an absolute URI, judged by a deliberately narrow pattern (`scheme://host[:port][/path]`) that `System.Uri` is known to accept. Another system's storage is never read | `new Uri(...)` throws |
| `PREMIS_REPEATED` | A section the parser reads holds more than one of an element it reads once: for a file's sections (resolved from the file's or its div's ADMID) `premis:fixity`, `size`, `originalName`, `format`, `formatName`, `formatRegistryKey` or the platform's `ExifMetadata`; for a directory's, `premis:originalName` | `SingleOrDefault` throws |
| `RECORD_IDENTIFIER_NO_SOURCE` | A dmdSec some div references holds a `mods:recordIdentifier` without `@source` | dereferences the missing attribute |
| `TIME_VALUE_BLANK` | An `area` has a blank `BEGIN` or `END`, or a `premis:significantProperties` Duration is blank | the time-code reader throws on a blank |

ADMID and DMDID are IDREFS and are resolved as the platform's `IdRefs` does: the whole value
first (a legacy platform ID can contain spaces), otherwise each XML-whitespace-separated token.
Where pinning down exactly which value the parser reads would take more code than it is worth,
the check covers more (every `area`, every referenced dmdSec) — a stricter judge is safe, a more
tolerant one is not.

**Known gaps**, deliberately not checked: the date of a virus-scan event, which the parser
converts with the *server's* culture (`Convert.ToDateTime`) — the platform itself writes dates
like `Wednesday, 18 March 2026`, so no culture-independent rule can mirror it; and an unrecognised
`area` carrying two attributes with the same local name in different namespaces.

Navigable ⇔ no blocker occurred. `fileCount` counts distinct files, not references. A document
whose structMap references no files is navigable, with the note `NO_FILES` — the platform's own
freshly-created deposit skeleton is exactly this, and once the verdict replaces the agent gate it
must be editable, or nothing could add the first file. Directory divs without an `ADMID` are
**not** blockers (their own paths are unresolvable, but the files' are not): note
`DIRECTORY_DIV_NO_ADMID` with a count.

## The common rules — the whole editable surface

Editing is not only the physical tree: the platform edits logical structMaps (whole-file
pointers, time segments, image regions), file-to-file links, and descriptive metadata.
**Editability means the platform understands the document and can change it** — so both editable
tiers additionally require the Schematron rules in `schematron/common.sch`:

| Code | Rule |
|---|---|
| `C_FILEID_RESOLVES` | Every `fptr/@FILEID` in **every** structMap — logical included — resolves |
| `C_AREA_FILEID_RESOLVES` | Every `area/@FILEID` (time segment, image region) resolves |
| `C_ONE_ROOT_DIV` | The chosen structMap has exactly one root div, as METS requires — the editing model keeps one, so a second would not survive a save |
| `C_ONE_FLOCAT` | Every `mets:file` in the fileSec, referenced or not, has exactly one `FLocat`, carrying an `xlink:href` |
| `C_LOGICAL_ROOT_HAS_ID` | Every root div of a logical structMap has a non-empty ID — logical maps are edited *by address* (replaced, reordered, removed by root div ID), and an ID-less one is present but unchangeable |
| `C_SMLINK_FROM_RESOLVES` / `C_SMLINK_TO_RESOLVES` | Both ends of every `smLink` resolve — to a file (the platform's arcrole style) or a div (Goobi's logical-to-physical style), by raw string. A missing end resolves to nothing |

A common-rule failure blocks both tiers: the document is at best `navigable-read-only`.

One further check gates both tiers natively, because DMDID token-splitting is not safe in
XPath 1.0: **`SHARED_DMDSEC`** — a MODS-editable dmdSec referenced from more than one div.
Editing metadata on one such div rewrites the section in place and silently changes the other
div's metadata too. A div that names the same dmdSec twice is not sharing it. A shared *foreign*
dmdSec is fine: the platform never edits those — it appends its own alongside.

Deliberately **not** rules:

- **DMDID resolution.** A dangling DMDID is by design in the platform's own skeleton (dmdSecs are
  created lazily). Whether a *resolved* dmdSec is editable is the `FOREIGN_DMDSEC` note below,
  never a demotion.
- **`smLinkGrp` (`smLocatorLink`/`smArcLink`) resolution.** No document in the corpus uses it,
  the platform never writes it, and edits never touch it (it is preserved as parsed);
  `smLocatorLink/@xlink:href` can legitimately be an external URI, so a resolution rule invented
  without real documents to validate against would be guessing. Revisit if one ever appears.

## The platform tier (`editable`)

Navigable, the common rules pass, no `SHARED_DMDSEC`, **and** the Schematron rules in
`schematron/platform-tier.sch` all pass:

| Code | Rule |
|---|---|
| `P_PHYSICAL_STRUCTMAP` | The **chosen** map is exactly `TYPE="PHYSICAL"` — a conformant sibling map the judge never walked cannot carry the tier |
| `P_AGENT` | `metsHdr` has an agent with a non-empty name |
| `P_SINGLE_OBJECTS_GROUP` | Exactly one fileGrp in the whole fileSec (nested ones counted), `USE="OBJECTS"` |
| `P_DIV_TYPED` | Every div is exactly `TYPE="Directory"` or `TYPE="Item"` |
| `P_ITEM_ONE_FPTR` | Every Item carries exactly one fptr |
| `P_DIRECTORY_ADMID` | Every Directory below the root anchors its path: its ADMID names an amdSec carrying exactly one `premis:originalName` (an empty or dangling ADMID anchors nothing) |
| `P_SHA256` | Every file's ADMID resolves to SHA256 fixity whose digest is 64 hex digits: every edit ends in an import job, and import jobs compare it with the file's actual checksum |

IDs that are not legal NCNames are counted and reported as note `LEGACY_IDS`, never a demotion:
a pre-#214 platform document is editable today and the migration, not the judge, retires its IDs.

## The EPrints tier (`editable-with-normalisation`)

Navigable, the common rules pass, no `SHARED_DMDSEC`, every declared ID is a legal NCName (an
invalid ID would need the #188 normalisation this tier does not perform — failure is reported as
`INVALID_IDS` and the tier is not met), **and** the Schematron rules in
`schematron/eprints-tier.sch` all pass. Together they describe exactly the shape the mutations
below turn into a platform document:

| Code | Rule |
|---|---|
| `E_HAS_FILES` | The root holds at least one file div — an EPrints-shaped document with no files has nothing to restructure |
| `E_NOT_FLAT` | No div is nested more than one level below the root |
| `E_ROOT_NO_FPTR` | The root div points at no file itself |
| `E_ROOT_TYPE` | The root div is untyped or `Directory` (any ASCII case) |
| `E_FILE_DIV_TYPE` | Every file div is untyped or `Item` (any ASCII case) — EPrints' own vocabulary (`eprint`, `page`) would need retyping the save does not do |
| `E_ITEM_ONE_FPTR` | Every file div carries exactly one fptr |
| `E_HREF_UNDER_OBJECTS` | Every file in the fileSec (nested groups included) sits **directly** in `objects/` — the save materialises one directory, `objects`, and a file in `objects/sub/` would need more |
| `E_SHA256` | As `P_SHA256` |
| `E_FLAT_FILEGRPS` | No fileGrp is nested in another — nested groups have no single obvious consolidation |
| `E_MIXED_FILEGRP_USE` | All fileGrps — referenced or not, since all are consolidated — agree on USE, and none lacks it while another has it |

Assumptions recorded when exercised: `UNTYPED_STRUCTMAP_ASSUMED_PHYSICAL` /
`CASE_INSENSITIVE_STRUCTMAP_TYPE`, `UNTYPED_DIV_ASSUMED_ITEM` (with count),
`CASE_INSENSITIVE_DIV_TYPE` (with count), `IMPLIED_OBJECTS_DIV`.

Mutations, in save order:

1. `set TYPE="PHYSICAL" on the structMap` (when not exactly that)
2. `set TYPE="Directory" on the root div` (when not exactly that)
3. `set LABEL="__ROOT" on the root div` (when it has none — the parser requires a LABEL on every
   Directory, and `__ROOT` is what the platform writes)
4. `set TYPE="Item" on N file div(s)` (those not exactly `Item`)
5. `materialise the objects Directory div (amdSec/techMD with premis:originalName) and re-parent
   N file div(s) under it`
6. `consolidate K fileGrp(s) into one USE="OBJECTS" group` (K counts every group; listed when
   K > 1, or one group with another USE)
7. `wrap the payload of N mdWrap(s) in the mets:xmlData element the schema requires` (when the
   `NO_XMLDATA_WRAPPER` quirk is present — the payload itself is preserved verbatim)
8. `append the platform agent to metsHdr`

`fixtures/eprints-sample-after-save.xml` is `EPrints.10315.METS.xml` with these eight applied by
hand; it is `editable`. (The EPrints root div keeps its ADMID, which names the amdSec holding
every file's techMD. Once the root is a Directory the parser reads that amdSec as the root's
metadata - which is safe only because EPrints techMDs carry no `premis:originalName`, and the
parser reads storage only from the platform's own `premis:storage`.)

## Quirk notes — recorded for every document, whatever the verdict

| Code | Meaning |
|---|---|
| `FOREIGN_STORAGE_LOCATION` | A `premis:storage` whose `storageMedium` is not the platform agent (the EPrints `file://` server paths). The platform reads only its own storage (#236), so these are kept as history and never read as a location |
| `METS_NAMESPACE_RECORD_INFO` | EPrints record identifiers declared in the METS namespace. The parser reads them because the dmdSec claims MODS (#237); a platform edit adds `mods:recordIdentifier` elements alongside, leaving these untouched |
| `FOREIGN_DMDSEC` | A div's DMDID resolves to a dmdSec claiming MODS (`MDTYPE="MODS"`) with no `mods:mods` record — the EPrints root dmdSec shape. **The platform never edits such a section.** A descriptive-metadata edit on that div creates a *new* platform dmdSec and **appends** its ID to the div's `DMDID` (DMDID is IDREFS), leaving the original untouched, byte for byte. The append rule presumes the parser reads *every* resolved dmdSec of a div — which it does not yet (it reads the first that resolves). That is a Phase 4 prerequisite |
| `ADMID_OUTSIDE_SURVIVAL_INDEX` | A non-empty ADMID sits on a `fileGrp`, `area` or `stream` element, which the platform's deletion reasoning does not consult — sections referenced only from there could be swept by an edit |
| `NO_XMLDATA_WRAPPER` | An `mdWrap` holds its payload directly, without the `binData`/`xmlData` child the schema requires (EPrints puts `premis:object` straight inside `mdWrap`). A save **normalises** this — mutation 7 — because any typed round-trip would otherwise silently drop the payload |

Text content is read as the element's string value (all its text, comments excluded) on both
sides.

## Acceptance

Both implementations must produce these verdicts over the repository's sample corpus
(`src/DigitalPreservation/XmlGen.Tests/Samples/`):

| Document | Verdict |
|---|---|
| `simple-image.mets.xml` | `editable` |
| `wow.mets.xml` | `navigable-read-only` (`P_SHA256`: a hand-made fixture whose digests are 8-character placeholders) |
| `path-fixture-spaces.xml` | `editable` + note `LEGACY_IDS` |
| `EPrints.10315.METS.xml` | `editable-with-normalisation` |
| `archivematica-wc-METS.299eb16f-….xml` | `navigable-read-only` + note `DIRECTORY_DIV_NO_ADMID` |
| `goobi-wc-b29356350.xml` | `navigable-read-only` (relative paths, but neither tier) |
| `goobi-2026.xml` | `not-editable` (`HREF_NOT_DEPOSIT_RELATIVE` — absolute IIIF URLs) |

This is the measured table from #223 re-expressed as verdicts, and it is enforced by tests on
both sides, next to the golden judgements in `fixtures/`.
