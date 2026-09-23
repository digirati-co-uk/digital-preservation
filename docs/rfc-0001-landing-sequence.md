# RFC-0001 — landing sequence

How the work on caller identity gets from two open branches to production without losing anything,
and in what order. Companion to [`rfc-0001-api-caller-identity.md`](./rfc-0001-api-caller-identity.md)
(the design), [`rfc-0001-phase0-entra-admin.md`](./rfc-0001-phase0-entra-admin.md) (the admin side)
and [`rfc-0001-lpii166-comparison.md`](./rfc-0001-lpii166-comparison.md) (the POC comparison).

*Written 2026-08-25. Branch facts below were verified against `origin` on that date.*

## Ladder status

**As of 2026-09-22 (the v1.3.0 cut).** The configuration *is* the authority — this table is a
reader's index to it, not a substitute: verify with the secret `jq` check and `terraform plan`
before acting, and **update this table as rungs are climbed**. "Committed" means merged to
`preservation-ops` `main`; "applied" means `terraform apply` has actually run against the
environment (there is no apply pipeline — it is a manual, local step).

| | dev | test | production |
|---|---|---|---|
| Phase 0 Entra admin (role, scope, `idtyp`) | **done 2026-09-23** (steps 1.1-1.5 completed by the Leeds administrator; `Assignment required` was already `Yes` and was deliberately left untouched per the admin doc; the §1.5 existing-assignments list was reported - four named human users plus the iiif-builder-dev and Playwright service accounts - and stays in place as the start of the Phase 3 user list) | **not yet requested** | **not yet requested** |
| Rung 1: four audience keys in the `preservation-api` `oauth_azure` secret | in place, verified 2026-09-21 | in place, verified 2026-09-22 | in place, verified 2026-09-22 |
| Rung 2: four-entry `ValidAudiences` terraform | committed (ops PR #72) **and applied** 2026-09-21 | committed (ops PR #75); **not yet applied** | committed (ops PR #76); **not yet applied** |
| Rung 3: `KnownClients` (Goobi, Phase 1) | not started | not started | not started |
| Rung 4: `TokenProvider__ResourceUri` repoints | not started | not started | not started |
| Deployed build | `main` (auto-deploys) | `v1.2.1` | `V1.0.1` (2025-10-24); `v1.3.0` is the cut for the next deploy |

Two facts a newcomer cannot otherwise infer: all three environments live in the **same Entra
tenant**, as separate per-environment registration pairs — so Phase 0's admin actions are
per-registration, and dev's completion does **not** cover test or production. The admin document
([`rfc-0001-phase0-entra-admin.md`](./rfc-0001-phase0-entra-admin.md)) is written against the dev
registrations; hand it to the administrator again for each environment with that environment's
registration names substituted.

## The two branches

| | `feature/multiple-deposit-buckets` (PR #208) | `feat/LPII-165/entra-api` (LPII-166) |
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
not depend on any decision still open on LPII-166 or with Leeds. Once it is on `main`, it cannot be
lost to a later conflict, and every subsequent branch (including the LPII-166 branch) rebases onto it rather than
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

A small PR, **opened after #208 is on `main`**, that brings across what is worth keeping from the LPII-166
branch *in corrected form*, rather than merging that branch as-is. Comparison §7.1 is the spec; in
summary:

1. ~~**Guarded `PostConfigure<JwtBearerOptions>`**~~ — **not needed** (established 2026-09-16):
   `AudienceValidationTests` proves `AzureAd:TokenValidationParameters:ValidAudiences` binds through
   the stock `AddMicrosoftIdentityWebApi(GetSection("AzureAd"))` path both APIs already use — both
   audiences accepted, foreign rejected — so the dual-audience mechanism ships on #208 as pure
   config with no code of its own. The LPII-166 `PostConfigure` block has nothing left to do.
2. ~~**Settle the config location**~~ — **already settled** on
   `AzureAd:TokenValidationParameters:ValidAudiences`: the tests are pinned to it, both
   `appsettings.Example.json` files document it, and RFC §6 Phase 0 prescribes it (with the
   top-level-`Audiences`-binds-to-nothing caution).
3. **`ResourceUri` on `AccessTokenProvider`** (RFC Phase 2 option (a)) with both defects fixed —
   *the whole of the follow-up PR*:
   - only the credential triplet (`TenantId`/`ClientId`/`ClientSecret`) gates acquisition; the
     optional `ResourceUri` must not, so deploying without the new key changes nothing (the
     LPII-166 reflection `nullCheck` defect, pinned by a regression test);
   - when `ResourceUri` is set, request the *target* resource from the v2
     `/oauth2/v2.0/token` endpoint with `scope = {ResourceUri}/.default` (RFC Appendix B wants this
     anyway); when unset, the legacy v1 self-token path is byte-for-byte unchanged. Tests assert
     the outgoing form body on both paths (note: the v2 endpoint returns `expires_in` as a JSON
     number, which the old `Dictionary<string, string>` deserialization would have rejected).

**Why a follow-up rather than merging the LPII-166 branch:** its three files carry both the mechanics and
the two defects, and that branch predates #208. Cherry-picking the ideas into a branch cut from
post-#208 `main` avoids the conflict resolution entirely and lets the fixes land with their tests.
If the LPII-166 branch is instead rebased onto `main` and fixed there, that is equally fine — the
point is the *order*, not the author.

**What this depends on ("further developments"):** nothing that blocks step 1. It *is* shaped by
the answers to the comparison's §8 questions — in particular whether the LPII-166 tests really exercised
the `ResourceUri` code path — and by confirming the config-key choice with him so the two branches
don't encode different shapes.

### Step 3 — Activation ladder (no code)

Only after steps 1–2 are deployed, and strictly in this order, because each rung is gated by the
previous one:

1. **Leeds admin does Phase 0** (admin doc Part 1: role, delegated scope, `idtyp` claim). Independent of
   code — can be requested now, in parallel with steps 1–2.
2. **Config flip: accept both audiences** — *replace* the singular `AzureAd:Audience` key with
   `AzureAd:TokenValidationParameters:ValidAudiences` listing both audiences — **four entries, not
   two**: each audience in its `api://` and bare-GUID forms, permanent pair first (the full shape,
   the reason for the bare-GUID forms, and the entry order are in "How the rungs are spelled in
   deployed config" below). This is pure config against the stock binding already on `main` from #208 —
   no code waits on it. With both keys present acceptance is the *union* (pinned by
   `AudienceValidationTests`), so a forgotten removal of the singular key is harmless here (its
   value is in the list) but replace-not-accumulate is the rule. Verify with `/whoami` and the
   audience tests' shapes.
3. **Phase 1 — Goobi's registration + role assignment**, its `KnownClients` profile with `depositBucket`.
   This is the point where Goobi is delivered; nothing user-visible waits on the later phases (which is
   exactly why RFC §6's completion commitment exists — name an owner and date for Phases 2–4 here).
4. **Phase 2–4** as RFC §6: repoint callers one at a time (assignment *before* repoint, or
   `AADSTS501051`), repoint the UI (delegated scope consent + user assignments first, or `AADSTS50105`),
   retire the transitional audience, switch the human/machine predicate to `idtyp`, remove the header.

**How the rungs are spelled in deployed config.** The deployed environments configure the APIs
through env vars in the ops repo's terraform (ECS task definitions; `__` maps to `:`, lists need
indexed keys), so each rung's "config flip" is literally an edit to those maps:

- *Rung 2 (per API)* — **done on dev 2026-09-21; production repeats these exact steps with the
  production registrations' values (recompute them — never copy dev's).** In BOTH
  `ecs-preservation-api.tf` and `ecs-storage-api.tf`, replace the `AzureAd__Audience` entry with
  a four-entry list — each audience in both its `api://` and bare-GUID forms (the bare forms
  survive a future `accessTokenAcceptedVersion: 2` flip, whose v2.0 tokens carry the bare GUID
  as `aud`; an explicit list drops Microsoft.Identity.Web's default both-forms tolerance). The
  values live as purpose-named keys **added to the Preservation API's existing `oauth_azure`
  secret**, and both services pull from that one secret — one source of truth for a list the two
  APIs must never let drift; the cross-path IAM read is granted automatically because the
  secrets-grant module derives from the same map:

  | Secret key (added alongside the existing ones) | Value |
  |---|---|
  | `ApiAudience` | `api://` + the API's `ClientId` — the permanent audience |
  | `ApiAudienceGuid` | the API's bare `ClientId` |
  | `TransitionalAudience` | the existing `Audience` value (`api://<UI client id>`) |
  | `TransitionalAudienceGuid` | its bare GUID |

  ```
  (These edits are already WRITTEN and merged to preservation-ops main — dev PR #72, test #75,
  prod #76 — so for test and production the remaining step is applying, not authoring.)

  AzureAd__TokenValidationParameters__ValidAudiences__0 = …preservation-api/oauth_azure:ApiAudience
  AzureAd__TokenValidationParameters__ValidAudiences__1 = …preservation-api/oauth_azure:ApiAudienceGuid
  AzureAd__TokenValidationParameters__ValidAudiences__2 = …preservation-api/oauth_azure:TransitionalAudience
  AzureAd__TokenValidationParameters__ValidAudiences__3 = …preservation-api/oauth_azure:TransitionalAudienceGuid
  ```

  Permanent pair first, deliberately: retirement (below) deletes the `__2`/`__3` lines and the
  indices stay contiguous, which .NET's env-var list binding requires. A wrong character in the
  secret values deploys cleanly and only fails when tokens arrive, so verify the four
  relationships before applying, without printing a value:

  ```bash
  aws secretsmanager get-secret-value --secret-id <.../oauth_azure> --query SecretString --output text \
    | jq -r '"\(.ApiAudience == "api://" + .ClientId) \(.ApiAudienceGuid == .ClientId) \(.TransitionalAudience == .Audience) \("api://" + .TransitionalAudienceGuid == .Audience)"'
  ```

  (Expect four `true`s.) Two companions to keep in step: `appsettings.Example.json` in both APIs
  documents the same four-entry shape, and each API's untracked `appsettings.Development.json`
  needs the same list for local work. One CLI caution: the console's Key/value editor adds a
  secret key in place, but `put-secret-value` replaces the WHOLE JSON — get, edit, put.
- *Rung 3 (per caller, on both APIs)* — `KnownClients` entries are plain env vars too (GUID
  hyphens are fine in ECS env var names):

  ```
  KnownClients__<caller-app-id>__Name          = goobi
  KnownClients__<caller-app-id>__DepositBucket = <goobi bucket>
  ```

- *Rung 4, Phase 2* — one line alongside the existing `TokenProvider__*` trio:
  `TokenProvider__ResourceUri = api://84c62880…`, and repoint that trio's source from the UI
  registration's credentials to the API's own (today Preservation API, Pipeline API **and the
  Deposit Archiver** all mint as the UI registration — the Archiver's `OAUTH_AZURE_SECRET`
  defaults to the UI secret path (`/preservation/<env>/ui/oauth_azure`) in every environment, so
  it must be repointed here too, not just the two APIs — the "mint as `a616cf42…`" arrangement
  Phase 2 exists to replace). `ResourceUri` is deliberately a NEW key, not a reuse of the
  existing `ScopeUri`: `ScopeUri` belongs to the delegated (signed-in-user) flow and is populated
  in every environment today, while `ResourceUri`'s *absence* is what keeps the machine mint on
  the legacy path — reusing an always-present key would flip behaviour on deploy day instead of
  when this rung is deliberately taken.
- *Rung 4, retirement* — delete the `…ValidAudiences__2`/`__3` (transitional) lines from both
  files, leaving the permanent pair at `__0`/`__1`. The `Transitional*` keys — and the old
  `Audience` key, by then referenced by nothing — can be removed from the secret at the same
  time.

**Standing rule for the whole transition window:** every build keeps supporting **both** models until
production has completed the ladder. An environment's position on the ladder is expressed in its
configuration (`ValidAudiences`, `KnownClients`) and its tenant's Entra state — never in the build —
so dev runs the ladder to completion first, and production repeats it later on whatever the current
build is, with nothing to wind back in between. Of rung 4's tidy items, only retiring the
transitional audience is per-environment (config); the code removals — the `X-Client-Identity`
fallback and the switch of the human/machine predicate to `idtyp` — happen once, after the **last**
environment (production) finishes. Dev "completing" the ladder means config-complete, not
code-complete; a PR that deletes the fallback while any environment still depends on it should not
pass review.

## Cutting a production release

Production releases are infrequent, named and tagged, which makes the interaction between the
release schedule and the activation ladder worth stating explicitly.

**The natural cut is the merge of this PR.** At that point `main` is the *complete dual-mode
platform*: every line of ladder code through Phase 3 is in the build, and all of it is inert under
an environment's existing configuration (empty `KnownClients`, header fallback, old audience). A
production deployment of that release behaves exactly as before, and production then climbs
rungs 0-3 — Entra, appsettings, the Goobi bucket and its infrastructure — entirely between
releases, at its own pace. At the time of writing, the last tag (`v1.2.1`, 2026-06-22) is 374
commits behind `main` — and **production actually runs `V1.0.1` (deployed 2025-10-24)**, so the
production delta is eleven months, not three: the gap includes the September 2026 security fixes,
the #188 migration machinery (so the same release also unblocks the production METS-ID campaign),
and the Deposit Archiver. Practical consequences for the production deploy: EF migrations for both
databases apply on startup (snapshot first), and the deploy-order-safety claims below were verified
against `V1.0.1`'s binding as well as `v1.2.1`'s (both pin Microsoft.Identity.Web 3.8.3 with the
identical `AddMicrosoftIdentityWebApi` call).

**Phase 4 never traps a release.** Its two behavioural steps — refusing an unknown `azp`, and
enforcing `Preservation.Call`-role-or-delegated-scope — are to be built as **configuration flags,
off by default** ([#293](https://github.com/digirati-co-uk/digital-preservation/issues/293); the
flag-gated code is not in this build — it can be written any time after step 2 merges, and is
needed only before the first environment enforces). That is the
standing rule above applied to Phase 4 itself: the same tagged build serves an environment with the
flags on and one that has not started the ladder, so no future release can strand production behind
the Entra schedule. Only the header-path deletion is one-way, and that goes in a release cut after
the **last** environment is enforcing (see the standing rule).

**Pre-cut check.** This release validates more strictly than `v1.2.1` (slugs, METS paths,
repository paths). Before tagging, run the read-only validation survey against production —
`mets_id_migration.py validation-survey` for METS paths the parser now refuses, plus its Fedora-DB
query for slugs containing `%` (which also answers
[#287](https://github.com/digirati-co-uk/digital-preservation/issues/287)) — so anything the new
rules would refuse is found before the release, not after it. The `validation-survey` subcommand
ships in [PR #294](https://github.com/digirati-co-uk/digital-preservation/pull/294), which must be
merged before this gate can run — it is an operator tool, not application code, so it has no
bearing on what the release tag contains.

**This gate RAN CLEAN for v1.3.0, against production, on 2026-09-22**: repository slugs — 0 of
804,434 `simple_search` ids contain `%` (evidence and method on
[#287](https://github.com/digirati-co-uk/digital-preservation/issues/287)); METS paths — all 220
surveyed Archival Groups clean (two ~1,600-resource groups needed a `HTTP_TIMEOUT_SECONDS=900`
retry against a cold AG cache — sizing evidence on
[#244](https://github.com/digirati-co-uk/digital-preservation/issues/244)). The survey is a
snapshot of that date's content: re-run it (about six minutes) only if significant new content
reaches production before the deploy.

The sequence in full: merge #294 (the survey tool) → survey production → merge this PR → tag →
deploy → production ladder by configuration → enforcement flags on (#293) → header-path deletion
in the release after that.

## What would change the sequence

- **If the POC topology were adopted instead of the RFC's** (comparison §7.3 — a recorded contingency,
  not the recommendation): steps 1 and 2 are *unchanged*. The bucket code keys on the resolved caller
  identity via `IClientDirectory`, and the audience-override mechanics are topology-neutral. Only the
  activation ladder differs: per-caller App ID URIs in `ValidAudiences` and `Assignment required = Yes`
  on each caller registration, instead of one audience plus role assignments. That is the reason the
  sequence is safe to start now regardless of how the LPII-166 topology discussion ends.
- **If the LPII-166 branch is merged before #208** (not recommended): #208 must then be rebased; the
  `Program.cs` conflicts are still trivial, but the `AccessTokenProvider` defects would be on `main`
  — with the `nullCheck` one able to break token acquisition on any deploy that lacks the new key.

## The PR #177 precedent

On PR #177's branch, a manual resolution of a `main`-into-branch merge conflict in
`Storage.Repository.Common/Storage.cs` silently dropped an unrelated block (the `metadata/ad-hoc`
scaffolding from #176); it was only caught by a later compatibility check. The equivalent hazard here is the two `Program.cs` files: whoever
resolves a conflict there must end with **both** the `AddClientDirectory` block *and* the audience
override present in each API. Check by grepping for both after resolution, not by eyeballing the diff.
