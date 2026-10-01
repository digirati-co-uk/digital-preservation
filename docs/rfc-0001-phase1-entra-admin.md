# RFC-0001 Phase 1 — Entra admin instructions: the Goobi registration (dev)

**For:** an administrator with rights to create app registrations in the Leeds Entra tenant —
the Part 3 "we will come back to you" follow-up promised in
[`rfc-0001-phase0-entra-admin.md`](./rfc-0001-phase0-entra-admin.md), whose Part 1 you have
already completed (2026-09-23).
**Registration this grants access to:** `Library-Preservation-API-Dev` (Application ID begins
`84c62880`) — you do not edit it this time, only point a new registration at it.
**Why now:** Goobi (intranda's system) is the first external caller with its own identity; its
integration is blocked on these credentials existing.

## TL;DR

Create one new app registration for Goobi, give it a client secret, grant it the
`Preservation.Call` **application** permission on the Preservation API with admin consent, and
hand the three values (tenant ID, new Application ID, secret) to the DLIP team — the secret via
a secure channel. Fifteen minutes, nothing existing is modified.

## 1 — Create the registration

Entra admin centre → **App registrations** → **New registration**:

| Field | Value |
|---|---|
| Name | `Library-Preservation-Goobi-Dev` |
| Supported account types | **Accounts in this organizational directory only** (single tenant) |
| Redirect URI | **leave empty** — this is a machine caller; it never signs a person in |

Register, and note the new **Application (client) ID** from its Overview page.

## 2 — Create a client secret

On the new registration: **Certificates & secrets** → **Client secrets** → **New client secret**.
Description `goobi-dev`, expiry **12 months** (add a calendar reminder to rotate — when it
expires, Goobi's access stops with it, which is the per-caller revocability this design exists
for). Copy the secret **Value** immediately; it is shown once.

## 3 — Grant `Preservation.Call` with admin consent

Still on the new registration: **API permissions** → **Add a permission** → **My APIs** →
`Library-Preservation-API-Dev` → **Application permissions** → tick **`Preservation.Call`** (you
created this app role in Phase 0, step 1.1) → **Add permissions** → then press **Grant admin
consent for [tenant]** and confirm.

This step is the one that matters most, and must be complete **before** Goobi ever requests a
token: `Library-Preservation-API-Dev`'s enterprise application has `Assignment required = Yes`
(you verified this on 2026-09-23), so an unconsented caller is refused by Entra itself with
`AADSTS501051`. Consent is what creates the assignment.

## 4 — Hand over

To the DLIP engineering team:

- the **Application (client) ID** of the new registration (not sensitive);
- the **client secret value** — via a secure channel (password vault / your agreed secrets
  route), never email or chat;
- (they already have the tenant ID).

The DLIP team will test the credentials themselves first (`src/caller-check` in this
repository mints a token, checks its claims, and proves deposit and export routing); intranda
receive them only after the end-to-end path is proven.

## Do NOT

- ❌ Do **not** add a redirect URI, platform configuration, or any **delegated** permissions to
  the new registration — it is purely a client-credentials machine caller.
- ❌ Do **not** edit `Library-Preservation-API-Dev` (`84c62880`) or `Library–Preservation-Web-UI-Dev`
  (`a616cf42`) — no manifest changes, no property changes, no assignment removals. Phase 0's
  state is load-bearing and complete.
- ❌ Do **not** add the new registration to any user group (it is not a user), and do not assign
  it anything on the Web-UI app.
- ❌ Do **not** create test/prod Goobi registrations yet — dev first, end to end; the other
  environments follow their own Phase 0.

## Self-check

On **Enterprise applications** → `Library-Preservation-API-Dev` → **Users and groups**: a new
row for `Library-Preservation-Goobi-Dev` (object type *Service principal*) with role
`Preservation.Call`. That row is the whole point — it is what lets Entra, and therefore the
platform, know cryptographically that a call came from Goobi and nobody else.
