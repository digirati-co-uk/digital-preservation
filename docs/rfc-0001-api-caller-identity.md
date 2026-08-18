# RFC-0001: Trustworthy API caller identity

| | |
|---|---|
| **Status** | Draft / for discussion |
| **Author** | Tom Crane (with assistance from Opus 4.8) |
| **Date** | 2026-06-26 |
| **Affects** | `DigitalPreservation.UI`, `Preservation.API`, `Storage.API`, all direct API clients (iiif-builder, EPrints migration, Playwright tests, future Goobi) |
| **Related** | [`api-caller-identity.md`](./api-caller-identity.md) (working notes), [`adr/0000-service-clients.md`](./adr/0000-service-clients.md), [ADR-005 Auth setup](https://github.com/digirati-co-uk/digital-preservation-docs/blob/main/adr/005-auth-setup.md) |

> [!NOTE]
> This RFC builds on the working notes in [`api-caller-identity.md`](./api-caller-identity.md). Those notes establish the Entra concepts (app registration vs enterprise application, API/resource app vs client app, `azp`/`oid` claims). This document assumes that grounding and focuses on **the problem we actually have** and **the change we should make**.

## 1. Summary

Today every component of the preservation stack — the UI, the Preservation API, and every direct API client — authenticates against Microsoft Entra using a **single shared app registration** (the Web-UI's, `a616cf42…`). As a result, Entra cannot tell our callers apart: every machine token carries the same `azp` (calling-application) claim. We augment this with a self-asserted, unauthenticated HTTP header, `X-Client-Identity`, purely so that logs and METS authorship can attribute an action to *something*.

This RFC proposes moving to the model already sketched as "Option A" in the working notes: **one registration per caller**, each with its own credential and its own app-role grant on the API, with the API as the single audience. The payoff is that we can determine **who is calling us cryptographically and safely** — from the signed token, not a spoofable header — and gain per-caller revocation, least privilege, and real auditing.

> [!NOTE]
> This is largely a **course-correction back to [ADR-005](https://github.com/digirati-co-uk/digital-preservation-docs/blob/main/adr/005-auth-setup.md)**, from which the live Entra configuration has drifted. ADR-005 already prescribes per-API audiences (each API's `Audience` = `api://<its own client id>`, *not* the UI's) and app roles for machine-to-machine callers. Neither was implemented: the API `Audience` was set to the UI app (`api://a616cf42…`), and no app roles were ever created — which is what collapsed every caller onto the one shared registration. "Option A" is, in effect, *implementing what ADR-005 already specified.*

### 1.1 Motivation: the driving use case

The immediate driver is **Goobi**. We want deposits created by Goobi to land in a **dedicated, Goobi-only bucket**, with the bucket chosen **server-side by the API from the caller's identity** — the caller gets no say in which bucket is used. The deposit-create surface need not change: the API infers the bucket from *who is calling*. (Any API-surface implications are deferred — see §8.)

This turns caller identity from an **audit** concern into an **authorization / data-isolation** concern: if a caller could assert "I am Goobi", it could steer deposits into — or out of — the isolated bucket. The spoofable `X-Client-Identity` header is therefore no longer sufficient; the routing decision must key off a **cryptographically verified** identity (the signed token). Establishing that reliable identity is the purpose of this RFC; **bucket routing is its first consumer**.

## 2. Current mechanism

### 2.1 Two authentication paths

The APIs accept two kinds of caller:

1. **Humans, via the UI (delegated).** A user signs in to `DigitalPreservation.UI` (OpenID Connect). When the UI calls the Preservation API it forwards a token acquired *on behalf of the user*; that token carries user claims (`preferred_username`, `name`). The user's display name then flows downstream because `Preservation.API` relays the inbound `Authorization` header to `Storage.API` verbatim (`PropagateCorrelationIdHandler`).

2. **Machines, directly (app-only / client credentials).** iiif-builder, the EPrints migration scripts, the Playwright test suite, and (later) Goobi obtain an app-only token via MSAL and call the API directly. App-only tokens carry **no** user claims.

### 2.2 How identity is derived today

`DigitalPreservation.Core/Auth/ClaimsPrincipalX.GetCallerIdentity` (the `ClaimsPrincipal` overload — the one its ~21 call sites use) returns:

- the principal's display name for a human caller, else
- the `"dlipdev"` fallback.

(Sibling overloads taking an `HttpContext`/`HttpRequest`/raw token return `"unknown"` fallbacks — and derive identity by reading `upn` from the `Authorization` header's JWT **without signature validation**. They are attribution-only today, but they violate G1 and should be retired as part of this work — see Appendix B.)

Because app-only tokens have no display name, machine callers produce no useful identity here. To compensate, `DigitalPreservation.Core/Auth/AuthFilterIdentifier` reads the **`X-Client-Identity`** header and synthesises a `Name` claim from whatever string the caller sent. This header is the *only* thing currently distinguishing one machine caller from another.

> [!IMPORTANT] 
> This is not a security hole, because we don't **Authorise** based on this header; we just use it to audit; all our callers are trusted, Entra-authorised clients, we assume they have no reason to supply a false header. However - with Goobi especially, we will want to authorise - or at least, have different behaviour - based on who is calling the API. So the current model is no longer enough.

### 2.3 Authorization

Both APIs register, globally, in `Program.cs`:

```csharp
config.Filters.Add(new AuthorizeFilter());      // == [Authorize]: requires an authenticated caller
config.Filters.Add(new AuthFilterIdentifier()); // attribution only
```

`new AuthorizeFilter()` **does** enforce authentication (its parameterless constructor is equivalent to `[Authorize]`; confirmed by `Storage.API.Tests/Integration/ApiAuthorizationStackTests.cs` and by probing the live dev API). So a valid Entra token is **required**. But beyond "is the token valid for our audience?", there is **no scope, role, or per-client authorization**. Authentication ≈ authorization.

## 3. The problem

Inspecting the dev configuration reveals that the stack has collapsed onto one registration:

| Where | Setting | Value |
|---|---|---|
| Web-UI appsettings | `AzureAd:ClientId` | `a616cf42…` (the Web-UI registration) |
| Web-UI appsettings | `AzureAd:ScopeUri` | `api://a616cf42…/.default` |
| **Preservation.API** appsettings | `AzureAd:ClientId` | `84c62880…` (the API registration) |
| **Preservation.API** appsettings | `AzureAd:Audience` | `api://a616cf42…` ← **the UI app, not the API app** |
| iiif-builder (`.env`) | `PRESERVATION_CLIENT_ID` | `a616cf42…` |
| Playwright tests | `API_CLIENT_ID` | `a616cf42…` |

Following the audience: **every caller requests a token for `api://a616cf42` (the UI), and the Preservation API validates that same audience.** So the resource identity for the whole system is the *Web-UI* registration. The purpose-built `Library-Preservation-API-Dev` registration (`84c62880`) is named as the API's `ClientId` but is **never requested or validated as an audience by anyone** — it is effectively vestigial.

Worse, iiif-builder and the Playwright tests authenticate *as* `a616cf42` — i.e. they present the UI's client ID (and the UI's client secret). They are, to Entra, the UI.

> [!WARNING]
> The "Assignment required = Yes" setting on `Library-Preservation-API-Dev` (`84c62880`, confirmed already set) is a **red herring *in the current state*** only: no caller requests `84c62880` as a resource today, so its assignment gate never fires, and the gate that actually matters sits on `a616cf42`. This is what made the live setup confusing — it *looks* like `84c62880`'s gate should block machine callers, but they never reach it. **Post-migration this flips:** once callers request `api://84c62880…` (Phase 2), that same already-on gate becomes the real per-caller enforcement — which is convenient (Phase 4 needn't enable it) but imposes a strict ordering: a caller must be **assigned `Preservation.Call` before** it is repointed, or Entra rejects it with `AADSTS501051`.

### 3.1 The access boundary is real — but it lives on the UI registration

Inspecting the **enterprise application** for `a616cf42` (the real audience) settles the question of who can actually obtain a token. Its **Assignment required = Yes** (confirmed), and its assignment list contains:

- a set of **named human users** (and a Playwright browser-login service account) — these are the *delegated* path; with assignment enforced, only these people can sign in to the UI and have tokens issued on their behalf;
- exactly **one service principal — `Library-Preservation-Web-UI-Dev` itself**.

That self-entry is the linchpin. It is an app **assigned access to its own exposed API** (created when admin consent was granted for the registration's permission to call `api://a616cf42`). Because the API defines no app roles, it is the default-access role. **This self-assignment is precisely what lets the shared registration mint app-only (client-credentials) tokens for its own API URI.** Every machine caller — iiif-builder, the Playwright app-only path — borrows this identity, so they all pass the gate as *"`a616cf42` calling `a616cf42`"*.

The consequence corrects an earlier worry. Because `a616cf42` has **Assignment required = Yes**, a *genuinely different* app requesting `api://a616cf42/.default` via client credentials is **rejected** by Entra (`AADSTS501051: application is not assigned to a role for the application`). So it is **not** true that "any registered application in the tenant can call the Preservation API" — access is locked to a **single, shared machine identity**. The boundary works; it was simply attached to the UI registration, which is why it was invisible from `84c62880`.

> [!NOTE]
> This can be confirmed empirically: create a throwaway registration with a secret, leave it unassigned, and request `api://a616cf42/.default`. A `AADSTS501051` response demonstrates the gate holds.

### 3.2 Consequences

1. **Callers are cryptographically indistinguishable.** `azp`/`appid` = `a616cf42` for the UI, iiif-builder, Playwright, and any future client following the pattern. The token cannot tell us who is calling.
2. **`X-Client-Identity` is load-bearing but untrustworthy.** It is the only discriminator we have, yet it is self-asserted and unauthenticated — fine as a cosmetic label, unacceptable as an identity for any authorization or audit-trust decision.
3. **Shared secret, shared blast radius.** A single leaked secret compromises every caller; rotating the UI's secret breaks every caller at once.
4. **No least privilege.** A read-only consumer (iiif-builder) holds exactly the same rights as a read-write producer (Goobi). There is no way to scope a caller down.
5. **No audience separation.** UI vs API, and Preservation API vs Storage API, all validate the same audience — a token minted for one is valid for all.
6. **The "who can call me" list is real but collapsed.** Access *is* gated (see §3.1: `a616cf42` enforces assignment), but it admits exactly one shared machine identity. The list is meaningful as a yes/no boundary and useless as a per-caller one — every machine caller is the same entry.
7. **The APIs are themselves shared-registration callers.** Preservation API and Pipeline API mint their *own* app-only token for downstream service-to-service calls (e.g. Preservation→Storage when there is no inbound user token to relay) via the `TokenProvider` config + `AccessTokenProvider`. That section is populated with the **UI registration's** `client_id`/secret (`a616cf42…`), and the grant requests `api://{client_id}` — so the platform's own internal calls are *also* the shared identity, and they self-assert `X-Client-Identity: "api-call"`. `TokenProvider` is therefore an in-scope caller to migrate (Phase 2), not just the external clients.

> [!IMPORTANT]
> Consequence #1 directly **blocks the Goobi use case (§1.1)**: while every machine caller shares the `a616cf42` identity, there is no reliable key on which to route Goobi's deposits to a Goobi-only bucket. Reliable per-caller identity is a **prerequisite** for that feature, not merely good hygiene.

## 4. Goals and non-goals

**Goals**

- G1 — Each caller (human or machine) is identified from the **validated, signed token**, never from a client-supplied header.
- G2 — Machine callers are **distinguishable** from one another and individually **revocable**.
- G3 — The API can apply **least privilege** per caller (at least: can-call vs cannot-call; ideally read vs write).
- G4 — The Entra portal presents a **real, enforced list** of who may call each API.
- G5 — Migration is **incremental** — no big-bang cutover, callers move one at a time.

**Non-goals**

- N1 — Replacing Entra or the MSAL-based flows.
- N2 — Reworking the human/delegated path; it already yields a trustworthy identity.
- N3 — Solving Preservation→Storage audience separation in full (noted as follow-up in §8).
- N4 — Removing `X-Client-Identity` immediately; it is demoted, then removed once nothing relies on it.

## 5. Proposed design (Option A)

### 5.1 Registrations

- **The API registration (`84c62880…`) becomes the single audience.** Ensure it has an Application ID URI (`api://84c62880…`) and define one or more **app roles** (application permissions), e.g. `Preservation.Call` (and optionally `Preservation.Read` / `Preservation.Write`).
- **Each caller gets its own app registration** with its own credential (prefer a certificate or federated credential over a client secret where possible): `…-iiif-builder-Dev`, `…-Goobi-Dev`, `…-EPrintsMigration`, `…-AdminScripts-Dev`, `…-PlaywrightTests-Dev`.
- **Each caller is assigned the appropriate app role** on the API registration, with admin consent.
- **The UI keeps `a616cf42` for user sign-in**, but its *downstream* scope changes to `api://84c62880…/.default`. The UI becomes an ordinary client of the API — its registration is no longer the API's audience.

With this in place, every token presented to the API now carries the **caller's own** `azp`/`appid`, plus a `roles` claim describing what that caller may do.

> [!NOTE]
> **Goobi is greenfield** — it has no existing config to migrate, so it should be born with its **own registration + app role from day one** and never join the shared-`a616cf42` pattern. That makes Goobi the first concrete per-caller caller (see §6, Phase 1) and unblocks the bucket use case (§1.1) without waiting for the full migration.

### 5.2 Determining who is calling — safely

`GetCallerIdentity` is rewritten to derive identity strictly from validated claims, with a configured **allow-list** that maps each known caller's app ID to a friendly name:

```csharp
public static string GetCallerIdentity(this ClaimsPrincipal principal, IClientDirectory clients)
{
    // 1. Human (delegated) caller — unchanged, already trustworthy.
    var displayName = principal.GetDisplayName();
    if (!string.IsNullOrEmpty(displayName))
        return displayName;

    // 2. Machine (app-only) caller — identity comes from the SIGNED token.
    var appId = principal.FindFirstValue("azp") ?? principal.FindFirstValue("appid");
    if (!string.IsNullOrEmpty(appId) && clients.TryResolve(appId, out var name))
        return name;

    // 3. Unknown caller — never fall back to a client-supplied header.
    return "unknown";
}
```

```jsonc
// appsettings — the allow-list doubles as friendly-name resolution AND a list of permitted callers.
// Entries are always objects: as implemented, the section binds to Dictionary<string, ClientProfile>,
// and a flat `"guid": "name"` string form does NOT bind — the config binder silently ignores the
// scalar value, leaving a profile with a null name.
"KnownClients": {
  "11111111-1111-1111-1111-111111111111": { "name": "iiif-builder" },
  "22222222-2222-2222-2222-222222222222": { "name": "goobi" },
  "33333333-3333-3333-3333-333333333333": { "name": "eprints-migration" },
  "44444444-4444-4444-4444-444444444444": { "name": "playwright-tests" }
}
```

The same map is the natural home for **per-caller policy**, not just a display name. For the Goobi use case (§1.1) the profile carries more than a name, e.g. the target deposit bucket:

```jsonc
"KnownClients": {
  "22222222-2222-2222-2222-222222222222": { "name": "goobi", "depositBucket": "leeds-goobi-deposits" },
  "11111111-1111-1111-1111-111111111111": { "name": "iiif-builder" }
}
```

The point here is only that the verified `azp` is the safe key such policy hangs off; the deposit-create handler reads the resolved caller's profile to choose the bucket, and the caller cannot influence it.

**Bucket routing is implemented** (on this branch), and is deliberately narrow:

- `CallerResolver.ResolveDepositBucket` is the single decision point: it returns the profile
  bucket **only** for a machine caller resolved from the signed `azp`/`appid`. A human user, or a
  machine identified only by the self-asserted `X-Client-Identity` header, always gets the
  default — the spoofable header can never steer deposits into another caller's bucket. `Resolve`
  (the `/whoami` path) uses the same function, so what `/whoami` reports is what creation does.
- `CreateDepositBase` passes the result as the `workingRoot` of
  `IStorage.GetWorkingFilesLocation` (a bucket name in the S3 implementation; the parameter is
  storage-neutral for future filesystem/other-blob implementations). This carve-out is the **only**
  moment the choice matters: the deposit's own `Files` URI is stored on the entity and every
  subsequent storage operation derives its location (and bucket) from that URI.
- **The pipeline only runs on default-bucket deposits.** Pipeline.API reaches deposit files
  through a filesystem mount of the default working bucket, so `RunPipelineHandler` declines
  (400) any deposit whose `Files` URI is outside it. **Caveat: it is *not* the only producer of
  pipeline jobs** — Pipeline.API's own `POST /pipeline` (`PipelineController`, guarded only by
  `X-API-KEY`) publishes the same message to the same topic with no bucket check, bypassing this
  guard. That entrypoint needs the same check, or retiring. Revisit if a routed caller ever
  needs characterisation.
- Deployment prerequisite, not code: the task roles of every service that touches deposit
  files (Preservation API, Storage API + Importer for export, UI) need IAM read/write on each
  per-caller bucket, and the caller needs access to its own bucket.

Why this is safe:

- The `azp`/`appid` claim is **signed by Entra** and validated as part of token validation — a caller cannot forge it.
- The allow-list is a closed set: an app ID we do not recognise resolves to `"unknown"` and (see §5.3) can be rejected outright.
- No code path trusts `X-Client-Identity` for identity. It survives, if at all, only as a cosmetic hint that must agree with the resolved identity or be ignored.
- **Caveat (open — see §8 Q7):** the human-vs-machine test is not yet unified. `AuthFilterIdentifier` detects a human via `GetDisplayName()` (which also honours `name`/`upn`), while `CallerResolver` keys on the literal `preferred_username` claim. A v1.0-format delegated token (which carries `upn`/`unique_name` but no `preferred_username`) would be classed human by the filter but *machine* by `ResolveDepositBucket` — and, if its `azp` matched a `KnownClients` profile with a bucket, could route a **human's** deposit into a per-caller bucket. The guarantee above currently holds for v2.0 delegated tokens (the UI's shape today); both chokepoints should share one predicate built on `idtyp` (Appendix A).

A future enhancement (out of scope here) can resolve unknown app IDs live via Microsoft Graph (`GET /servicePrincipals(appId='{azp}')`) instead of, or in addition to, the static map.

### 5.3 Authorization

Today's boundary (§3.1) already relies on **Assignment required = Yes** — but on `a616cf42`, admitting one shared identity. Option A keeps the same mechanism and simply moves it to the real API registration with a role per caller:

- Set **Assignment required = Yes** on the API registration (`84c62880`) so Entra will only issue tokens to callers that have been assigned a role — the same gate that protects `a616cf42` today, now per-caller.
- Enforce the role in the API — e.g. `Microsoft.Identity.Web`'s `[RequiredScopeOrAppPermission]`, or a policy that checks the `roles` claim — so a token without `Preservation.Call` is rejected (403) even if otherwise valid. **Delegated callers must not be forgotten here:** a human's token via the UI carries `scp` (the delegated scope), never `roles`, so enforcement must accept *role or scope* — checking the app role alone would 403 every human. This is one reason the API must expose a delegated scope (see Phase 0/Phase 3).
- Optionally split read vs write roles so iiif-builder cannot mutate and Goobi can.

This is what turns the "who can call me" portal page (G4) into a real, enforced list.

#### 5.3.1 Authorization is per-API and can be per-endpoint

Storage API and Preservation API share the same *authentication* mechanism, but that does not force them to share the same *authorization* check. Each API decides which roles it requires, and an individual endpoint can require more than the API's baseline. The `roles` claim is carried in the token regardless, so this granularity is available even before any audience split.

The motivating example is Storage's **`GET /content/{*path}`** (`ContentController`) — it streams raw preserved binary bytes straight out of Fedora/S3. A *typical* Preservation API caller has no business pulling raw content directly from the storage layer (Storage is meant to be the gateway, reached *through* Preservation — see §7 "Defence in depth"). So `/content` is a good candidate to gate behind a dedicated role (e.g. `Storage.Content.Read`) that the everyday Preservation caller simply does not hold, even though it holds `Preservation.Call`. Other Storage operations could likewise carry their own roles, independent of what Preservation requires.

This is cleanest once **Storage has its own audience and its own app roles** (§8 Q1): roles are defined on the resource registration they protect, so Storage-specific roles naturally live on a Storage registration. Until then the same effect is achievable by defining the extra roles on the shared registration and having Storage check for them specifically — but the audience split is the tidier end state.

## 6. Migration plan (incremental, dual-audience)

The key enabler for a non-breaking migration is that the API can be configured to accept **both** audiences during transition.

**Phase 0 — Prepare the API (no caller impact).**
- Add the Application ID URI and the `Preservation.Call` app role to `84c62880`, and expose one **delegated scope** (e.g. `access_as_user`). The scope is dormant now but is a hard prerequisite for Phase 3: the UI's delegated `/.default` request only succeeds against a consented delegated permission, and app roles are invisible to the delegated flow.
- Configure the API to accept **both** audiences.

  > [!CAUTION]
  > **A top-level plural `Audiences` key does NOT work** — an earlier draft of this RFC prescribed exactly that, wrongly. In the `AddMicrosoftIdentityWebApi(GetSection("AzureAd"))` path both APIs use, the section is bound onto exactly two types: `JwtBearerOptions` (singular `Audience` only) and `MicrosoftIdentityOptions` (no audience properties at all). The class that *does* expose `Audience`/`Audiences` — `MicrosoftIdentityApplicationOptions` — is never bound in this path. An `Audiences` key therefore binds to nothing, and with the singular `Audience` removed, `Microsoft.Identity.Web` falls back to its **default** audiences derived from `ClientId` (the `84c62880…` forms only) and **rejects every current caller's `api://a616cf42…` token** (`IDX10214`) — turning the "no caller impact" phase into a full outage. Verified empirically against `Microsoft.Identity.Web` 3.8.3.

  What works in config is binding through `TokenValidationParameters`, which the JwtBearer binder populates directly (verified against 3.8.3):

  ```jsonc
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "bdeaeda8…",
    "ClientId": "84c62880…",
    "TokenValidationParameters": {
      "ValidAudiences": [
        "api://a616cf42…",   // current audience (the UI registration) — accepted during transition
        "api://84c62880…"    // the API's own App ID URI — the target audience
      ]
    },
    "ClientSecret": "…"
  }
  ```

  Remove the old `"Audience": "api://a616cf42…"` key when adding this. The equivalent code-side form — Microsoft's documented approach for multiple audiences — is `services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o => o.TokenValidationParameters.ValidAudiences = …)`. Two hardening notes:
  - Consider also listing the bare-GUID forms (`a616cf42…`, `84c62880…`) alongside the `api://` URIs, so a future `accessTokenAcceptedVersion: 2` flip (v2 tokens carry the bare GUID as `aud`) cannot silently break validation. Microsoft.Identity.Web's *default* validation accepts both forms; an explicit list drops that tolerance.
  - A mis-shaped audience config deploys cleanly and only fails when tokens arrive. **Before shipping this change, add an integration test that validates one token per accepted audience and rejects a foreign audience** — no such test exists today.
- Ship the new `IClientDirectory` allow-list in **dual mode**: prefer the resolved `azp`, fall back to `X-Client-Identity` for callers not yet migrated. Log whenever the fallback is used.
  - **As implemented**, this dual-mode resolution lives in `AuthFilterIdentifier` — the existing post-auth chokepoint that already synthesises the machine caller's `Name` claim — rather than in `GetCallerIdentity`. It resolves `azp`/`appid` against `IClientDirectory` and injects the resolved name as that same `Name` claim, falling back to the header otherwise. This keeps `GetCallerIdentity`'s signature (and its ~21 call sites) untouched; the §5.2 form remains the eventual target. With `KnownClients` empty, no `azp` resolves, so behaviour is identical to today bar the fallback log line. Two operational caveats: the platform's own service-to-service calls self-assert `X-Client-Identity: "api-call"` (§3.2 #7), so the fallback warning fires on essentially **every internal request** until Phase 2 completes — log once per unresolved `appId`, or at `Information`, rather than a warning per request; and the fallback warning is currently asserted by no test, so the Phase 2 "watch the logs" mechanism is unverified.

**Phase 1 — Create per-caller registrations.**
- Create a registration per caller, each with its own credential, assigned `Preservation.Call` on the API (admin consent). Add each app ID to `KnownClients`.
- **Start with Goobi (§1.1).** Being new, it launches directly with its own registration + role and its `KnownClients` profile (including its bucket), delivering the bucket feature without waiting for the other callers to migrate.

**Phase 2 — Repoint callers one at a time.**
- For each machine caller, switch its config to its own `client_id` + credential and scope `api://84c62880…/.default`. Because the API accepts both audiences, callers move independently with no coordinated cutover. Watch the logs: the `X-Client-Identity` fallback warning should stop firing for each caller as it migrates.
- **Ordering is strict, because `84c62880` already has `Assignment required = Yes`** (see §3 warning): each caller must already hold its `Preservation.Call` assignment (Phase 1) *before* it is repointed here, or Entra rejects the token request with `AADSTS501051`. There is no grace window — assign, *then* repoint.
- **The `TokenProvider` callers (the APIs' own service-to-service identity, §3.2 #7) are *not* config-only.** `AccessTokenProvider` can only request its own `client_id` as the resource (the §8 Q1 constraint), so a new per-service registration cannot request `api://84c62880…/.default` without a code change. Choose explicitly: (a) extend `AccessTokenProvider` to decouple the requested resource from the client credential — the same change §8 Q1 wants for a Storage audience; or (b) as a stopgap, have the internal services authenticate *as* `84c62880` itself (own-resource then works) — which requires assigning `Preservation.Call` to `84c62880`'s **own service principal** (the same self-assignment linchpin §3.1 describes on `a616cf42`), an admin action that must be scheduled.

**Phase 3 — Repoint the UI.**
- **Prerequisites — without both, the delegated flow fails at token acquisition:**
  - The API registration must expose a **delegated scope** (created in Phase 0), the UI registration must be granted that permission, and **admin consent** given. A delegated `/.default` request resolves to the client's statically consented delegated permissions; with none configured, the request fails outright. App roles do not substitute — delegated tokens carry `scp`, never `roles`.
  - Because `84c62880` already has **Assignment required = Yes**, every UI *user* must be assigned on its enterprise application *before* the UI is repointed, or Entra rejects them with `AADSTS50105`. The human users already assigned there (Phase 0 admin doc) are the **start of this required list, not leftovers to remove** — extend it to all UI users (ideally via a group), including the Playwright browser-login account.
- Change the UI's downstream `ScopeUri` to `api://84c62880…/.default`. The UI continues to sign users in with `a616cf42`; only the API-call audience changes.

**Phase 4 — Tighten.**
- Remove `api://a616cf42…` from the API's accepted audiences, leaving only the API's own App ID URI:

  ```jsonc
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "bdeaeda8…",
    "ClientId": "84c62880…",
    "TokenValidationParameters": {
      "ValidAudiences": [ "api://84c62880…" ]
    },
    "ClientSecret": "…"
  }
  ```

  With a single entry you may instead revert to the singular `"Audience": "api://84c62880…"` (which *does* bind, via `JwtBearerOptions`) — but never a top-level `Audiences` key, which binds to nothing (see the Phase 0 caution).
- **Assignment required = Yes** on `84c62880` is **already set** (see §3 warning), so the only remaining tightening is to enforce the `Preservation.Call` role in code — accepting *role or delegated scope*, per §5.3, so humans via the UI are not locked out. (Verify the setting is still on rather than re-enabling it.)
- Switch `GetCallerIdentity` out of dual mode: unknown `azp` ⇒ rejected; `X-Client-Identity` no longer consulted for identity.
- Remove or down-grade `X-Client-Identity` to a purely cosmetic, ignored-on-mismatch hint.

Each phase is independently reversible until Phase 4.

## 7. Security considerations

- **Credential hygiene.** Per-caller credentials mean a leak is contained and a single caller can be revoked without collateral damage. Prefer certificates / federated credentials / managed identity over shared secrets; set expiries and a rotation process.
- **Defence in depth for Storage.API.** Because Storage currently re-validates the same forwarded token, it should remain reachable **only** from the Preservation API / importer at the network layer, regardless of this change.
- **No trust in transport-set headers.** After Phase 4, identity is derived solely from validated token claims. `X-Client-Identity` (and any similar header) must never influence an authorization or audit-trust decision.
- **Audit integrity.** Distinct `azp` per caller makes logs and METS authorship trustworthy and non-repudiable, which the shared-registration model cannot provide.
- **`FeatureFlags:DisableAuth` blast radius.** The flag skips the *entire* filter set — `AuthorizeFilter` **and** `AuthFilterIdentifier` — plus `UseAuthentication()`/`UseAuthorization()`. With it on, `/whoami` is anonymous and happily echoes a spoofed `X-Client-Identity` as the resolved name. It must never be enabled outside local/test environments; note that `Storage.API/appsettings.Example.json` currently ships it as `"true"`.
- **Header relay.** `PropagateCorrelationIdHandler` relays any *inbound* `X-Client-Identity` verbatim downstream, so a spoofed header propagates Preservation → Storage until Phase 4 removes the header entirely.

## 8. Open questions / follow-ups

1. **Storage.API audience.** Should Storage get its own audience and an on-behalf-of exchange from Preservation, rather than the current verbatim token relay? (Out of scope here; tracked separately.) Note a current constraint: `AccessTokenProvider.GetBearerToken` only ever requests a token whose audience equals its *own* configured `client_id` (`scope=api://{client_id}/.default`, `resource=api://{client_id}`) — it cannot target a different downstream resource. Preservation→Storage works today only because both validate the same shared audience. Giving Storage a distinct audience therefore requires more than config: `TokenProvider`/`AccessTokenProvider` must be able to request a *different* resource than the caller's own registration (or an OBO/token-exchange step must be introduced).
2. **Service *user* accounts on the API enterprise app.**
   - The **Playwright browser-login service account** is **resolved and out of scope**: it is a pseudo-human login the Playwright suite uses to drive a browser through a real UI sign-in for end-to-end tests. It rides the normal human/delegated path and needs no change. Note that Playwright therefore has *two distinct identities* — this browser user, and the app-only `API_CLIENT_ID` it uses for direct API calls. Only the latter (currently borrowing `a616cf42`) is in migration scope.
   - The **iiif-builder service account** is **still open**: the iiif-builder service itself is a daemon and should be purely app-only (client credentials), so a *user* account for it is unexpected. The leading hypothesis is that Leeds have built a **UI over iiif-builder** (source not currently available to us) that signs users in and calls the Preservation API on their behalf, with this account as its (test/service) login. That would explain a user-context path. What it does **not** yet explain is why that UI appears only as a user and not as its own client app registration — though that may simply be another instance of the registration-collapse described in §3 (if the iiif-builder UI also reuses `a616cf42`, it has no distinct app identity to surface). **Action:** confirm with Leeds whether such a UI exists, which registration it uses, and whether the account is live or a leftover — then fold it into the migration or retire it.
3. **Role granularity — read/write, and per-API/per-endpoint (see §5.3.1).** Is a single `Preservation.Call` sufficient for v1, or do we split read vs write, and define Storage-specific roles (e.g. `Storage.Content.Read` gating `GET /content`) that an everyday Preservation caller does not hold? This interacts with Q1: Storage-specific roles are cleanest once Storage has its own audience.
4. **Graph-based name resolution** vs the static `KnownClients` map — static is recommended for v1 (cheap, doubles as allow-list); revisit if caller churn becomes high.
5. **Bucket: routing vs isolation (Goobi, §1.1).** *Routing* is now implemented (§5.2): config in the `KnownClients` profile, consumed at deposit creation. Still open is *isolation* ("**only** Goobi may write there") — an IAM/authz concern rather than routing code — and whether the policy would be better expressed as a per-caller app role.
6. **Per-caller behaviour generally.** Bucket choice is the first identity-driven behaviour; others may follow (default rights statement, METS template, quota). Decide whether such policy belongs in the `KnownClients` profile or a separate policy store — and whether any of it warrants a change to the deposit-create API surface.
7. **Unify the human/machine discriminator.** `AuthFilterIdentifier` decides "is this a human" via `GetDisplayName()` (which honours `name`/`upn` as well), while `CallerResolver` keys on the literal `preferred_username` claim; the two chokepoints can disagree on the same token (see the §5.2 caveat), and a v1.0-format delegated token would be classed human by one and machine by the other — with bucket-routing consequences. Appendix A already names the right discriminator (`idtyp = app`): both should share a single predicate built on it, covered by tests that stub v1-shaped (`upn`-only) as well as v2-shaped (`preferred_username`) tokens.

## 9. Testing the migrated identity

Phase 0's `ApiAuthorizationStackTests` already characterise resolution at the filter level with a stubbed token (a signed `azp` → friendly name wins over the `X-Client-Identity` header). As later phases land, add end-to-end coverage that the *verified* identity drives real behaviour:

1. **Goobi bucket routing — the driving use case (§1.1).** With a caller presenting Goobi's identity (resolved `azp` → the `goobi` profile carrying `depositBucket`), create a deposit and assert the deposit's `.files` S3 URIs point at the **Goobi bucket**, not `AwsStorage:DefaultWorkingBucket`. This proves verified-identity → data-isolation end to end. The routing mechanism is now implemented and unit-tested at each seam (`CallerResolverTests` for the identity→bucket decision, `StorageTests` for carving in an explicit `workingRoot`, `RunPipelineHandlerTests` for the pipeline's default-bucket-only guard); this end-to-end stubbed-`azp` test through the API surface is still the outstanding piece.
2. **Default caller — e.g. the Playwright API identity.** A caller *without* a `depositBucket` in its profile creates a deposit and lands in the default bucket — proving Goobi's routing is specific to Goobi and the default path is unaffected. The app-only identity the Playwright suite uses for direct API calls (distinct from its browser-login user — §8 Q2) is the natural concrete subject once it has its own registration.

**Test seam.** Real-Entra tokens are awkward in CI, so prefer driving these through the test host with a stubbed `azp` claim (as `ApiAuthorizationStackTests` does) for deterministic CI coverage, plus an optional `Category=Manual` smoke test using genuine client-credentials tokens against dev.

**Known coverage gaps.** The accepted-audience configuration is now pinned by `Storage.API.Tests/Integration/AudienceValidationTests.cs`, which drives locally signed tokens through the real `AddMicrosoftIdentityWebApi` pipeline for all three config shapes: the Phase 0 `TokenValidationParameters:ValidAudiences` transition shape (both audiences accepted, foreign rejected), today's singular `Audience` shape, and — as a regression pin for the defect this section's caution describes — the top-level `Audiences` key binding to nothing and locking out the transitional audience. Still uncovered: the `X-Client-Identity` fallback warning that Phase 2 uses as its migration diagnostic.

### 9.1 The caller-identity endpoint (implemented)

`GET /whoami` is implemented on **both** APIs (`WhoAmIController` in each; resolution logic in
`DigitalPreservation.Core/Auth/CallerResolver.cs`). It returns only the calling identity as the API
resolved it:

```jsonc
{
  "name": "goobi",                    // friendly name (or display name for a human)
  "source": "token",                  // user | token | header-fallback | unknown
  "appId": "2222…",                   // signed azp/appid, when present
  "depositBucket": "leeds-goobi-deposits" // profile bucket, else the API's default working bucket
}
```

It exists to:

- give the tests above a **direct assertion target** for resolution, independent of any side effect such as bucket choice — and cover callers (like the Playwright API user) that have no distinctive side effect;
- serve as a **migration diagnostic**: during Phase 2 a caller confirms its `source` flips from `header-fallback` to `token` the moment it is repointed, mirroring the fallback warning log.

Keep it minimal and self-only: it sits behind the standard `AuthorizeFilter` and returns only the *caller's own* identity — never the `KnownClients` map or other callers. It **does** report the caller's resolved **deposit bucket** ("this is where deposits would be made for you"), since callers are expected to interact with that bucket directly; the bucket falls back to `AwsStorage:DefaultWorkingBucket` when the caller's profile carries none. Two caveats: each API resolves that fallback from its *own* `AwsStorage:DefaultWorkingBucket`, and Storage's value can differ from Preservation's — only the **Preservation** API's `/whoami` is authoritative for where a deposit would actually be created; and "sits behind the standard `AuthorizeFilter`" holds only when `FeatureFlags:DisableAuth` is off (see §7).

## Appendix A — App-only token claims we rely on

| Claim | Meaning | Use |
|---|---|---|
| `azp` / `appid` | client ID (GUID) of the calling app | **the** machine identity; resolve via `KnownClients` |
| `oid` | object ID of the caller's service principal | tenant-unique alternative key |
| `roles` | app roles granted to the caller | authorization (`Preservation.Call`, …) |
| `idtyp` = `app` | token is app-only (no user) | distinguishes machine vs human path |
| `azpacr` | how the client authenticated (secret/cert) | optional credential-strength policy |

## Appendix B — Key code touchpoints

- `DigitalPreservation.Core/Auth/ClaimsPrincipalX.cs` — `GetCallerIdentity` (rewrite per §5.2). Also retire the `HttpContext`/`HttpRequest`/raw-token overloads, which parse the bearer token **without signature validation** (§2.2).
- `DigitalPreservation.Core/Auth/AuthFilterIdentifier.cs` — stop synthesising identity from `X-Client-Identity`; enforce role; unify the human/machine predicate with `CallerResolver` on `idtyp` (§8 Q7).
- `Preservation.API/Program.cs`, `Storage.API/Program.cs` — `AzureAd` audience config; role enforcement filter.
- `DigitalPreservation.Core/Web/Headers/PropagateCorrelationIdHandler.cs` — relays the inbound user token downstream, and when there is none falls back to `AccessTokenProvider` for an app-only token, hard-coding `X-Client-Identity: "api-call"`; it also relays any *inbound* `X-Client-Identity` verbatim (remove both in Phase 4).
- `DigitalPreservation.Core/Web/Headers/AccessTokenProvider.cs` + the `TokenProvider` config section (`Preservation.API`, `Pipeline.API`) — the APIs' own machine credentials/identity for service-to-service calls (see §3.2 #7). Migrating these in Phase 2 is *not* config-only — the own-resource-only limitation (§8 Q1) forces either a code change here or the run-as-`84c62880` stopgap (see Phase 2). While touching it, move off the legacy v1 `/oauth2/token` endpoint.
- `Pipeline.API/Features/Pipeline/PipelineController.cs` — `POST /pipeline` enqueues pipeline jobs with no default-bucket guard, bypassing `RunPipelineHandler`'s check (§5.2); add the guard or retire the entrypoint.
- `Storage.API.Tests/Integration/ApiAuthorizationStackTests.cs` — existing characterisation of the authorization stack; extend with role-enforcement cases.
