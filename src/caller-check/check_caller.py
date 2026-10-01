"""
Verify a per-caller Entra registration end to end (RFC-0001 Phase 1+).

Built for the first Goobi handover and reusable for every caller after it: run this the moment
an administrator hands over a client id and secret, BEFORE any integrator receives them. Stages,
each building on the last:

  A  mint    - client-credentials token from Entra (the AADSTS error, if any, says which admin
               step is missing)
  B  claims  - decode the token (no signature check - it is our own, fresh from Entra) and
               assert aud / azp / roles are what the platform will see
  C  whoami  - GET /whoami with ONLY the Bearer token (no X-Client-Identity): how the platform
               resolves this caller. Before the KnownClients config is applied this shows the
               fallback; after it, the caller's profile name.
  D  deposit - (--deposit) create a throwaway deposit and assert the API routed it to the
               expected bucket, then delete it
  E  export  - (--export <path>) export a real Archival Group and assert the export lands in
               the same bucket: the integrator's pull-back scenario, proven. Deletes the
               deposit afterwards; the Archival Group itself is only read.

Read-only against Entra; stages D and E write only deposits, which they delete. Refuses a
PRESERVATION_API containing 'prod' unless --allow-prod is given.

Configuration (environment, or a .env beside this script):
  TENANT_ID, CLIENT_ID, CLIENT_SECRET   the registration under test
  API_APP_ID_URI                        e.g. api://<the API registration's client id>
  PRESERVATION_API                      e.g. https://preservation-api-dev.example
  EXPECTED_BUCKET                       e.g. dev-goobi-deposits (stages D/E)

Usage:
  python check_caller.py                     # stages A-C
  python check_caller.py --deposit           # + D
  python check_caller.py --export library/some-ag --deposit
"""

import argparse
import base64
import json
import os
import sys
import time
import uuid

import requests

def load_env() -> None:
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), ".env")
    if not os.path.exists(path):
        return
    for line in open(path, encoding="utf-8"):
        line = line.strip()
        if line and not line.startswith("#") and "=" in line:
            key, _, value = line.partition("=")
            os.environ.setdefault(key.strip(), value.strip())

def need(name: str) -> str:
    value = os.environ.get(name)
    if not value:
        sys.exit(f"Missing configuration: {name} (environment or .env)")
    return value

def decode_claims(token: str) -> dict:
    payload = token.split(".")[1]
    payload += "=" * (-len(payload) % 4)
    return json.loads(base64.urlsafe_b64decode(payload))

def check(label: str, ok: bool, detail: str) -> bool:
    print(f"  {'PASS' if ok else 'FAIL'}  {label}: {detail}")
    return ok

def get_deposit_bucket(deposit: dict) -> str:
    files = deposit.get("files") or ""
    return files.removeprefix("s3://").split("/", 1)[0]

def delete_deposit(api: str, headers: dict, deposit: dict) -> None:
    slug = deposit["id"].rsplit("/", 1)[-1]
    response = requests.delete(f"{api}/deposits/{slug}", headers=headers, timeout=60)
    print(f"  cleanup: DELETE deposit {slug} -> {response.status_code}"
          + ("" if response.ok else "  (delete it manually)"))

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[1])
    parser.add_argument("--deposit", action="store_true", help="run stage D (create+delete a deposit)")
    parser.add_argument("--export", metavar="AG_PATH", help="run stage E against this Archival Group path")
    parser.add_argument("--allow-prod", action="store_true", help=argparse.SUPPRESS)
    arguments = parser.parse_args()

    load_env()
    tenant = need("TENANT_ID")
    client_id = need("CLIENT_ID")
    secret = need("CLIENT_SECRET")
    resource = need("API_APP_ID_URI").rstrip("/")
    api = need("PRESERVATION_API").rstrip("/")

    if "prod" in api.lower() and not arguments.allow_prod:
        sys.exit(f"PRESERVATION_API looks like production ({api}). Point this at dev, "
                 "or pass --allow-prod if you really mean it.")

    failures = 0

    # --- A: mint -------------------------------------------------------------------------
    print(f"A  minting as {client_id[:8]}… for {resource}/.default")
    response = requests.post(
        f"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token",
        data={"grant_type": "client_credentials", "client_id": client_id,
              "client_secret": secret, "scope": f"{resource}/.default"},
        timeout=60)
    body = response.json()
    if "access_token" not in body:
        code = body.get("error_description", "")[:200]
        print(f"  FAIL  {body.get('error', 'no access_token')}: {code}")
        if "AADSTS501051" in code:
            print("  -> the Preservation.Call application permission is missing or not admin-"
                  "consented (admin doc, step 3). Assignment required is on, so consent IS the gate.")
        elif "AADSTS7000215" in code:
            print("  -> the client secret is wrong (copied the secret ID instead of its Value?)")
        elif "AADSTS700016" in code:
            print("  -> no registration with this client id in this tenant")
        return 1
    token = body["access_token"]
    print(f"  PASS  token issued, expires_in={body.get('expires_in')}")

    # --- B: claims -----------------------------------------------------------------------
    print("B  claims (decoded without validation - this is our own fresh token)")
    claims = decode_claims(token)
    bare = resource.removeprefix("api://")
    failures += not check("aud", claims.get("aud") in (resource, bare),
                          f"{claims.get('aud')!r} (accepting {resource!r} or bare GUID)")
    failures += not check("azp/appid", client_id in (claims.get("azp"), claims.get("appid")),
                          f"azp={claims.get('azp')!r} appid={claims.get('appid')!r}")
    failures += not check("roles", "Preservation.Call" in (claims.get("roles") or []),
                          f"{claims.get('roles')!r}")
    if "idtyp" in claims:
        failures += not check("idtyp", claims["idtyp"] == "app", f"{claims['idtyp']!r}")

    # --- C: whoami -----------------------------------------------------------------------
    print(f"C  GET {api}/whoami  (Bearer only - deliberately NO X-Client-Identity)")
    headers = {"Authorization": f"Bearer {token}"}
    response = requests.get(f"{api}/whoami", headers=headers, timeout=60)
    print(f"  HTTP {response.status_code}: {response.text[:400]}")
    if not response.ok:
        print("  FAIL  the platform rejected the token - if A and B passed, check the API's "
              "accepted audiences include this resource (landing sequence, rung 2)")
        failures += 1

    # --- D: deposit routing ---------------------------------------------------------------
    if arguments.deposit:
        expected = need("EXPECTED_BUCKET")
        print(f"D  create a throwaway deposit; expect bucket {expected}")
        response = requests.post(f"{api}/deposits", headers=headers, json={
            "type": "Deposit", "template": "None",
            "archivalGroup": f"{api}/repository/caller-check/check-{uuid.uuid4().hex[:8]}",
            "archivalGroupName": "caller-check throwaway",
        }, timeout=60)
        if not response.ok:
            print(f"  FAIL  HTTP {response.status_code}: {response.text[:300]}")
            failures += 1
        else:
            deposit = response.json()
            bucket = get_deposit_bucket(deposit)
            failures += not check("deposit bucket", bucket == expected,
                                  f"{deposit.get('files')!r}")
            delete_deposit(api, headers, deposit)

    # --- E: export routing ----------------------------------------------------------------
    if arguments.export:
        expected = need("EXPECTED_BUCKET")
        print(f"E  export {arguments.export!r}; expect bucket {expected}")
        response = requests.post(f"{api}/deposits/export", headers=headers, json={
            "type": "Deposit", "archivalGroup": f"{api}/repository/{arguments.export}",
        }, timeout=60)
        if not response.ok:
            print(f"  FAIL  HTTP {response.status_code}: {response.text[:300]}")
            failures += 1
        else:
            deposit = response.json()
            bucket = get_deposit_bucket(deposit)
            failures += not check("export bucket", bucket == expected,
                                  f"{deposit.get('files')!r}")
            slug = deposit["id"].rsplit("/", 1)[-1]
            while deposit.get("status") == "exporting":
                time.sleep(5)
                deposit = requests.get(f"{api}/deposits/{slug}", headers=headers, timeout=60).json()
                print(f"  …status={deposit.get('status')}")
            failures += not check("export completed", deposit.get("status") == "new",
                                  f"status={deposit.get('status')!r}")
            delete_deposit(api, headers, deposit)

    print(f"\n{'ALL CHECKS PASSED' if failures == 0 else f'{failures} CHECK(S) FAILED'}")
    return 0 if failures == 0 else 1

if __name__ == "__main__":
    sys.exit(main())
