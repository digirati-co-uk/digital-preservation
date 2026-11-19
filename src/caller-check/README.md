# caller-check

Verifies a per-caller Entra registration end to end (RFC-0001 Phase 1 and later), the moment an
administrator hands over a client id and secret — **before** any integrator receives them. Built
for the first Goobi registration; reusable unchanged for every caller and environment after it.

```bash
pip install requests
cp .env.example .env     # fill in the handover values
python check_caller.py                       # A: mint, B: claims, C: /whoami
python check_caller.py --deposit             # + D: deposit routed to the caller's bucket
python check_caller.py --deposit --export library/some-real-ag   # + E: the pull-back scenario
```

Stage A's failure messages decode the common `AADSTS` errors into which admin-doc step was
missed (consent not granted, wrong secret value, wrong tenant). Stage C deliberately sends no
`X-Client-Identity`: it shows what the platform resolves from the signed token alone. Once the
caller's `KnownClients` entry is applied, that's the caller's profile name. Before then, the API
accepts the token but refuses the call (401, "No valid user name or machine header"), and C reports
that as a missing `KnownClients` entry, not a token problem. Stages D
and E create only deposits, and delete them; E's Archival Group is read, never written.

The script refuses any `PRESERVATION_API` that isn't a dev host (`-dev.` in the hostname) or
localhost unless `--allow-non-dev` is passed. It's an allow-list because the production hostname
doesn't contain `prod`. Stage E stops waiting for the export after 20 minutes and leaves that
deposit for you to delete.

`.env` keys: `TENANT_ID`, `CLIENT_ID`, `CLIENT_SECRET`, `API_APP_ID_URI` (e.g.
`api://<the API registration's client id>`), `PRESERVATION_API`, `EXPECTED_BUCKET` (stages D/E).
