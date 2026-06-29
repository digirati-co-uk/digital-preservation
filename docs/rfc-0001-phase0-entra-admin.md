# RFC-0001 Phase 0 — Entra admin instructions

**For:** an administrator with rights to edit app registrations in the **dev** Entra tenant
(Application Administrator / Cloud Application Administrator, or owner of the registration).
**From:** the Digital Preservation dev team.
**Companion doc:** [`rfc-0001-api-caller-identity.md`](./rfc-0001-api-caller-identity.md) — full background. You
do **not** need to read it to do the Phase 0 task below, but §3 explains why this is safe.

## TL;DR

We are moving the preservation APIs from one shared app registration to per-caller identity. **Phase 0
is preparatory and has zero impact on any current caller.** We need you to do **one** thing now that the
dev team lacks permission for: **create an app role on the API registration.** Everything else in this
doc is either "verify only" or "please do NOT do yet."

- **Registration to edit:** `Library-Preservation-API-Dev`, **Application (client) ID `84c62880…`**.
  ⚠️ **Not** the Web-UI registration `a616cf42…` — double-check the client ID before changing anything.

---

## Part 1 — DO NOW (Phase 0)

### 1.1 Verify the Application ID URI (likely already done — just confirm)

Entra admin centre → **Applications → App registrations → `Library-Preservation-API-Dev` → Expose an API**.

- The **Application ID URI** at the top should read **`api://84c62880…`**.
- If it's already set to that: ✅ nothing to do.
- If it's blank: click **Add**, accept the pre-filled default (`api://84c62880…`), **Save**.
- Do **not** add any **Scopes**. (Machine callers use an app role, below — not a delegated scope.)

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

### 1.3 Document the existing enterprise-app assignments (investigate only — do NOT remove)

We noticed the **enterprise application** for `84c62880` (Entra admin centre → **Enterprise applications
→ `Library-Preservation-API-Dev` → Users and groups**) already has assignments: several **human users**
plus the **`zz_libplaywrighttest`** service account.

These are **inert today** (they're users on the default-access role; no machine caller's *app* is
assigned, and nothing requests this audience yet). They appear to be leftovers from an earlier partial
setup. **Please do not delete or change them as part of Phase 0.** Instead, just **send us the list**
(names / object IDs) so we can reconcile it against the intended caller list before the later phases. We
will tell you which, if any, to remove and when.

### 1.4 Confirm the assignment gate is unchanged

`84c62880` already has **Assignment required = Yes** (Enterprise application → **Properties**). **Leave it
as-is.** We are *not* asking you to change it — we only note it so it isn't toggled by accident; it is
already the setting we want for later phases.

---

## Part 2 — Please do NOT do yet

None of the following are part of Phase 0. Doing them early could break current callers:

- ❌ Do **not** assign the new `Preservation.Call` role to any application or user.
- ❌ Do **not** change **Assignment required** on `84c62880` (it stays **Yes**) or on the Web-UI
  registration `a616cf42…`.
- ❌ Do **not** remove or edit the existing enterprise-app assignments (see 1.3).
- ❌ Do **not** touch the Web-UI registration `a616cf42…` at all.
- ❌ Do **not** grant admin consent for anything new.

---

## Part 3 — LATER, on the dev team's request (context only — nothing to do now)

So you can see where this is heading and why ordering matters. We will come back to you for each:

- **Phase 1 — per-caller registrations.** We create one registration per caller (starting with Goobi),
  and ask you to **assign `Preservation.Call`** to each on `84c62880` **with admin consent**.
- **Phase 2 — repoint callers.** Dev-side config change. **Strict ordering:** a caller must already hold
  its `Preservation.Call` assignment (Phase 1) **before** we repoint it, because `Assignment required =
  Yes` is already live on `84c62880` — otherwise Entra rejects the token with `AADSTS501051`.
- **Phase 3 — repoint the UI**, and **Phase 4 — tighten** (we may then ask you to reconcile/remove the
  stray assignments from 1.3). Assignment-required is already on, so there's nothing to enable.

---

## When to do what — summary

| When | Who | Action |
|---|---|---|
| **Now (Phase 0)** | **Admin** | 1.1 verify App ID URI · **1.2 create `Preservation.Call` role** · 1.3 send us the assignment list · 1.4 confirm gate unchanged |
| Now (Phase 0) | Dev team | Ship dual-mode resolution + accept both audiences in config (already done in code) |
| Later, on request | Admin + dev | Phase 1 assign roles → Phase 2 repoint (strictly in that order) → Phases 3–4 |

## How to confirm Phase 0 succeeded

On `84c62880`'s registration → **Manifest**, confirm:
- `"identifierUris": ["api://84c62880…"]`
- an `appRoles` entry with `"value": "Preservation.Call"`, `"allowedMemberTypes": ["Application"]`,
  `"isEnabled": true`.

Then let us know, and send the 1.3 assignment list. That completes the admin side of Phase 0.
