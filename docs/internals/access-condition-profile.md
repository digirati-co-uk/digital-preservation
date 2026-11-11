# Access conditions configuration

What a person can say about the access to a file, folder or logical range, and what the platform
writes into METS as a result, is configuration of the **Preservation API**. It is the only
access-related configuration in the stack, and it comes in two forms. Almost every installation
wants the first.

This page is for whoever deploys the platform. What a caller or a user sees is on the documentation
site: the API shape at [Vocabularies](https://digirati-co-uk.github.io/digital-preservation-docs/preservation-api/vocabularies/),
the METS at [Descriptive metadata and structure](https://digirati-co-uk.github.io/digital-preservation-docs/mets/mets-we-write-descriptive/),
and the derived form at [Derived access conditions](https://digirati-co-uk.github.io/digital-preservation-docs/mets/derived-access-conditions/).

## Which services read it

| Service | Reads the configuration? |
|---|---|
| Preservation API | **Yes.** Builds its profile from whichever form is present at startup, validates it (a bad profile stops the service, with the reasons in the log), serves it at `GET /access-conditions`, and uses it when writing METS. |
| Preservation UI | No. It fetches the API's profile and caches it for five minutes, so a change needs only the API redeployed. If the API cannot be reached it uses the last profile this process fetched; if there has never been one, a METS write is **refused** (503, nothing written) rather than saved with derived values that no longer match the chosen ones. |
| Pipeline API, Deposit Archiver | No. Both write METS through the same `MetsManager`, but with no profile, which means they derive nothing and leave any derived elements they find untouched. |
| Storage API, Storage API Importer | No. They never look inside METS. |

So: one service's `appsettings.json` (or the equivalent environment variables, with
`AccessConditions__0__Value` style keys). `Preservation.API/appsettings.Example.json` shows the
standard form.

## The standard form: `AccessConditions`

Out of the box the platform records two things about a resource, each as a `mods:accessCondition`
in the METS, each chosen by a person and inherited down the folder tree:

- an **access restriction** - one value from a vocabulary you define (`type="restriction on access"`);
- a **rights statement** - one URI from the fixed [rightsstatements.org](https://rightsstatements.org/)
  and Creative Commons vocabularies (`type="use and reproduction"`).

Nothing is computed from them. All you configure is the restriction vocabulary, as a plain list:

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
and rights statements work as normal. Nothing in the platform requires any particular restriction
value to exist.

That is the whole of the standard form. The rest of this page is for an installation that needs a
value **derived** from its choices; if that is not you, stop here.

## The two-axis form: `AccessConditionProfile`

Leeds needs an **Access Tier** (0-5) on every node, which their delivery systems map to IIIF
authorisation roles. The tier is a function of two things: the **Protected Access Level** (the
restriction) and the **copyright status** - an internal risk assessment (public domain, low risk,
high risk...) that Leeds also record in EMu and EPrints. The rights statement is *not* one of the
inputs: it is the public claim about what people may do, and the same statement (`InC`) can sit at
low, medium or high risk. So the two-axis form adds a third chosen value and derives from the
first two by a matrix.

Replace the `AccessConditions` list with an `AccessConditionProfile` section (never both; the
service refuses to start):

| Part | What it holds |
|---|---|
| `RestrictionAxis` | `Label` and `Values`, exactly as the standard list, plus a caption. |
| `CategoryAxis` | The second chosen axis. `Label` (the caption), `Type` (the `mods:accessCondition/@type` it is written under, e.g. `copyright status`), and `Values`, each with `Value`, `Label` and `RightsStatements`: the rights statements, by short label (`InC`, `NoC-US`, `CC-BY`...), that make sense under that category. A statement may appear under several. The UI offers those first once the category is chosen and preselects the category when a chosen statement is permitted under exactly one; nothing is enforced on save. |
| `RightsAxis` | `Label` only: the caption of the rights-statement choice. |
| `Derived` | One entry per derived condition: `Type` (the METS `@type`, e.g. `tier`), `Label`, `Values` in order from least to most restrictive, and `Matrix`: restriction value → category value → derived value. Row and column keys may also be `__none__` ("no effective value on or above this node") and `*` ("anything a more specific key does not match"). |
| `AllowMultipleRestrictions` | Must be false (the default): a derived value is a single matrix cell. |

Validation at startup rejects: a `CategoryAxis` without `Derived` (use the standard form) or
`Derived` without a `CategoryAxis`; a `CategoryAxis` with no `Type`, or a type that is one of the
two chosen types or a derived type; an unknown rights short label under a category, or one listed
twice there; a matrix row that is not a restriction value, `__none__` or `*`; a column that is not
a category value, `__none__` or `*`; a cell that is not one of the `Values`; a **gap** - any
restriction value (or `__none__`) that resolves to nothing for any category (or `__none__`), which a
`*` row or column fills - note the check covers configured values only, so a cell or column of `*`
is what catches a value outside the vocabulary; duplicate restriction values, category values or derived types; an empty
`Values` or `Matrix`; and `AllowMultipleRestrictions` set. The error lists every problem it found.

What then happens, in brief (the detail is on the documentation site and in
[effective-metadata-inheritance.md](../effective-metadata-inheritance.md)):

- The category is a third chosen `mods:accessCondition`, under `CategoryAxis.Type`, inherited down
  the tree exactly like the restriction.
- Every div, physical or logical, that carries a chosen element of its own gets each derived element
  too, computed from that div's **effective** restriction and category. So does every **file** whose
  effective inputs come partly from its logical range: the parser falls back to the range for an
  axis the folders are silent on, and such a file could not inherit the right value from its folder.
- Every write recomputes every derived value in the document, so changing a folder's restriction
  changes the tier of everything beneath it that carries one, and a derived element whose source has
  gone is removed - from any dmdSec the platform itself minted. A dmdSec the platform did **not**
  mint (a Goobi export's, say) that carries a derived element and no chosen ones is left exactly as
  found: Leeds intend Goobi to hand over only the resulting tier, and the platform must not strip it.
  One exception, which predates this feature: saving a **logical structMap** rebuilds every logical
  div's dmdSec from the client's copy (title, record identifiers, restriction, rights, category), so
  a logical div's other typed conditions, a Goobi tier among them, do not survive a logical-structure
  edit in the UI. Whether that matters depends on whether Goobi puts the tier on logical divs and
  whether Goobi deposits' logical structure is ever edited here - both open on #433.
- Only the types in `Derived` are touched. Other typed `accessCondition` elements - a third party's,
  or a type you used to derive - are left as found.
- Nothing is written for a node with no effective input at all; the `__none__` row and column are
  for a node that has one input but not the other.
- The UI shows the category and the derived value in the tree and recomputes the derived value
  live in the metadata dialogue. It warns, and no more than warns, when the chosen rights statement
  is not one the chosen category lists.

## Worked example: the Leeds Access Tier

Leeds choose a **Protected Access Level**, a **copyright status** and a **Rights Statement**
(the rights statement), and derive the **Access Tier** from the first two using their master
matrix. The configuration, as carried in `Preservation.API/appsettings.json` for the development
environment:

```json
"AccessConditionProfile": {
  "RestrictionAxis": {
    "Label": "Protected Access Level",
    "Values": [
      { "Value": "PA1", "Label": "Level 1" },
      { "Value": "PA2", "Label": "Level 2" },
      { "Value": "PA3", "Label": "Level 3" },
      { "Value": "PA4", "Label": "Level 4" },
      { "Value": "PA-unassessed", "Label": "Unassessed" }
    ]
  },
  "CategoryAxis": {
    "Label": "Copyright status",
    "Type": "copyright status",
    "Values": [
      { "Value": "public-domain",      "Label": "Out of Copyright (Public Domain / CC0)",
        "RightsStatements": [ "NoC-US", "NoC-NC", "NoC-OKLR", "NoC-CR", "NKC", "CC0" ] },
      { "Value": "low-risk",           "Label": "In Copyright: Assessed (Low Risk)",
        "RightsStatements": [ "InC", "InC-EDU", "InC-NC", "InC-RUU", "InC-OW-EU", "CC-BY", "CC-BY-SA", "CC-BY-ND", "CC-BY-NC", "CC-BY-NC-SA", "CC-BY-NC-ND" ] },
      { "Value": "medium-risk",        "Label": "In Copyright: Assessed (Medium Risk)",
        "RightsStatements": [ "InC", "InC-OW-EU", "InC-RUU" ] },
      { "Value": "high-risk",          "Label": "In Copyright: Assessed (High Risk)",
        "RightsStatements": [ "InC", "InC-OW-EU", "InC-RUU" ] },
      { "Value": "unassessed",         "Label": "In Copyright: Unassessed",
        "RightsStatements": [ "CNE", "UND" ] },
      { "Value": "holder-conditions",  "Label": "In Copyright: Holder Conditions (High Risk)",
        "RightsStatements": [ "InC", "InC-OW-EU", "InC-RUU" ] }
    ]
  },
  "RightsAxis": { "Label": "Rights Statement" },
  "Derived": [
    {
      "Type": "tier",
      "Label": "Access Tier",
      "Values": [
        { "Value": "0", "Label": "Tier 0 - Open" },
        { "Value": "1", "Label": "Tier 1 - Open" },
        { "Value": "2", "Label": "Tier 2 - Restricted" },
        { "Value": "3", "Label": "Tier 3 - Protected" },
        { "Value": "4", "Label": "Tier 4 - Closed" },
        { "Value": "5", "Label": "Tier 5 - Unassessed" }
      ],
      "Matrix": {
        "PA1":           { "public-domain": "0", "low-risk": "1", "medium-risk": "2", "high-risk": "2", "unassessed": "2", "holder-conditions": "3", "__none__": "2", "*": "3" },
        "PA2":           { "*": "3" },
        "PA3":           { "*": "3" },
        "PA4":           { "*": "4" },
        "PA-unassessed": { "*": "5" },
        "__none__":      { "*": "5" },
        "*":             { "*": "5" }
      }
    }
  ]
}
```

Reading the matrix:

| Effective restriction | Effective copyright status | Tier |
|---|---|---|
| `PA1` | `public-domain` | 0 |
| `PA1` | `low-risk` | 1 |
| `PA1` | `medium-risk`, `high-risk` or `unassessed` | 2 |
| `PA1` | `holder-conditions` | 3 |
| `PA1` | none (nothing set, nothing inherited) | 2 |
| `PA1` | a value outside the vocabulary (mistyped, or renamed since) | 3 (the `*` cell) |
| `PA2` or `PA3` | anything | 3 |
| `PA4` | anything | 4 |
| `PA-unassessed`, or none | anything | 5 |
| `dlip-open` (a value outside the vocabulary) | anything | 5 (the `*` row) |

Configuration keys are case-insensitive, so this block is also, key for key, what `GET /access-conditions`
returns (in camelCase). The documentation site's
[Derived access conditions](https://digirati-co-uk.github.io/digital-preservation-docs/mets/derived-access-conditions/)
page shows the same example from the caller's side.

**Assumptions in this example, pending confirmation from Leeds** (tracked on
[issue #433](https://github.com/digirati-co-uk/digital-preservation/issues/433)): the `tier` and
`copyright status` type names, the `PA1`..`PA-unassessed` and category values, and the rights
statements permitted under each category. The matrix itself is Leeds' master matrix verbatim (Access Control Matrix, version 1.0, October 2026).

## Changing a profile that is already in use

- **Adding or relabelling** restriction values, categories or permitted statements is safe. Labels
  are presentation only; a value that no longer exists in the vocabulary still reads back and still
  appears in the UI, it just cannot be chosen afresh - and the `*` row says what such a value derives
  (for Leeds, 5). Without one, validation would fail on the gap.
- **Changing a matrix** does not rewrite preserved METS. Each deposit's derived values are recomputed
  the next time *that* METS is written, so a corrected matrix reaches existing content only when it
  is next edited and preserved. Plan a re-save campaign if the change must be universal.
- **Changing `CategoryAxis.Type`** orphans every category already written under the old type: it
  becomes an unknown typed condition, reported in `otherAccessConditions` and inherited, but no
  longer an input. Treat it like a vocabulary migration.
- **Removing a derived type** leaves the old elements in place in every METS, as unknown typed
  conditions; they are reported in `otherAccessConditions` and inherited like any other, but never
  rewritten.
- **Moving from the two-axis form back to the standard list** keeps every category and tier element
  already written, as above, and the UI stops showing them.
