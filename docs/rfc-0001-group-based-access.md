# RFC-0001: group-based access for UI users (do-ahead work)

**For:** the Leeds Entra administrator, with the platform team CC'd.
**When:** any time from now — every step here is safe ahead of the migration and changes nothing
observable today.
**Companions:** [`rfc-0001-api-caller-identity.md`](./rfc-0001-api-caller-identity.md) (the why),
[`rfc-0001-landing-sequence.md`](./rfc-0001-landing-sequence.md) (the when),
[`rfc-0001-phase0-entra-admin.md`](./rfc-0001-phase0-entra-admin.md) (the registration-side steps,
already done for dev).

## TL;DR

Create one security group per environment for the people who use the Preservation UI, and assign
that group to **two** enterprise applications: the UI app (today's sign-in gate) and the API app
(the gate that starts firing at Phase 3). Membership is then maintained in exactly one place, and
the migration's hard precondition — *every UI user must be assigned on the API app before the UI
is repointed, or they are locked out with `AADSTS50105`* — becomes true by construction instead of
by bookkeeping.

## The model in one table

| | Today | After Phase 3 |
|---|---|---|
| UI app (`a616cf42…`) assignment | **The only human gate.** Governs UI sign-in; every human API call, direct or indirect, rides a token this gate issued | Governs UI sign-in only |
| API app (`84c62880…`) assignment | **Dormant.** Nothing requests this resource yet, so its `Assignment required = Yes` never fires; the six existing assignments are seeds, not access | **The master gate.** Governs whether a user can hold an API token at all, through any client |
| Relationship | independent | API list must be a **superset** of active UI users — a user on the UI list but not the API list signs in fine and then every call fails |

One group assigned to both apps makes the superset constraint unbreakable for ordinary users.

## Part 1 — create the group(s)

1. Entra admin centre → **Groups** → New group. Type: **Security**. Suggested name:
   `Library-Preservation-UI-Users-Dev` (repeat per environment — see the note below).
2. Add the human users as **members**. Work from a list of **UPNs, never display names**: this
   tenant's test and service accounts wear real-person display names over cryptic UPNs, so a
   membership list built from the portal's visible names will be wrong in both directions. The
   current direct assignments on the UI app's *Users and groups* blade are the starting
   population — click each entry to read its UPN.
3. Members must be **direct**. Entra does not honour nested groups for application assignment: a
   group inside the group contributes nothing. If an existing departmental group looks tempting,
   its members must be added to this group individually (or by script), not by nesting.

**Per-environment or one group?** All three environments live in the same tenant, so a single
group would technically serve all of them. Recommended anyway: one group per environment, because
the populations will almost certainly diverge (production access tighter than dev). If Leeds
prefers a single group for all three, nothing here breaks — it is an access-policy choice, not a
technical one.

**Licensing:** assigning *groups* (rather than individual users) to enterprise applications
requires Entra ID P1, which the university's academic agreement will almost certainly include —
confirm before scheduling, not after starting.

## Part 2 — assign the group to both enterprise applications

For **dev**, both apps exist now:

1. Enterprise applications → `Library–Preservation-Web-UI-Dev` (Application ID begins
   `a616cf42`) → **Users and groups** → **Add user/group** → select the group → role: **Default
   Access** → Assign.
2. Enterprise applications → `Library-Preservation-API-Dev` (Application ID begins `84c62880`) →
   same steps, same **Default Access** role.

Default Access is correct in both places. On the API app, the assignment itself is what satisfies
the sign-in/token gate; the humans' actual permission arrives later as the delegated scope
(Phase 3). The `Preservation.Call` *app role* is for machine callers and is assigned through the
separate Phase 1/2 steps — never to this group.

For **test** and **production**: the groups can be created and populated now, but the app
assignments wait until those environments' registrations have had their own Phase 0 pass (see the
ladder status table in the landing sequence — Phase 0 is currently done for dev only).

## Part 3 — verify, then retire the direct assignments

1. After assigning the group, confirm a group member can still sign in to the dev UI. Group and
   direct assignments coexist (access is the union), so this step risks nothing.
2. Once verified, the direct **human** assignments on the UI app can be removed, leaving the
   group as the single source of truth. Remove people one at a time, checking sign-in still works
   for a group member after the first removal.
3. **Leave untouched**: the app's own service-principal self-assignment (the "linchpin" entry —
   removing it breaks every machine caller today, see RFC §3.1), and the `zz_*` service accounts
   unless Leeds deliberately decides to move them into the group (they are accounts like the
   Playwright browser-login and iiif-builder identities; keeping them as direct assignments keeps
   them visible as special).

## Do NOT

- ❌ Do not change **Assignment required** on either app (both are already `Yes`, deliberately).
- ❌ Do not remove the service-principal self-assignment from the UI app (see Part 3).
- ❌ Do not put machine identities or app registrations into the UI-users group. Machine access
  is governed by `Preservation.Call` role assignments and the platform's `KnownClients` list
  (RFC §6, Phases 1–2), not by user assignment.
- ❌ Do not remove the six existing assignments on the API app — they are the start of the
  Phase 3 list (Phase 0 admin doc, §1.5). They become redundant once the group is assigned and
  can be tidied then.

## End-state question: can a UI user bypass the UI and call the API directly?

Short answer: **not without the platform team and the administrator both enabling it.** Three
independent controls stack up:

1. **The browser never holds an API token.** The UI is a server-side confidential client: it
   acquires API tokens in its backend (`EnableTokenAcquisitionToCallDownstreamApi`, server-side
   token cache) and the browser carries only the UI's own sign-in cookie. There is no token in
   dev tools to copy into `curl`.
2. **Minting a delegated API token requires a client application** — a tenant registration with
   the API's delegated scope granted and admin-consented, plus a flow a human can drive (device
   code, etc.). Being a "human with an account" is not enough; being assigned on the API's
   enterprise app governs whether a token *may be issued for you*, not how you would obtain one.
   If the UI is the only client with that consent, the UI is the only door. This is ordinary
   admin-consent governance and stays in Leeds' hands.
3. **Phase 4 closes the loop in the platform itself**: the API resolves every caller from the
   token's signed `azp` (which client requested it) and rejects unknown clients — so even a
   validly-minted token from some newly-consented client is refused until that client is
   deliberately added to the platform's `KnownClients` configuration.

So the API-side group assignment is *necessary* for UI users but not *sufficient* for direct
access — which is exactly the intended shape: user population managed by Leeds in one group;
client population managed explicitly, one registration at a time, by consent plus configuration.

## Self-check

After Part 2 on dev: the group appears on both apps' **Users and groups** blades with role
Default Access; a member can sign in to the dev UI; and (nothing else changes) machine callers
and the Playwright suite behave exactly as before, because nothing here touches service
principals, roles, or registrations.
