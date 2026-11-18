# Access conditions configuration

What a person can say about the access to a file, folder or logical range, and what the platform
writes into METS as a result, is configuration of the **Preservation API**. It is the only
access-related configuration in the stack, and it comes in two forms. Almost every installation
wants the first.

This page is for whoever deploys the platform. What a caller or a user sees is on the documentation
site: the API shape at [Vocabularies](https://digirati-co-uk.github.io/digital-preservation-docs/preservation-api/vocabularies/),
the METS at [Descriptive metadata and structure](https://digirati-co-uk.github.io/digital-preservation-docs/mets/mets-we-write-descriptive/),
and the extended form at [Derived access conditions](https://digirati-co-uk.github.io/digital-preservation-docs/mets/derived-access-conditions/).

## Which services read it

| Service | Reads the configuration? |
|---|---|
| Preservation API | **Yes.** Builds its profile from whichever form is configured at startup, validates it (a bad profile stops the service, with the reasons in the log), serves it at `GET /access-conditions`, and uses it when writing METS. |
| Preservation UI | No. It fetches the API's profile and caches it for five minutes, so a change needs only the API redeployed. If the API cannot be reached it uses the last profile this process fetched; if there has never been one, a METS write is **refused** (503, nothing written) rather than saved with derived values that no longer match the chosen ones. |
| Pipeline API, Deposit Archiver | No. Both write METS through the same `MetsManager`, but with no profile, which means they derive nothing and leave any derived elements they find untouched. |
| Storage API, Storage API Importer | No. They never look inside METS. |
| iiif-builder (or any other consumer) | Not configuration as such, but it can read the same profile from `GET /access-conditions` to turn the keys in METS into IIIF `rights` and `requiredStatement` - see [For iiif-builder](#for-iiif-builder). |

## The standard form: `AccessConditions`

Out of the box the platform records two things about a resource, each as a `mods:accessCondition`
in the METS, each chosen by a person and inherited down the folder tree:

- an **access restriction** - one value from a vocabulary you define (`type="restriction on access"`);
- a **rights statement** - one URI from the fixed [rightsstatements.org](https://rightsstatements.org/)
  and Creative Commons vocabularies (`type="use and reproduction"`).

Nothing is computed from them. All you configure is the restriction vocabulary, as a plain list in
`appsettings.json` (or environment variables with `AccessConditions__0__Value` style keys):

```json
"AccessConditions": [
  { "Value": "Open",       "Label": "Open" },
  { "Value": "Restricted", "Label": "Restricted" },
  { "Value": "Closed",     "Label": "Closed" }
]
```

`Value` is what is written to METS and returned in `accessRestrictions`; `Label` is what the UI
shows. The rights-statement vocabulary needs no configuration. One optional key sits beside the
list:

| Key | Default | Effect |
|---|---|---|
| `AllowMultipleAccessConditions` | `false` | `true` lets a resource carry several restriction values at once (the UI shows a multi-select; the METS gets one element per value). |

**With no `AccessConditions` at all** the service still starts: `GET /access-conditions` returns a
profile with an empty vocabulary, the UI's restriction dropdown offers only *(inherit from parent)*,
and rights statements work as normal.

That is the whole of the standard form. The rest of this page is for an installation whose rules
are richer; if that is not you, stop here.

## The statement profile: `AccessConditionProfileFile`

Leeds do not choose a rights statement. They choose a **specific copyright statement** from their
own list (orphan work under the 2039 rule, low risk, held online; in copyright, University of Leeds
the owner, not online...), which says three things at once:

- which **copyright status** it falls under (out of copyright, low risk, medium risk, high risk,
  unassessed, holder conditions) - their risk assessment;
- what text the public sees with the item - a IIIF `requiredStatement`, and for some a IIIF `rights` URI;
- together with the **Protected Access Level** (the restriction), the item's **Access Tier** (0-5),
  which their delivery systems map to IIIF authorisation roles.

So the extended form replaces the rights statement with a statement from a configured list, and
the configuration has to say, for each statement, its category and what it publishes; and how the
tier follows from restriction x category. That is too much, and too important to get right, for
`appsettings.json`, so it is a **document of its own**: a JSON file in the API's content root,
named by one key.

```json
"AccessConditionProfileFile": "AccessConditionProfiles/leeds.json"
```

Configure this **or** `AccessConditions` (with `AllowMultipleAccessConditions`), never both; the
service refuses to start. Profile documents live in `Preservation.API/AccessConditionProfiles/`
and are copied into the image. The document is the same JSON that `GET /access-conditions` serves,
property for property, so whatever reads one can read the other.

### The document

The top level is the standard model; everything the extension adds is under `statements`.

```jsonc
{
  "name": "Leeds University Library",          // informational
  "version": "2026-11-18-draft",               // informational: lets a consumer say which it read
  "restrictionAxis": {                         // the standard restriction dropdown
    "label": "Protected Access Level",
    "values": [ { "value": "PA1", "label": "Level 1" }, ... ]
  },
  "statements": {
    "label": "Copyright Statement",            // the dropdown that replaces the rights statement
    "type": "copyright statement",             // the mods:accessCondition/@type a chosen statement is written under
    "values": [
      {
        "value": "inc-low-risk-orphan-2039",   // the key written to METS - never change once used
        "label": "In Copyright: Assessed - LOW RISK (Online -orphan work-2039)",
        "category": "low-risk",                // one of categories: its matrix column
        "iiif": "orphan-2039-online",          // one of iiif.entries: what it publishes
        "description": "To be applied to unpublished material (archives) where ...",  // shown when chosen
        "externalIds": { "emu-sprig": [ "SPRIG-..." ], "emu-irn": [ "..." ] }         // optional
      },
      ...
    ],
    "categories": [ { "value": "low-risk", "label": "In Copyright: Assessed (Low Risk)", "description": "..." }, ... ],
    "iiif": {
      "requiredStatementLabel": "Rights",      // the requiredStatement label unless an entry gives its own
      "entries": [
        { "id": "out-of-copyright",
          "rights": "https://creativecommons.org/publicdomain/mark/1.0/",
          "requiredStatement": "This material is out of copyright." },
        { "id": "orphan-2039-online", "requiredStatement": "We believe this material is ..." },
        ...
      ]
    },
    "derived": [
      {
        "type": "tier",                        // the mods:accessCondition/@type it is written under
        "label": "Access Tier",
        "values": [ { "value": "0", "label": "Tier 0 - Open", "externalIds": { ... } }, ... ],  // least to most restrictive
        "matrix": {                            // restriction -> category -> value
          "PA1": { "public-domain": "0", "low-risk": "1", ..., "__none__": "2", "*": "3" },
          "PA2": { "*": "3" },
          ...
          "__none__": { "*": "5" },
          "*": { "*": "5" }
        }
      }
    ]
  }
}
```

Some choices worth knowing about:

- **Keys and labels are separate.** A statement's `value` goes into preserved METS and must never
  change; its `label`, `description` and IIIF text can change whenever Leeds like, and the next
  preview or manifest build shows the new wording.
- **IIIF text is stated once.** Statements reference an `iiif` entry by `id`, so two statements
  that publish the same words share one entry and cannot drift apart.
- **Categories are never written to METS.** A node's category is its statement's, looked up here.
  METS holds keys only: the restriction, the statement, the derived values.
- **`externalIds`** (on restriction values, statements and derived values) record the same thing's
  identifiers in other systems, by scheme - for Leeds, EMu Rights SPRIGs and IRNs - so a flow that
  starts from EMu (EMu → Goobi) can map them to keys. Within one list an identifier may belong to
  only one value.
- In a matrix, a row or column key may also be `__none__` ("no effective value on or above this
  node") or `*` ("anything a more specific key does not match"). The `*` row and column are what
  give a value outside the vocabulary - a legacy restriction, a statement since removed - a result.
- `derived` is optional. Statements with IIIF text and no tier is a valid profile; `category` is
  then optional too.

### Validation

The document is read strictly and checked at startup; any failure stops the service and the log
lists every problem found:

- **Reading:** an unknown or misspelt property is an error (comments and trailing commas are fine).
- **Keys:** restriction values, statement values, category values, IIIF entry ids and derived values
  must be non-blank (a blank means *(inherit from parent)* in the forms), unique, and not `__none__`
  or `*`.
- **References:** every statement's `category` must be one of `categories` (and, when anything is
  derived, every statement must have one); every `iiif` must be one of the entries; every matrix
  row must be a restriction value, `__none__` or `*`; every column a category, `__none__` or `*`;
  every cell one of the derived `values`.
- **Coverage:** every restriction value (and `__none__`) must resolve to a value for every category
  (and `__none__`). A gap would silently leave nodes without a tier.
- **Types:** `statements.type` and each derived `type` must be distinct, and none may be
  `restriction on access` or `use and reproduction`.
- **IIIF:** every entry has `rights` or `requiredStatement` or both; `rights` is an absolute http(s)
  URI; no text is blank.
- **External ids:** one identifier, one value, per list.
- `allowMultipleRestrictions` cannot be combined with `derived`: a derived value is one matrix cell.

### What happens

The detail is on the documentation site and in
[effective-metadata-inheritance.md](../effective-metadata-inheritance.md); in brief:

- The chosen statement is written as `mods:accessCondition type="{statements.type}"`, inherited down
  the tree exactly like the restriction (including a file's fallback to its logical range).
- The UI shows a statement dropdown, grouped by category, **in place of** the rights statement
  dropdown, with the chosen statement's `description` beneath it. A node's existing
  `use and reproduction` element is left exactly as it is: the form says nothing about rights.
- Every div, physical or logical, that carries a chosen element of its own gets each derived element
  too, computed from that div's **effective** restriction and statement. So does every **file** whose
  effective inputs come partly from its logical range. Every write recomputes every derived value in
  the document, so changing a folder's restriction changes the tier of everything beneath it.
- A derived element whose source has gone is removed from any dmdSec the platform itself minted. A
  dmdSec the platform did **not** mint (a Goobi export's) that carries a derived element and no
  chosen ones is left exactly as found. One exception, which predates this feature: saving a
  **logical structMap** rebuilds every logical div's dmdSec from the client's copy, so a Goobi tier
  on a logical div does not survive a logical-structure edit in the UI (open on #433).
- Only the derived types are rewritten. Other typed `accessCondition` elements are left as found.
- The dialogue has a **Preview METS and IIIF** button: the `mods:accessCondition` elements the node
  will carry after saving, and the `rights` and `requiredStatement` a IIIF manifest built from this
  profile would carry. Nothing in the preview is saved; the IIIF text is never written to METS.
- The tree shows the statement and the tier beside each node; hovering the statement shows its IIIF
  text.

The code follows the same split. The standard model is `AccessConditionProfile`, the restriction
and rights setters in `MetsManager`, `deposit.js` and the dialogue in `Deposit.cshtml`. The extension
is `StatementProfile` (with its validation), `DerivedAccessConditionWriter` (the whole-document pass
`MetsManager.WriteMets` calls), `IDerivedAccessConditionResolver`, the `_AccessStatementFields`
partial and `access-statements.js`, which the page loads only when the profile has statements.

## For iiif-builder

A manifest builder that follows the profile does, for each node it publishes:

1. Find the node's **effective** statement in METS: its own `mods:accessCondition` of type
   `statements.type`, else the nearest ancestor's, falling back to the file's logical range the way
   [effective-metadata-inheritance.md](../effective-metadata-inheritance.md) describes. (The
   platform's own METS parser computes this as `effectiveOtherAccessConditions`; a builder that
   reads METS itself has to do the same walk.)
2. Find that `value` in `statements.values`, then its `iiif` entry in `statements.iiif.entries`.
3. Publish the entry's `rights` (if any) as the manifest's `rights`, and its `requiredStatement` (if
   any) as `requiredStatement`, labelled with the entry's `requiredStatementLabel` or else
   `statements.iiif.requiredStatementLabel`.
4. No statement, or a statement the profile no longer knows: publish neither.

The derived tier is written in METS and needs no lookup, only the same inheritance walk. The UI's
preview implements steps 2-4 on the effective statement, so what an archivist sees there is what
such a builder produces.

## Worked example: Leeds

The Leeds document is `Preservation.API/AccessConditionProfiles/leeds.json`; the development
`appsettings.json` points at it. It was generated from Leeds' spreadsheet *Access Control -
MASTER.xlsx*:

| From the spreadsheet | Becomes |
|---|---|
| *DATA FOR DIGIRATI*, column C (the combined dropdown labels) | statement `label`s |
| *SPRIGS LIST - NEW*: MATRIX column | statement `category` |
| *SPRIGS LIST - NEW*: Condition (the public text) | `iiif` entries' `requiredStatement` (identical texts merged into one entry) |
| *SPRIGS LIST - NEW*: Notes | statement `description` |
| *SPRIGS LIST - NEW*: Right number / irn, where they exist | `externalIds` |
| *Access Control Matrix - MASTER* (v1.0, October 2026) | the categories and the tier matrix |
| *Access Control Logic - MASTER*: tier SPRIGs and IRNs | the tier values' `externalIds` |

Reading the matrix:

| Effective restriction | Effective statement's category | Tier |
|---|---|---|
| `PA1` | `public-domain` | 0 |
| `PA1` | `low-risk` | 1 |
| `PA1` | `medium-risk`, `high-risk` or `unassessed` | 2 |
| `PA1` | `holder-conditions` | 3 |
| `PA1` | no statement (nothing set, nothing inherited) | 2 - treated as unassessed |
| `PA1` | a statement the profile does not know | 3 (the `*` cell) |
| `PA2` or `PA3` | anything | 3 |
| `PA4` | anything | 4 |
| `PA-unassessed`, or none | anything | 5 |
| a value outside the vocabulary | anything | 5 (the `*` row) |

`AccessConditionProfileTests` checks every statement against every restriction, reading the deployed
document itself, against an expected table restated from the spreadsheet.

**Open, pending Leeds** (tracked on [issue #433](https://github.com/digirati-co-uk/digital-preservation/issues/433)):
the statement keys are ours (most statements have no SPRIG yet); two statements have placeholder
text (contractual restrictions, preservation copies only); only out of copyright has a IIIF `rights`
URI; the contractual statement is placed under high risk as its label says, though the matrix lists
in-contract material under medium (the same tier under PA1); *Copyright - in - high risk (no
copying)* is in the SPRIGS list but not the dropdown; the `requiredStatement` label (`Rights`) and
the type names (`copyright statement`, `tier`) are our choices.

## Changing a profile that is already in use

- **Adding or relabelling** restriction values, statements, categories or IIIF text is safe.
  Labels and text are presentation only; a value that no longer exists still reads back and still
  appears in the UI (as itself), it just cannot be chosen afresh - and the `*` row and column say
  what it derives.
- **Moving a statement to another category, or changing a matrix,** does not rewrite preserved METS.
  Each deposit's derived values are recomputed the next time *that* METS is written, so a change
  reaches existing content only when it is next edited and preserved. Plan a re-save campaign if the
  change must be universal. A IIIF text change, by contrast, needs nothing: it is looked up when a
  manifest is built.
- **Changing a statement's `value`** orphans every use of the old key. Don't; add a new statement
  and retire the old one instead.
- **Changing `statements.type`** orphans every statement already written under the old type: it
  becomes an unknown typed condition, reported in `otherAccessConditions` and inherited, but no
  longer an input. Treat it like a vocabulary migration.
- **Removing a derived type** leaves the old elements in place in every METS, as unknown typed
  conditions, never rewritten.
- **Moving back to the standard list** keeps every statement and tier element already written, and
  the UI stops showing them.
