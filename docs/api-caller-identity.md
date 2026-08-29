# Caller Identity in API

> [!WARNING]
> **Working notes, June 2026 — kept for the Entra grounding, not as a statement of current state.**
> The "Understanding Entra" section (registration vs enterprise application, resource vs client app,
> where a caller's secret comes from) is still the best plain-English introduction we have, and
> [RFC-0001](./rfc-0001-api-caller-identity.md) builds on it. The diagnosis in "Current conclusion" —
> everything runs as the Web-UI registration `a616cf42…`, `84c62880…` is vestigial — is also still
> right. But the notes are a snapshot of an investigation, and the following points have since been
> checked or superseded. For current state read the RFC, then the glossary in
> [`rfc-0001-lpii166-comparison.md`](./rfc-0001-lpii166-comparison.md).
>
> 1. **`idtyp` is an *optional* claim, absent by default** — the claims list below reads as if every
>    app-only token carries `"idtyp": "app"`. It doesn't; that is why the code infers human-vs-machine
>    from claim shape, and why RFC Phase 0 provisions the claim (with the caveat that it only attaches to
>    tokens requested *for* the configured registration). See RFC §6 Phase 0 and §8 Q7.
> 2. **Preservation→Storage does not only forward the caller's token.** `PropagateCorrelationIdHandler`
>    relays an inbound token when there is one; with none, it *mints* an app-only token via
>    `AccessTokenProvider` from the `TokenProvider` config section (RFC §3.2 #7). The conclusion that
>    `84c62880…` and its secret are dormant still holds — `TokenProvider` is configured with the UI's
>    client id — but the mechanism described below is incomplete.
> 3. **`a616cf42…`'s assignment gate has been checked** ("which you haven't checked" below is stale):
>    `Assignment required = Yes`, with only `a616cf42…` itself assigned — the self-assignment linchpin in
>    RFC §3.1. Only holders of the one shared secret can mint an `a616cf42…`-audience app token.
> 4. **"Resolve `azp` → friendly name" is no longer blocked.** PR #208 implements it in dual mode in
>    `AuthFilterIdentifier`: prefer the signed `azp`/`appid` against the `KnownClients` allow-list, fall
>    back to `X-Client-Identity` for callers not yet migrated. Inert until `KnownClients` is populated.
> 5. **The existing assignments on `84c62880…`'s enterprise app are not junk.** The list below
>    (humans plus service accounts — the exact membership is awaiting the admin's export; the later
>    record shows humans plus `zz_libplaywrighttest`, not an iiif-builder account) will become
>    load-bearing at RFC Phase 3, when UI users must be assigned on this audience. Do not clear it.
>    See `rfc-0001-phase0-entra-admin.md` step 1.5.
> 6. **`Assignment required = Yes` on `84c62880…` is a "red herring" only for today's traffic.** It is a
>    design input for the migration: it imposes assign-*before*-repoint ordering in Phase 2 (else
>    `AADSTS501051`) and means Phase 4 has nothing to enable. See the admin doc, Part 3.
>
> The `X-Client-Identity` header was a deliberate, known trade-off for internal attribution; it is
> Goobi — a third party — that changes the requirement (RFC §1.1, comparison §2).

At the moment, we ask API callers to supply a `X-Client-Identity` HTTP header to identify themselves. This is not used as a credential in any way, all clients must authenitcate via MSAL and present a valid bearer token. It just allows us to audit calls - who created what.

For human users presenting credentials to the UI, their ClaimsPrincipal display name persists when calls on their behalf are delegated to the Preservation API. But direct API callers, like iiif-builder, eprints migration scripts and later Goobi, don't present a principal from which a distinct identity can be extracted.

## Available identity

_Written with assistance from Opus 4.8_

It's true that an app-only (client-credentials) token carries no _human/user_ identity — no `upn`, `name`, or `preferred_username`, which is why `GetDisplayName()` comes back empty and the code falls back to X-Client-Identity. But it's not true that we only know "they're authorised for the API." An Entra app-only token does identify _which client application is calling_ — verifiably, inside the signed token. What it doesn't give you is a human-readable name for that app.

When iiif-builder, etc.,  do client-credentials, the validated JWT contains, among others:

 - `azp` (v2.0) / `appid` (v1.0) — the client ID (GUID) of the calling application. This is the verifiable machine identity.
 - `oid` — object ID of the calling app's service principal in your tenant.
 - `sub` — for app-only tokens equals the service principal oid.
 - `idtyp": "app"` — tells you it's app-only (vs a user).
 - `roles` — app roles assigned to the calling application, if you've defined any (see below). Empty/absent by default.
 - `azpacr` — how the client authenticated (secret vs certificate).

So the distinction is: the token gives you the app's **GUID**, not its **display name**. Entra deliberately won't emit the app's display name in the token. To turn `azp` (GUID) → a name you either:
 
 - keep a small config map of known client IDs → service names (cheap, no extra calls, and it doubles as an allow-list of permitted callers), or
 - call Microsoft Graph (GET /servicePrincipals(appId='{azp}')) to resolve displayName (live, but a network hop + Graph permission).

## Our `X-Client-Identity` header

The header is a reasonable _convenience_ for supplying that human-readable label the token lacks. But it's self-asserted and unauthenticated, so it should be used for display/logging only, never as the identity for an authorization or audit-trust decision. The verifiable identity is in the validated token as `azp/oid`. A robust version of `GetCallerIdentity` would:

 1. prefer the user display name (human path), else
 2. resolve `azp`/`appid` from the validated token, map it to a friendly name via config (which also lets you reject unknown clients), and
 3. treat `X-Client-Identity` only as a fallback cosmetic label.


## Understanding Entra

If you go to the [App registrations](https://portal.azure.com/#view/Microsoft_AAD_IAM/ActiveDirectoryMenuBlade/~/RegisteredApps) page in the Azure portal, and filter by "preservation", you see six apps:


 - Library-Preservation-API
 - Library-Preservation-API-Dev
 - Library-Preservation-API-Test
 - Library-Preservation-Web-UI
 - Library-Preservation-Web-UI-Dev
 - Library-Preservation-Web-UI-Test

The latter three (the UI ones) have secrets associated with them (they *possess a client secret* - they have a secret they can present to **other** apps).

[App registrations](https://portal.azure.com/#view/Microsoft_AAD_IAM/ActiveDirectoryMenuBlade/~/RegisteredApps) is the Leeds tenant's directory of application identities. Every row is one application identity with one Application (client) ID (the GUID column).

The key thing: **this list does not separate "APIs" from "clients."** Both kinds of app live here. Being in this list just means "Entra knows about this application." Whether an app behaves as an API (something that gets called) or a client (something that calls) is decided by how you configure each registration, not by which list it's in.

> [!IMPORTANT] 
> Each registration here has a twin called an Enterprise application — the service principal. The App registration is where you configure the app; the Enterprise application is where permission grants / admin consent / app-role assignments are recorded. Same app, two views.

|                 |  API / resource app                       |            Client / caller app             |
|-----------------|-------------------------------------------|--------------------------------------------|
| Job             | Exposes endpoints, validates tokens       | Acquires tokens, calls an API              |
| Config you set  | Expose an API (Application ID URI api://…, scopes), App roles | API permissions, Certificates & secrets    |
| In your code    | the AzureAd section → it is the audience  | client_id + secret in MSAL / TokenProvider |
| Needs a secret? | Often *_no_* (pure validation)            | Yes — it's a confidential client           |


- The three `…-API` rows (prod / Dev / Test) are your **resource/API** apps. One per environment. The `AzureAd:ClientId` / `Audience` in Preservation.API appsettings points at one of these. When the Python iiif-builder asks for "a token for the Preservation API," the scope/audience is this app's `api://…` URI (e.g. `api://84c62880-…` for dev). Note their **Certificates & secrets = "-"** — fitting, because a pure API mostly just validates tokens, it doesn't need a secret of its own.
 - The three `…-Web-UI` rows are the UI acting as a **client** — a confidential web app. They show **secrets = Current** precisely because the UI signs users in and acquires  tokens to call the API. So the Web-UI is a _client_ of the Preservation-API.

Each client's **Application (client) ID** is the `azp` / `appid` value that appears in the token it sends. That GUID is the verifiable machine identity — Entra signs it into the token. The `X-Client-Identity` header is just a friendly label the client chooses to send _alongside_ that GUID.

The secret this client needs to send **doesn't come from the Preservation API at all — it comes from a client registration, because your script is a client.**

| Value            | Whose is it                                     | Where to get it                                        |
|------------------|-------------------------------------------------|--------------------------------------------------------|
| `tenant_id`      | the directory (Leeds, same for all)             | any registration's Overview → Directory (tenant) ID    |
| `client_id`      | your script's client registration (the caller)  | that registration's Overview → Application (client) ID |
| `client_secret`  | your script's client registration               | that registration → Certificates & secrets             |
| scope / resource | the Preservation API (the thing you're calling) | `api://<Preservation-API-client-id>/.default`          |

You need a client registration that (a) you can put a secret on and (b) is allowed to call the Preservation API. Two options:

**Option A** — dedicated registration for scripts (recommended). Create a new App registration, e.g. Library-Preservation-AdminScripts-Dev:

 1. App registrations → + New registration → name it, single tenant.
 2. API permissions → Add a permission → My APIs → Library-Preservation-API-Dev → grant the app permission/role → Grant admin consent.
 3. Certificates & secrets → + New client secret → copy the Value immediately (see below).
 4. Use that registration's client ID + the secret + the API's .default scope.

This keeps your script's credentials separate so you can rotate/revoke them without touching iiif-builder.

**Option B** — reuse an existing client. 

But then we can't distinguish between clients unless they send the "courtesy" (and spoofable) `X-Client-Identity` header. Prefer Option A.

## But ... I can't see ANY app registrations that appear to be clients of the Preservation API apps!

> How do I see the client registrations for Library-Preservation-API-Dev?

It's helpful to reframe the question first: there's no parent/child link in Entra, so you're not looking for "child" registrations of the API. You're asking "which client apps are allowed to — or actually do — call Library-Preservation-API-Dev?" That relationship lives in a few different places, and (importantly) **it may not exist as a tidy list at all**. 

> [!WARNING]  
> There may be no enforced list

A client is "allowed" to call your API in one of two ways:

 - App roles assigned (the proper app-only authorization model), or
 - nothing — if the API defines no app roles and assignment isn't required, then **any client in the tenant** that requests api://…/.default can get a token. There's no per-client grant to enumerate.

The Preservation API only checks "valid token for the audience" (no roles checks):

 1. App registration → Library-Preservation-API-Dev → App roles — are any defined? No: **No app roles have been added**
 2. Enterprise applications → Library-Preservation-API-Dev → Properties → "Assignment required?" — Yes or No? **Yes**

If App roles is empty / Assignment required = No, then there is no allow-list — no per-client authorization.


> [!WARNING]  
> But... "Assignment required?" is set to Yes 

Where to actually look

A. Pre-authorized clients (curated).

App registration → Library-Preservation-API-Dev → Expose an API → Authorized client applications. This lists clients explicitly pre-authorized for its scopes (typically the Web-UI). Often partial or empty.

→ **No client applications have been authorized""

B. App-role assignments (only meaningful if app roles exist).
  
Enterprise applications → Library-Preservation-API-Dev → Users and groups. App-only callers granted an app role show here.

This lists some humans, and two service accounts: a Playwright browser-login account and an iiif-builder account.

C. Who actually calls it — the most useful view. ← start here

Enterprise applications → Library-Preservation-API-Dev → Sign-in logs → Service principal sign-ins tab. Each entry is a token request for this API as the resource, and shows the calling application (client) and its client ID. This gives you the empirical list of machine callers (iiif-builder, Goobi, your script, etc.) regardless of whether app roles exist. The client IDs you see here are the `azp` values that land in the tokens.

> [!WARNING]  
> Sign-in logs is greyed-out; I can't select it. 

> [!NOTE]  
> You must select the API from Enterprise applications, not App registrations — sign-in activity lives on the service principal.

## Current conclusion

The registered apps have:

- Library-Preservation-API-Dev: Application (client) ID = 84c62880...
- Library–Preservation-Web-UI-Dev: Application (client) ID = a616cf42...

My local Python iiif-builder (API client, MSAL) has `PRESERVATION_CLIENT_ID=a616cf42...`

Similarly my local playwright API tests has `API_CLIENT_ID=a616cf42...`

They are both set to the User Interface's client ID, despite the fact they have nothing to do with the UI.

In my development appsettings for the Web UI, I have (values truncated):

```
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "bdeaeda8...",
    "ClientId": "a616cf42...",
    "CallbackPath": "/oauth2/idpresponse",
    "ClientSecret": "<redacted>",
    "ScopeUri": "api://a616cf42.../.default"
  },
```

In my development appsettings for Preservation.API (called by UI and caller of Storage API) I have:

```
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "bdeaeda8...",
    "ClientId": "84c62880...",
    "Audience": "api://a616cf42-...",
    "ClientSecret": "<redacted>"
  },  
```

(Storage API has the same `Audience` value)

This config has revealed what's actually going on.

The whole system is running on a single app registration — the Web‑UI's, `a616cf42...` — used for three different jobs at once. The purpose‑built Library-Preservation-API-Dev registration (`84c62880...`) is essentially vestigial: it's named as the API's ClientId, but nothing actually requests or validates tokens for it.

| Where | Setting | Value | What it means |
|---|---|---|---|
| Web-UI appsettings | `ScopeUri` | `api://a616cf42…/.default` | UI asks Entra for a token for the UI app itself |
| Preservation.API appsettings | `Audience` | `api://a616cf42…` | API only accepts tokens whose `aud` = the UI app |
| Python iiif-builder | `PRESERVATION_CLIENT_ID` | `a616cf42…` | authenticates as the UI app |
| Playwright tests | `API_CLIENT_ID` | `a616cf42…` | authenticates as the UI app |

Every caller requests a token for `api://a616cf42...` (the UI), and the Preservation API validates `Audience = api://a616cf42...` (the UI). So the resource/audience identity for the entire system is the Web‑UI registration, not the API registration. The API's own `ClientId = 84c62880...` is set but never used as an audience by anyone.

And because iiif‑builder and Playwright use `client_id = a616cf42`, they aren't calling as themselves — **they're impersonating the UI app at the Entra level**. They also share the UI's client secret.

All of the callers call as the one same app (`a616cf42...`). They're not distinct tenant apps slipping through — they're literally the same identity wearing different config files. Whether a genuinely different app could mint an `aud=api://a616cf42...` token depends on the assignment/role settings of `a616cf42...` — which you haven't checked. The "Assignment required = Yes" you found was on 84c62880, and that's a red herring: nobody requests 84c62880 as a resource, so its gate never fires. That single mismatch explains all the confusion in section "But… Assignment required is set to Yes" — you were inspecting the wrong app.

Consequences (why this matters)

 1. Every machine caller is cryptographically identical. `azp/appid` = `a616cf42...` for the UI, iiif‑builder, Playwright, and any future Goobi using this pattern. So the "resolve `azp` → friendly name" idea from earlier in the doc won't work as‑is — there's only one `azp`. This is precisely why `X-Client-Identity` is load‑bearing today: it's the only thing that tells your callers apart. (The header section of your doc is correct, but the "robust GetCallerIdentity resolves azp" recommendation is blocked until you split the registrations.)
 2. Shared secret = shared blast radius. One leaked secret compromises every caller; rotating the UI's secret breaks iiif‑builder and the tests simultaneously.
 3. No audience separation anywhere — UI vs API, and (as the review noted) Preservation API vs Storage API all validate the same a616cf42 audience, so a token for one is a token for all.


To reiterate:

What the Audience line actually does

It's the inbound validation rule. On every request, the JWT bearer middleware checks the token's `aud` claim and accepts it only if `aud == api://a616cf42...`. That one line is what decides which tokens the API will accept. So yes, for "who gets in," that line is doing the work.

Is 84c62880 (the API's ClientId) irrelevant? For this, yes.

Normally, if you don't set Audience, Microsoft.Identity.Web defaults the accepted audience to the API's own ClientId (api://{ClientId}). By setting Audience explicitly to a616cf42, you override that default — so 84c62880 is not used for inbound validation at all. For "who can call me," it's irrelevant, exactly as you said.

(The only place `84c62880...` + its secret could matter is if the Preservation API acted as a client to call something downstream — but the Preservation→Storage hop just forwards the caller's existing token rather than minting a new one, so that registration is largely dormant. It's wired up, not exercised.)

The `Audience` line alone doesn't say who may obtain such a token — it only says which audience the API accepts. The other part is enforced on the Entra side, by the assignment gate on a616cf42:

 - appsettings (Audience) says: "I accept tokens for `a616cf42`."
 - Entra (Assignment required = Yes + only `a616cf42` self-assigned) says: "Only `a616cf42` can obtain an app-only token for `a616cf42`."

Put together: to call the API as a machine, you must be able to mint an `a616cf42`-audience token, and the only way to do that is to hold a616cf42's client ID + secret. Which is "whoever possesses my shared secret" — the blast-radius / indistinguishability problem the RFC is about. It's not "anyone in the tenant"; it's "anyone holding the one shared credential."

Humans don't hold the secret. They get in through the delegated path — they're on a616cf42's assignment list (Users and Groups in the Enterprise apps view of a616cf42/Library–Preservation-Web-UI-Dev) and sign in interactively via the UI, which holds the secret and brokers the token for them. So "auth as me (id/secret)" describes the machine callers; humans come through a separate, also-gated door.

A token minted for `a616cf42` is equally valid at Storage — which is why there's no audience separation between the two APIs, and why Preservation can forward the caller's token straight through.

The one-line mental model — "the Audience line means whoever can authenticate as `a616cf42` can call me" — is correct, as long as you remember it's Audience (the API's accept rule) plus Entra's assignment gate (the obtain rule) working together, and that "authenticate as a616cf42" means "hold its shared secret."