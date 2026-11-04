# Access condition profile

The `AccessConditionProfile` section of the **Preservation API's** configuration decides what a person
can say about the access to a file, folder or logical range, and what the platform writes into METS as a
result. It is the only access-related configuration in the stack.

This page is for whoever deploys the platform. What a caller or a user sees is on the documentation
site: the API shape at [Vocabularies](https://digirati-co-uk.github.io/digital-preservation-docs/preservation-api/vocabularies/),
the METS at [Descriptive metadata and structure](https://digirati-co-uk.github.io/digital-preservation-docs/mets/mets-we-write-descriptive/),
and the optional extension at [Derived access conditions](https://digirati-co-uk.github.io/digital-preservation-docs/mets/derived-access-conditions/).

## Which services read it

| Service | Reads the section? |
|---|---|
| Preservation API | **Yes.** Binds it at startup, validates it (a bad profile stops the service, with the reasons in the log), serves it at `GET /access-conditions`, and uses it when writing METS. |
| Preservation UI | No. It fetches the API's profile and caches it for five minutes, so a change needs only the API redeployed. If the API cannot be reached it uses the last profile this process fetched; if there has never been one, a METS write is **refused** (503, nothing written) rather than saved with derived values that no longer match the chosen ones. |
| Pipeline API, Deposit Archiver | No. Both write METS through the same `MetsManager`, but with no profile, which means they derive nothing and leave any derived elements they find untouched. |
| Storage API, Storage API Importer | No. They never look inside METS. |

So: one section, in one service's `appsettings.json` (or the equivalent environment variables, with
`AccessConditionProfile__RestrictionAxis__Values__0__Value` style keys). `Preservation.API/appsettings.Example.json`
shows the standard form below.

## The standard case: access restrictions and rights statements

Out of the box the platform records two things about a resource, each as a `mods:accessCondition` in
the METS:

- an **access restriction** - one value from a vocabulary you define (`type="restriction on access"`);
- a **rights statement** - one URI from the fixed [rightsstatements.org](https://rightsstatements.org/)
  and Creative Commons vocabularies (`type="use and reproduction"`).

Both are inherited down the folder tree, and the rights statement can be explicitly suppressed; the
rules are in [effective-metadata-inheritance.md](../effective-metadata-inheritance.md). Nothing is
computed from them.

All you configure for this is the restriction vocabulary:

```json
"AccessConditionProfile": {
  "RestrictionAxis": {
    "Values": [
      { "Value": "Open",       "Label": "Open" },
      { "Value": "Restricted", "Label": "Restricted" },
      { "Value": "Closed",     "Label": "Closed" }
    ]
  }
}
```

`Value` is what is written to METS and returned in `accessRestrictions`; `Label` is what the UI shows.
The rights-statement vocabulary needs no configuration.

Three optional settings refine the standard case:

| Setting | Default | Effect |
|---|---|---|
| `RestrictionAxis.Label` | `Access Restriction(s)` | The caption of the restriction choice in the UI's metadata dialogue and tree. |
| `RightsAxis.Label` | `Rights statement` | The caption of the rights choice. |
| `AllowMultipleRestrictions` | `false` | `true` lets a resource carry several restriction values at once (the UI shows a multi-select; the METS gets one element per value). Not available together with `Derived`, below. |

**With no section at all** the service still starts: `GET /access-conditions` returns a profile with an
empty vocabulary, the UI's restriction dropdown offers only *(inherit from parent)*, and rights statements
work as normal. Nothing in the platform requires any particular restriction value to exist.

## Derived access conditions

An installation may need a *third* value that is a function of the two chosen ones - Leeds derives an
Access Tier that its delivery systems map to IIIF authorisation roles. Rather than hard-code that, the
profile can declare any number of **derived** access conditions. Each is written to METS as a further
`mods:accessCondition` with a `type` of your choosing, computed by the Preservation API on every write
of a METS file and never accepted as input.

A derived value is a cell in a matrix:

- the **rows** are the restriction values (plus `__none__`, meaning "no effective restriction");
- the **columns** are **rights groups** - you partition the rights-statement vocabulary into named
  groups, because a matrix with one column per rights URI would be unmanageable;
- the **cell** is the derived value.

This needs two more parts of the profile:

| Part | What it holds |
|---|---|
| `RightsAxis.Groups` | A list of `{ Key, Label, Members }`. `Members` names rights statements by their short label (`InC`, `NoC-US`, `CC-BY`...). Two reserved members: `__none__` claims resources with no effective rights statement, and `*` claims any statement no other group names. When groups exist the UI offers only the grouped statements, under their group labels. |
| `Derived` | A list of `{ Type, Label, Values, Matrix }`. `Type` is the METS `type` attribute. `Values` are the possible results, in order from least to most restrictive (used to pick a single value for legacy content that carries several restrictions). `Matrix` maps restriction value → group key → derived value; a row key or column key of `*` matches anything a more specific key does not. |

Validation at startup rejects: a matrix row that is not a restriction value, `__none__` or `*`; a column
that is not a group key or `*`; a cell that is not one of the `Values`; a **gap** - any restriction value
(or `__none__`) that resolves to nothing for any group, which a `*` row or column fills; a group member
that is not a known rights short label or a reserved key, or that appears in two groups; a group with no
members; duplicate restriction values, group keys or derived types; a derived `Type` of
`restriction on access` or `use and reproduction`; an empty `Values` or `Matrix`; and
`AllowMultipleRestrictions` set together with `Derived`. The error lists every problem it found.

What then happens, in brief (the detail is on the documentation site and in
[effective-metadata-inheritance.md](../effective-metadata-inheritance.md)):

- Every div, physical or logical, that carries an explicit restriction or rights element gets each
  derived element too, computed from that div's **effective** inputs.
- So does every **file** whose effective inputs come partly from its logical range: the parser falls
  back to the range for an axis the folders are silent on (a folder sets only a rights statement, the
  range sets the restriction), and such a file could not inherit the right value from its folder.
- Every write recomputes every derived value in the document, so changing a folder's restriction
  changes the tier of everything beneath it that carries one, and a derived element whose source has
  gone is removed - from any dmdSec the platform itself minted. A dmdSec the platform did **not** mint
  (a Goobi export's, say) that carries a derived element and no chosen ones is left exactly as found:
  Leeds intend Goobi to hand over only the resulting tier, and the platform must not strip it.
- Only the types currently in `Derived` are touched. Other typed `accessCondition` elements - a third
  party's, or a type you used to derive - are left as found.
- Nothing is written for a node with no effective input at all (nothing chosen on or above it); the
  `__none__` row is for a node with a rights statement but no restriction.
- The UI shows the derived value in the tree and recomputes it live in the metadata dialogue.

## Worked example: the Leeds Access Tier

Leeds chooses a **Protected Access Level** (the restriction) and a **Copyright Statement** (the rights
statement), and derives an **Access Tier** 0-5 from their agreed matrix. The configuration, as carried in
`Preservation.API/appsettings.json` for the development environment:

```json
"AccessConditionProfile": {
  "AllowMultipleRestrictions": false,
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
  "RightsAxis": {
    "Label": "Copyright Statement",
    "Groups": [
      { "Key": "public-domain", "Label": "Out of copyright (public domain / CC0)",
        "Members": [ "NoC-US", "NoC-NC", "NoC-OKLR", "NoC-CR", "NKC", "CC0", "CC-BY", "CC-BY-SA" ] },
      { "Key": "low-risk", "Label": "In copyright: assessed, low risk / permission given",
        "Members": [ "InC-EDU", "InC-NC", "CC-BY-NC", "CC-BY-ND", "CC-BY-NC-SA", "CC-BY-NC-ND" ] },
      { "Key": "restricted", "Label": "In copyright (restrictions apply)",
        "Members": [ "InC", "InC-OW-EU", "InC-RUU" ] },
      { "Key": "unassessed", "Label": "Copyright unassessed",
        "Members": [ "CNE", "UND", "__none__", "*" ] }
    ]
  },
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
        "PA1":           { "public-domain": "0", "low-risk": "1", "restricted": "2", "unassessed": "2" },
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

| Effective restriction | Effective rights statement | Group | Tier |
|---|---|---|---|
| `PA1` | `NoC-US` | public-domain | 0 |
| `PA1` | `InC-EDU` | low-risk | 1 |
| `PA1` | `InC` | restricted | 2 |
| `PA1` | none (nothing set, nothing inherited) | unassessed, via `__none__` | 2 |
| `PA2` | anything | - (`*`) | 3 |
| `PA4` | anything | - | 4 |
| none | anything | `__none__` row | 5 |
| `dlip-open` (a value outside the vocabulary) | anything | `*` row | 5 |

Configuration keys are case-insensitive, so this block is also, key for key, what `GET /access-conditions`
returns (in camelCase). The documentation site's
[Derived access conditions](https://digirati-co-uk.github.io/digital-preservation-docs/mets/derived-access-conditions/)
page shows the same example from the caller's side.

**Assumptions in this example, pending confirmation from Leeds** (tracked on
[issue #433](https://github.com/digirati-co-uk/digital-preservation/issues/433), where the grouping is
laid out statement by statement): the `tier` type name, the `PA1`..`PA-unassessed` restriction values,
and the group each rights statement sits in - nothing in the wiki thread allocates statements to
columns, so every placing here is inferred. In particular the master matrix has six copyright columns
and gives PA 1 x *High Risk* = 3; a rights statement URI carries no risk assessment, so the grouping
cannot express that, and `InC` currently yields 2.

## Changing a profile that is already in use

- **Adding or relabelling** restriction values or groups is safe. Labels are presentation only; a value
  that no longer exists in the vocabulary still reads back and still appears in the UI, it just cannot be
  chosen afresh - and the `*` row says what such a value derives (for Leeds, 5). Without one, validation
  would pass but the value would silently derive nothing.
- **Changing a matrix** does not rewrite preserved METS. Each deposit's derived values are recomputed the
  next time *that* METS is written, so a corrected matrix reaches existing content only when it is next
  edited and preserved. Plan a re-save campaign if the change must be universal.
- **Removing a derived type** leaves the old elements in place in every METS, as unknown typed
  conditions; they are reported in `derivedAccessConditions` and inherited like any other, but never
  rewritten.
- **Switching `AllowMultipleRestrictions` off** on content that carries several values: nothing is
  rewritten until someone saves the resource from the UI, whose single-select can hold only one of them,
  so that save reduces the resource to the one value chosen. If `Derived` is configured at the same time,
  the most restrictive of the values, by `Values` order, is used for the lookup until then.
