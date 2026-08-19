"""
The Preservation API, as this tool uses it.

Everything the migration does to a preserved object goes through here. Nothing writes to S3, to
Fedora or to OCFL directly - not because it would be hard, but because Fedora owns the OCFL writes
and an object changed behind its back would leave its index disagreeing with storage. The same
reasoning keeps the METS rewriting in MetsManager rather than in this repository: one implementation
of how an ID is spelt, not two.
"""

import time
from typing import Any

import msal
import requests
from logzero import logger

from app import settings

_confidential_client = None


class ApiError(RuntimeError):
    """A request the API refused. Carries enough to say what happened without a traceback."""

    def __init__(self, what: str, response: requests.Response):
        detail = response.text[:500] if response.text else ""
        super().__init__(f"{what}: HTTP {response.status_code} {detail}")
        self.status_code = response.status_code


def _headers() -> dict[str, str]:
    headers = {"X-Client-Identity": settings.CLIENT_IDENTITY, "Accept": "application/json"}
    if settings.DISABLE_AUTH:
        return headers

    global _confidential_client
    if _confidential_client is None:
        _confidential_client = msal.ConfidentialClientApplication(
            client_id=settings.PRESERVATION_CLIENT_ID,
            client_credential=settings.PRESERVATION_CLIENT_SECRET,
            authority=settings.PRESERVATION_AUTHORITY_URL)

    token = _confidential_client.acquire_token_silent(settings.PRESERVATION_SCOPE, account=None)
    if not token:
        token = _confidential_client.acquire_token_for_client(scopes=[settings.PRESERVATION_SCOPE])
    if "access_token" not in token:
        raise RuntimeError(f"No access token from AAD: {token.get('error_description', token)}")
    headers["Authorization"] = f"Bearer {token['access_token']}"
    return headers


def _url(path: str) -> str:
    return f"{settings.PRESERVATION_API}/{path.lstrip('/')}"


def _request(method: str, path: str, what: str, **kwargs) -> requests.Response:
    response = requests.request(
        method, _url(path), headers=_headers(), timeout=settings.HTTP_TIMEOUT_SECONDS, **kwargs)
    if not response.ok:
        raise ApiError(what, response)
    return response


# ---------------------------------------------------------------------------
# Reading
# ---------------------------------------------------------------------------

def list_deposits(page: int, page_size: int) -> dict[str, Any]:
    """
    One page of deposits, oldest first.

    Ordered by creation ascending so that paging is stable while the platform is in use: a deposit
    created during the walk sorts after everything already seen, so it cannot shift a later page
    underneath us. Archived deposits are included - an Archival Group is a candidate whether or not
    its deposit was tidied away afterwards.
    """
    response = _request("GET", "/deposits", "Could not list deposits", params={
        "ShowAll": "true",
        "Archived": "true",
        "OrderBy": "Created",
        "Ascending": "true",
        "Page": page,
        "PageSize": page_size,
    })
    return response.json()


def get_archival_group_mets(path_under_root: str) -> bytes:
    """The Archival Group's current METS, as bytes. 404 if it has none."""
    response = _request(
        "GET", f"/repository/{path_under_root.lstrip('/')}",
        f"Could not read METS for {path_under_root}", params={"view": "mets"})
    return response.content


def activity_page(page_number: int) -> dict[str, Any]:
    """One page of the Archival Group Activity Stream, oldest first."""
    response = _request("GET", f"/activity/archivalgroups/pages/{page_number}",
                        f"Could not read activity stream page {page_number}")
    return response.json()


def get_archival_group(path_under_root: str) -> dict[str, Any]:
    """The Archival Group resource, including its version and its binaries."""
    response = _request(
        "GET", f"/repository/{path_under_root.lstrip('/')}",
        f"Could not read Archival Group {path_under_root}")
    return response.json()


# ---------------------------------------------------------------------------
# Writing
# ---------------------------------------------------------------------------

def create_deposit(archival_group_path: str) -> dict[str, Any]:
    """
    A deposit against an existing Archival Group, WITHOUT export.

    This is the whole reason the migration is cheap. An export copies every binary in the Archival
    Group into the deposit's area in S3; a plain deposit against an existing group copies only the
    METS (CreateDepositBase.EnsureMets takes the ExportArchivalGroupMetsOnly branch). The diff still
    comes out right, because the deposit's combined tree gets every other file from the METS, with
    the digests the Archival Group already holds - so nothing but the METS can appear in it.
    """
    body = {
        "type": "Deposit",
        "archivalGroup": _url(f"/repository/{archival_group_path.lstrip('/')}"),
        "submissionText": settings.SUBMISSION_TEXT,
        "template": "RootLevel",
    }
    response = _request("POST", "/deposits", f"Could not create deposit for {archival_group_path}",
                        json=body)
    return response.json()


def normalise_mets_ids(deposit_id: str) -> dict[str, Any]:
    """
    Rewrite the deposit's METS IDs. Returns the report; `changed` false means it wrote nothing.
    Requires FeatureFlags:EnableMetsIdNormalisation on the Preservation API.
    """
    response = _request("POST", f"/deposits/{deposit_id}/mets/normalise",
                        f"Could not normalise METS IDs for deposit {deposit_id}")
    return response.json()


def get_diff_import_job(deposit_id: str) -> dict[str, Any]:
    response = _request("GET", f"/deposits/{deposit_id}/importjobs/diff",
                        f"Could not get diff import job for deposit {deposit_id}")
    return response.json()


def execute_import_job(deposit_id: str, import_job: dict[str, Any]) -> dict[str, Any]:
    response = _request("POST", f"/deposits/{deposit_id}/importjobs",
                        f"Could not execute import job for deposit {deposit_id}", json=import_job)
    return response.json()


def get_import_job_result(deposit_id: str, import_job_result_id: str) -> dict[str, Any]:
    response = _request("GET", f"/deposits/{deposit_id}/importjobs/results/{import_job_result_id}",
                        f"Could not read import job result {import_job_result_id}")
    return response.json()


def await_import_job(deposit_id: str, import_job_result: dict[str, Any]) -> dict[str, Any]:
    """Poll until the job finishes, or the timeout expires. Returns the final result."""
    result_id = import_job_result["id"].rstrip("/").rsplit("/", 1)[-1]
    deadline = time.monotonic() + settings.IMPORT_JOB_TIMEOUT_SECONDS
    while True:
        status = import_job_result.get("status")
        if status in ("completed", "completedWithErrors"):
            return import_job_result
        if time.monotonic() > deadline:
            raise TimeoutError(
                f"Import job {result_id} still '{status}' after "
                f"{settings.IMPORT_JOB_TIMEOUT_SECONDS}s")
        time.sleep(settings.IMPORT_JOB_POLL_SECONDS)
        import_job_result = get_import_job_result(deposit_id, result_id)


def delete_deposit(deposit_id: str) -> None:
    """
    Remove a deposit. Only ever used on deposits this tool created, and only once its work is done
    or has been abandoned - the Archival Group and its OCFL versions are untouched by this.
    """
    try:
        _request("DELETE", f"/deposits/{deposit_id}", f"Could not delete deposit {deposit_id}")
    except ApiError as error:
        # Worth a line, not worth failing a migration that has already been preserved.
        logger.warning("Could not delete deposit %s: %s", deposit_id, error)


def slug(uri: str) -> str:
    """The last path segment of a URI - a deposit or Archival Group id."""
    return uri.rstrip("/").rsplit("/", 1)[-1]
