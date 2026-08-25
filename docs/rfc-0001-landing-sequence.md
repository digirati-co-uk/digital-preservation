# RFC-0001 — landing sequence

How the work on caller identity gets from two open branches to production without losing anything,
and in what order. Companion to [`rfc-0001-api-caller-identity.md`](./rfc-0001-api-caller-identity.md)
(the design), [`rfc-0001-phase0-entra-admin.md`](./rfc-0001-phase0-entra-admin.md) (the admin side)
and [`rfc-0001-lpii166-comparison.md`](./rfc-0001-lpii166-comparison.md) (the POC comparison).

*Written 2026-08-25. Branch facts below were verified against `origin` on that date.*

## The two branches

| | `feature/multiple-deposit-buckets` (PR #208) | `feat/LPII-165/entra-api` (Frank, LPII-166) |
|---|---|---|
| Purpose | RFC-0001 Phase 0 code half **plus the bucket-routing feature** (Goobi's isolated deposit bucket, the RFC's first consumer) | POC of the audience mechanics against Digirati's own tenant |
| Files changed | 33 | 3: `Core/Web/Headers/AccessTokenProvider.cs`, `Preservation.API/Program.cs`, `Storage.API/Program.cs` |
| Overlap | the two `Program.cs` files only — #208 adds one `builder.Services.AddClientDirectory(builder.Configuration);` block to each | |
| State | open, MERGEABLE, ~30 commits behind `main` | last commit `8b249aa` (2026-08-19), ~46 behind `main`, not a PR |
| Known defects | none open | two — see comparison §6 (`nullCheck` reflection guard; `resource = ClientId` against the v1 endpoint) |

The overlap is two *adjacent-line* additions, not competing edits of the same lines. Git will merge
them cleanly in either order; the only way to lose code is a careless manual conflict resolution
(see the PR #177 note below).

## The sequence

### Step 1 — Merge PR #208 first

**Why first:** the bucket code is the largest, most valuable and *least* controversial piece — it does
not depend on any decision still open with Frank or with Leeds. Once it is on `main`, it cannot be
lost to a later conflict, and every subsequent branch (including Frank's) rebases onto it rather than
the reverse.

**Why it is safe before Phase 0's admin work:** #208 is *inert without configuration*.

- `KnownClients` is empty in every deployed config → `AuthFilterIdentifier` takes the header fallback
  on every request, exactly today's behaviour, plus one throttled warning line per unresolved `appId`.
- No code on the branch alters `JwtBearerOptions`; the accepted audience is whatever `AzureAd:Audience`
  already says. The `AudienceValidationTests` pin the config *shapes*; they do not change deployed config.
- Bucket routing needs a caller profile with a `depositBucket`; none exists → no request is routed
  anywhere new. The pipeline guard is a no-op for the same reason.
- No EF migrations, no CI/workflow or docker changes.

Observable differences after deploy are limited to the new `/whoami` endpoints and the warning log.

**Before merging:** rebase or merge `main` into the branch (it is ~30 commits behind) and let CI run;
confirm the deployed **dev** config (parameter store / task definitions, not just the repo
`appsettings`) carries no stray `KnownClients` or audience keys that would make "inert" untrue.
Refresh the PR description — it predates the RFC's revisions and the comparison doc.

**What this unblocks:** the Phase 0 admin request can go to Leeds any time (it is independent of
code), and Goobi's registration can be created the moment Leeds do 1.2 of the admin doc.

### Step 2 — Follow-up PR: adopt the fixed POC mechanics

A small PR, **opened after #208 is on `main`**, that brings across what is worth keeping from Frank's
branch *in corrected form*, rather than merging his branch as-is. Comparison §7.1 is the spec; in
summary:

1. **Guarded `PostConfigure<JwtBearerOptions>`** — hoist Frank's block out of the two `Program.cs`
   files into one `DigitalPreservation.Core` extension (e.g. `AddValidAudiencesOverride`), called from
   both APIs. It acts only when the new config section is present; the existing singular `Audience`
   remains the fallback, which is what makes deploy-then-activate possible.
2. **Settle the config location** — `Authentication:ValidAudiences` (Frank's shape) or
   `AzureAd:TokenValidationParameters:ValidAudiences` (the RFC's). Pick one, then **re-pin
   `Storage.API.Tests/Integration/AudienceValidationTests.cs`** to it in the same PR, and update both
   `appsettings.Example.json` files and RFC §6 Phase 0.
3. **`ResourceUri` on `AccessTokenProvider`** (RFC Phase 2 option (a)) with both defects fixed:
   - exempt `ResourceUri` (or all optional properties) from the reflection `nullCheck`, so deploying
     without the new key does not kill token acquisition;
   - send the *target* resource, not the caller's own `ClientId` — preferably by moving to the v2
     `/oauth2/v2.0/token` endpoint with `scope = {ResourceUri}/.default` (RFC Appendix B wants this
     anyway). Add a test that asserts the outgoing form body.

**Why a follow-up rather than merging Frank's branch:** his three files carry both the mechanics and
the two defects, and his branch predates #208. Cherry-picking the ideas into a branch cut from
post-#208 `main` avoids the conflict resolution entirely and lets the fixes land with their tests.
If Frank would rather rebase his own branch onto `main` and fix it there, that is equally fine — the
point is the *order*, not the author.

**What this depends on ("further developments"):** nothing that blocks step 1. It *is* shaped by
Frank's answers to the comparison's §8 questions — in particular whether his tests really exercised
the `ResourceUri` code path — and by confirming the config-key choice with him so the two branches
don't encode different shapes.

### Step 3 — Activation ladder (no code)

Only after steps 1–2 are deployed, and strictly in this order, because each rung is gated by the
previous one:

1. **Leeds admin does Phase 0** (admin doc Part 1: role, delegated scope, `idtyp` claim). Independent of
   code — can be requested now, in parallel with steps 1–2.
2. **Config flip: accept both audiences** (the `ValidAudiences` section from step 2, listing
   `api://a616cf42…` *and* `api://84c62880…`). Deploy-then-activate: the code from step 2 is already
   live and idle; adding the section activates it. Verify with `/whoami` and the audience tests' shapes.
3. **Phase 1 — Goobi's registration + role assignment**, its `KnownClients` profile with `depositBucket`.
   This is the point where Goobi is delivered; nothing user-visible waits on the later phases (which is
   exactly why RFC §6's completion commitment exists — name an owner and date for Phases 2–4 here).
4. **Phase 2–4** as RFC §6: repoint callers one at a time (assignment *before* repoint, or
   `AADSTS501051`), repoint the UI (delegated scope consent + user assignments first, or `AADSTS50105`),
   retire the transitional audience, switch the human/machine predicate to `idtyp`, remove the header.

## What would change the sequence

- **If the POC topology were adopted instead of the RFC's** (comparison §7.3 — a recorded contingency,
  not the recommendation): steps 1 and 2 are *unchanged*. The bucket code keys on the resolved caller
  identity via `IClientDirectory`, and the audience-override mechanics are topology-neutral. Only the
  activation ladder differs: per-caller App ID URIs in `ValidAudiences` and `Assignment required = Yes`
  on each caller registration, instead of one audience plus role assignments. That is the reason the
  sequence is safe to start now regardless of how the topology discussion with Frank ends.
- **If Frank's branch is merged before #208** (not recommended): #208 must then be rebased; the
  `Program.cs` conflicts are still trivial, but the `AccessTokenProvider` defects would be on `main`
  — with the `nullCheck` one able to break token acquisition on any deploy that lacks the new key.

## The PR #177 precedent

On PR #177's branch, a manual resolution of a `main`-into-branch merge conflict in
`Storage.Repository.Common/Storage.cs` silently dropped an unrelated block (the `metadata/ad-hoc`
scaffolding from #176); it was only caught by a later compatibility check. The equivalent hazard here is the two `Program.cs` files: whoever
resolves a conflict there must end with **both** the `AddClientDirectory` block *and* the audience
override present in each API. Check by grepping for both after resolution, not by eyeballing the diff.
