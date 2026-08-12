# Issue #188 companion: safe IDREFS resolution (the `string.Join(' ', …)` problem)

> **Status (2026-08-11):** IMPLEMENTED on `feat/188-idrefs-resolution` (stacked on PR #211;
> its PR opens once #211 merges). One deviation from §"Sequencing and tests" below: DeleteDiv
> is *tolerant* of unresolvable amdSec/dmdSec references rather than failing cleanly —
> deletion is how broken content gets cleaned up, and this also fixes a latent crash on
> by-design dangling DMDIDs (lazy dmdSec creation).
>
> **Status (2026-08-12, after PR #213 review):** the review found the deletion sites needed
> more than `ResolveSingle`, so — revising §"What NOT to do" point 3 — `IdRefs.ResolveAll`
> now exists for them: `DeleteDiv` and `RemoveLogicalStructMapDmdSecs` (the one site the
> original inventory missed) remove **every** section a reference resolves, EXCEPT a section
> another div/file still references (`SectionReferencedElsewhere`) — a genuinely shared
> section (e.g. Archivematica's shared rightsMD) must survive the deletion of one referrer.
> `TryRemoveRedundantDmd` now drops only the reference to the dmdSec it removed
> (`IdRefs.RemoveReference`), not the div's whole DMDID list. And the raw-string overload
> splits on all XML whitespace (tab/newline/CR), not just spaces.

> 2026-08-11. Companion to `issue-188-plan.md` (v2). Analysed against
> `feat/188-physical-divs-cache` (PR #211), which this work should follow.

## The problem

`ADMID` and `DMDID` on `mets:div` / `mets:file` are schema-typed **IDREFS** — a
whitespace-separated list of ID references. That single fact plays out oppositely in our two
METS-reading stacks:

|  | Legacy ID containing spaces (`ADM_objects/my file.pdf`) | Genuine multi-reference (`ADMID="ADM_A ADM_B"`) |
|---|---|---|
| **XmlGen typed model** (XmlSerializer splits IDREFS into `Collection<string>`) | **Broken by tokenisation**: one intended ID arrives as `["ADM_objects/my", "file.pdf"]`. The `string.Join(' ', …)` workaround reconstructs it. | Would work naturally (each token is a complete ID) — **but the join workaround breaks it**: `"ADM_A ADM_B"` matches no element ID. |
| **XDocument** (`MetsParser`) and Python iiif-builder (raw attribute strings) | Works naturally: the reference attribute and the target `ID` attribute are the same raw string, so equality lookups just match. | **Broken**: the whole-string lookup `AmdSecMap.TryGetValue("ADM_A ADM_B")` finds nothing; no split fallback exists. |

The target `ID` attribute itself (`xs:ID` on `amdSec`/`dmdSec`/`techMD`) is a *single* value in
both stacks, so `amdSec.Id` always carries the full string, spaces included. That is why the
join reconstructs correctly on the XmlGen side.

Our own writer only ever emits **one** reference per attribute, so genuine multi-IDREFS never
occurs in platform-written METS — today. Two things change that calculus: third-party METS
(Goobi, Archivematica) can carry real IDREFS lists, and the conformance-based-editability
direction means MetsManager code paths may eventually see them too. Robust resolution should
not depend on knowing which case it is looking at.

**Step 2 interplay**: `XmlConvert.EncodeLocalName` encodes a space as `_x0020_`, so no ID
minted after step 2 can contain a space. Post-step-2, every new reference tokenises to exactly
one collection member and the joined form degenerates to `tokens[0]`. The tiered resolution
below is therefore purely a bridge for legacy and third-party content — removable only after a
step 3 bulk migration (and never removable for third-party parsing).

## Site inventory (branch `feat/188-physical-divs-cache`)

### XmlGen side — join-based reconstruction

| Site | Pattern | Hazard beyond the split problem |
|---|---|---|
| `MetadataManager.cs:198` (`GetMetadataXml`) | `ctx.FileAdmId = string.Join(' ', ctx.File.Admid)` → `AmdSec.Single(a => a.Id == …)` | `Single` **throws** on no match (genuine IDREFS) → unhandled 500. `ctx.FileAdmId` is also reused to mint/find the `digiprovMD_ClamAV_` ID. |
| `ModsManager.cs:171` (`GetModsForDiv`) | `var normalised = string.Join(' ', div.Dmdid)` → `GetModsForDmdId` | Lookup miss for genuine multi-DMDID. |
| `ModsManager.cs:178` (`SetModsForDiv`) | same | same |
| `MetsManager.cs:358, 364` (`DeleteDiv`, admId) | `Count > 1 ? Join : file.Admid[0]` → `AmdSec.Single` | `[0]` **throws IndexOutOfRange** when the collection is empty; `Single` throws on no match. |
| `MetsManager.cs:373` (`DeleteDiv`, dmdId) | `Count > 1 ? Join : div.Dmdid[0]` → `DmdSec.Single` | same |
| `MetsCache.cs:112` (new in PR #211) | `var admId = string.Join(' ', child.Admid)` → `AmdSec.FirstOrDefault` | No throw, but single-tier: a genuine multi-ADMID directory resolves no path and drops out of the cache (tier-2/3 navigation fallbacks then apply). |

### XDocument side (`MetsParser`) — whole-string lookups with no split fallback

| Site | Lookup |
|---|---|
| `:326–330` | directory div `ADMID` → `AmdSecMap` (feeds `premis:originalName` → directory paths) |
| `:395–427` | file `ADMID` (from div — Goobi — or file element — EPrints/Archivematica) → `TechMdMap`, falling back to `AmdSecMap` |
| `:501` | `clamavKey = VirusProvEventPrefix + admId` — a **derived key** built from the raw reference attribute |
| `:560` | `ADMID` → `AmdSecMap` (second pass) |
| `:714, 1093` | `DMDID` → `DmdSecMap` |

`FILEID` (fptr/area) and `smLink` from/to are single-IDREF typed, so raw-string equality is
inherently consistent on this side; no change needed there.

### Python iiif-builder

`mets_parser.py` mirrors the XDocument situation (`amd_map.get(adm_id)` etc. on raw attribute
strings): legacy space IDs work, genuine IDREFS lists don't. Same fallback applies; separate
change, lower priority (Goobi METS reaching iiif-builder do not use multi-IDREFS today).

## Proposed design

### 1. One resolution helper, tiered, on the XmlGen side

New `DigitalPreservation.Mets/IdRefs.cs`:

```csharp
public static class IdRefs
{
    /// <summary>The single intended ID a legacy space-containing ID was split from.</summary>
    public static string Joined(IList<string> tokens) => string.Join(' ', tokens);

    /// <summary>
    /// Resolve an IDREFS token collection to one referenced element:
    /// 1. the joined form — a legacy single ID that contained spaces (schema-invalid, but
    ///    what the platform wrote before issue #188; unambiguous because no schema-valid
    ///    ID can contain a space);
    /// 2. otherwise each token individually — schema-valid IDREFS; first match wins.
    /// Returns null (never throws) when nothing matches or the collection is empty.
    /// </summary>
    public static T? ResolveSingle<T>(IList<string> tokens, Func<string, T?> lookupById)
        where T : class
    {
        if (tokens.Count == 0) return null;
        if (tokens.Count == 1) return lookupById(tokens[0]);
        return lookupById(Joined(tokens))
               ?? tokens.Select(lookupById).FirstOrDefault(m => m != null);
    }
}
```

The tier order is safe: in a schema-valid document no ID contains a space, so the joined form
of a genuine multi-token list can never collide with a real ID — if the joined lookup matches,
the document is legacy-form and that match is the right one.

All six XmlGen-side sites move onto this (with `mets.AmdSec` / `mets.DmdSec` lambdas), which
simultaneously:
- keeps legacy space-ID METS working (tier 1 = today's join);
- makes genuine multi-IDREFS resolve instead of failing (tier 2 — new capability);
- eliminates the `Single()` throws and the `[0]` empty-collection crash (null → `Result.Fail`).

### 2. Derive dependent keys from the resolved element, not the reference

`ctx.FileAdmId` (and the DeleteDiv admId/dmdId strings) should be set from **the resolved
element's own `Id`** rather than the re-joined tokens. Identical string in every legacy case;
correct-by-construction in the multi-IDREFS case; and the `digiprovMD_ClamAV_{admId}` derived
ID then always embeds a real amdSec ID. Same principle at `MetsParser.cs:501`: build
`clamavKey` from the resolved techMD/amdSec's actual `ID` attribute, not the raw `ADMID` value.

### 3. Whole-then-split fallback on the XDocument side

Mirror-image helper for `MetsParser` (this is the old pre-#188 issue text, still valid):

```csharp
static XElement? ResolveIdRef(string attrValue, IReadOnlyDictionary<string, XElement> map)
{
    if (map.TryGetValue(attrValue, out var whole)) return whole;      // single ID or legacy space-ID
    if (!attrValue.Contains(' ')) return null;
    return attrValue.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(t => map.GetValueOrDefault(t))
        .FirstOrDefault(m => m != null);                              // genuine IDREFS list
}
```

Apply at the five `MetsParser` sites listed above (keeping the existing techMD→amdSec fallback
ordering intact).

### 4. What NOT to do

- Do not "fix" the joins by escaping spaces at write time in step 1/2 code paths other than
  via `ToMetsId()` — step 2 already guarantees no new spaced IDs; anything else double-encodes.
- Do not remove the joined tier until a step 3 migration has eliminated every legacy spaced ID
  (this is the same lifecycle as the frozen `Samples/path-fixture-spaces.xml` corpus).
- Do not try to make `ResolveSingle` return multiple elements for multi-IDREFS in step 1 of
  this work — no current caller can consume more than one; add `ResolveAll` only when a caller
  needs it. *(Superseded 2026-08-12: the deletion sites are exactly such callers — see the
  status note above.)*

## Sequencing and tests

Branch `feat/188-idrefs-resolution`, off `feat/188-physical-divs-cache` (PR #211) — it touches
the same files and its tests lean on the cache tests' fixtures. Merge after #211, before (or
folded into) step 2.

Tests:
- `IdRefs.ResolveSingle`: empty; single valid; legacy spaced (joined matches); genuine
  two-token (each resolves, first wins); two-token where only second matches; no match → null.
- Legacy fixture regression: `Samples/path-fixture-spaces.xml` still fully editable
  (already covered by `PhysicalDivsCacheTests`; keep green).
- New fixture: a METS with a genuine two-ADMID div and two-DMDID div — resolves via tier 2 on
  the XmlGen side and via split fallback in `MetsParser`; `DeleteDiv` on a div with an empty
  `Admid` collection fails cleanly instead of throwing.
- Parser: ADMID list of two real IDs resolves techMD; `clamavKey` built from resolved ID.

## Docs impact

`02c-METS-Parsing.md`: the "Finding each file's technical metadata" section gains a sentence on
IDREFS handling (whole value first, then per-token). `02b` needs no change — the platform still
writes exactly one reference per attribute.
