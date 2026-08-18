# RFC-0001 Phase 0 — Entra admin instructions

**For:** an administrator with rights to edit app registrations in the **dev** Entra tenant
(Application Administrator / Cloud Application Administrator, or owner of the registration).
**From:** the Digital Preservation dev team.
**Companion doc:** [`rfc-0001-api-caller-identity.md`](./rfc-0001-api-caller-identity.md) — full background. You
do **not** need to read it to do the Phase 0 task below, but §3 explains why this is safe.

## TL;DR

We are moving the preservation APIs from one shared app registration to per-caller identity. **Phase 0
is preparatory and has zero impact on any current caller.** We need you to do **two** things now that the
dev team lacks permission for: **create an app role on the API registration (1.2), and expose one
delegated scope on it (1.3).** Everything else in this doc is either "verify only" or "please do NOT
do yet."

- **Registration to edit:** `Library-Preservation-API-Dev`, **Application (client) ID `84c62880…`**.
  ⚠️ **Not** the Web-UI registration `a616cf42…` — double-check the client ID before changing anything.

---

## Part 1 — DO NOW (Phase 0)

### 1.1 Verify the Application ID URI (likely already done — just confirm)

Entra admin centre → **Applications → App registrations → `Library-Preservation-API-Dev` → Expose an API**.

- The **Application ID URI** at the top should read **`api://84c62880…`**.
- If it's already set to that: ✅ nothing to do.
- If it's blank: click **Add**, accept the pre-filled default (`api://84c62880…`), **Save**.
- (Machine callers use an app role — 1.2. Human callers via the Web-UI will use the delegated scope
  created in 1.3.)

### 1.2 Create the `Preservation.Call` app role  ← the action we're blocked on

Same registration → **App roles** → **Create app role**:

| Field | Value |
|---|---|
| **Display name** | `Preservation Call` |
| **Allowed member types** | **Applications** ← important: *application* permission, not Users/Groups, not Both |
| **Value** | `Preservation.Call` ← exact string; the API will check this later |
| **Description** | `Caller may call the Preservation API.` |
| **Enable this app role?** | ✅ Yes |

Click **Apply**.

**Why this is safe (no caller impact):** no caller requests `api://84c62880…` as a token audience yet,
so this role is dormant. Creating it does **not** assign it to anyone and does **not** alter any existing
assignment.

### 1.3 Expose the `access_as_user` delegated scope

Same registration → **Expose an API** → **Add a scope**:

| Field | Value |
|---|---|
| **Scope name** | `access_as_user` ← exact string |
| **Who can consent?** | **Admins only** |
| **Admin consent display name** | `Access the Preservation API as the signed-in user` |
| **Admin consent description** | `Allows a client application to call the Preservation API on behalf of the signed-in user.` |
| **State** | ✅ Enabled |

Click **Add scope**.

**Why this is needed:** when the Web-UI is later repointed at this API (Phase 3), it acquires tokens *on
behalf of the signed-in user* — a **delegated** flow, which can only succeed against a consented
delegated scope. The app role from 1.2 covers machine callers only; it is invisible to the delegated
flow. Without this scope, Phase 3 fails at token acquisition.

**Why this is safe (no caller impact):** like the app role, the scope is dormant — no client has been
granted permission to it, no consent has been given, and nothing requests this audience yet.

### 1.4 Document the existing enterprise-app assignments (investigate only — do NOT remove)

We noticed the **enterprise application** for `84c62880` (Entra admin centre → **Enterprise applications
→ `Library-Preservation-API-Dev` → Users and groups**) already has assignments: several **human users**
plus the **`zz_libplaywrighttest`** service account.

These are **inert today** (they're users on the default-access role; no machine caller's *app* is
assigned, and nothing requests this audience yet) — but they are **not junk to be cleared out later**.
Because this app has **Assignment required = Yes**, every Web-UI user will need exactly such an
assignment once the UI is repointed at this API (Phase 3); an unassigned user is rejected with
`AADSTS50105`. These entries are the *beginning* of that required list — at Phase 3 we will ask you to
extend it to **all** UI users (ideally via a group). **Please do not delete or change them.** For now,
just **send us the list** (names / object IDs) so we can reconcile it against the expected UI-user
population before Phase 3.

### 1.5 Confirm the assignment gate is unchanged

`84c62880` already has **Assignment required = Yes** (Enterprise application → **Properties**). **Leave it
as-is.** We are *not* asking you to change it — we only note it so it isn't toggled by accident; it is
already the setting we want for later phases.

---

## Part 2 — Please do NOT do yet

None of the following are part of Phase 0. Doing them early could break current callers:

- ❌ Do **not** assign the new `Preservation.Call` role to any application or user.
- ❌ Do **not** grant any client permission to the new `access_as_user` scope (creating the scope in
  1.3 is Phase 0; granting and consenting it is Phase 3).
- ❌ Do **not** change **Assignment required** on `84c62880` (it stays **Yes**) or on the Web-UI
  registration `a616cf42…`.
- ❌ Do **not** remove or edit the existing enterprise-app assignments (see 1.4).
- ❌ Do **not** touch the Web-UI registration `a616cf42…` at all.
- ❌ Do **not** grant admin consent for anything new.

---

## Part 3 — LATER, on the dev team's request (context only — nothing to do now)

So you can see where this is heading and why ordering matters. We will come back to you for each:

- **Phase 1 — per-caller registrations.** We create one registration per caller (starting with Goobi),
  and ask you to **assign `Preservation.Call`** to each on `84c62880` **with admin consent**.
- **Phase 2 — repoint callers.** Dev-side config change. **Strict ordering:** a caller must already hold
  its `Preservation.Call` assignment (Phase 1) **before** we repoint it, because `Assignment required =
  Yes` is already live on `84c62880` — otherwise Entra rejects the token with `AADSTS501051`. (If the
  dev team takes the interim route of running the platform's *internal* service-to-service calls as
  `84c62880` itself, we will also ask you to assign `Preservation.Call` to `84c62880`'s **own** service
  principal at this point.)
- **Phase 3 — repoint the UI.** Two admin actions **before** the dev-side change: (a) grant the Web-UI
  registration (`a616cf42…`) the `access_as_user` delegated permission on `84c62880` and **admin-consent**
  it; (b) **assign all UI users** (ideally a group, plus the Playwright browser-login account) on
  `84c62880`'s enterprise app — unassigned users are rejected with `AADSTS50105` the moment the UI is
  repointed. The existing user assignments from 1.4 stay and grow; they are required, not stray.
- **Phase 4 — tighten.** Assignment-required is already on, so there's nothing to enable. Any
  reconciliation of assignments here removes only entries that should not call the API — **not** the UI
  user population, which is load-bearing from Phase 3 onward.

---

## When to do what — summary

| When | Who | Action |
|---|---|---|
| **Now (Phase 0)** | **Admin** | 1.1 verify App ID URI · **1.2 create `Preservation.Call` role** · **1.3 expose `access_as_user` scope** · 1.4 send us the assignment list · 1.5 confirm gate unchanged |
| Now (Phase 0) | Dev team | Ship dual-mode resolution + accept both audiences in config (via `TokenValidationParameters:ValidAudiences` — a top-level `Audiences` key does **not** bind; see RFC §6 Phase 0) |
| Later, on request | Admin + dev | Phase 1 assign roles → Phase 2 repoint (strictly in that order) → Phase 3 consent scope + assign UI users, then repoint UI → Phase 4 |

## How to confirm Phase 0 succeeded

On `84c62880`'s registration → **Manifest**, confirm:
- `"identifierUris": ["api://84c62880…"]`
- an `appRoles` entry with `"value": "Preservation.Call"`, `"allowedMemberTypes": ["Application"]`,
  `"isEnabled": true`
- a delegated-scope entry with `"value": "access_as_user"`, enabled (`api.oauth2PermissionScopes` in the
  new manifest format, `oauth2Permissions` in the classic one).

Then let us know, and send the 1.4 assignment list. That completes the admin side of Phase 0.
