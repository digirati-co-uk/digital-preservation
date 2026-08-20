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

#: One session for the whole run, so a 100,000-group walk reuses pooled connections instead of
#: paying TCP and TLS setup on every request. The retry loop stays hand-rolled rather than moving
#: to urllib3's Retry: ours is GET-only by construction (a retried POST could preserve a version
#: twice) and logs each attempt, which is most of why it exists.
_session = requests.Session()


#: Statuses worth another try on a GET: the request never reached the application (a gateway
#: answered for it), or the application explicitly asked us to come back.
_RETRYABLE_STATUSES = frozenset({429, 502, 503, 504})


class ApiError(RuntimeError):
    """
    A request that failed - refused by the API, or never completing at all. The message carries
    what was being attempted, the method and URL, and what came back, so that one log line is
    enough to investigate with; rerunning the command is the retry.
    """

    def __init__(self, what: str, detail: str, status_code: int | None = None):
        super().__init__(f"{what}: {detail}")
        #: The HTTP status, or None when there was no response at all (timeout, connection lost).
        self.status_code = status_code


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
    """
    One request, with retries where a retry is safe.

    Only a GET is retried. A GET can be repeated freely; a timed-out POST may have been processed
    despite the timeout, and repeating it could create a second deposit or execute an import job
    twice. So a POST that fails is reported once, with everything needed to investigate before
    anyone tries it again - which for every POST this tool makes is a matter of reading the ledger
    and the deposit, not of guessing.
    """
    url = _url(path)
    headers_extra = kwargs.pop("headers_extra", None)
    attempts = 1 + (settings.HTTP_RETRIES if method == "GET" else 0)
    for attempt in range(1, attempts + 1):
        headers = _headers()
        if headers_extra:
            headers.update(headers_extra)
        try:
            response = _session.request(
                method, url, headers=headers, timeout=settings.HTTP_TIMEOUT_SECONDS, **kwargs)
        except requests.RequestException as error:
            # No response at all: a timeout, a dropped connection, DNS. Transient more often than
            # not, so say exactly what was happening and (on a GET) try again.
            failure = f"{type(error).__name__}: {error}"
            if attempt < attempts:
                _wait_to_retry(what, method, url, failure, attempt, attempts)
                continue
            raise ApiError(what, f"{failure} ({method} {url})") from error

        if response.status_code in _RETRYABLE_STATUSES and attempt < attempts:
            _wait_to_retry(what, method, url, f"HTTP {response.status_code}", attempt, attempts)
            continue
        if not response.ok:
            detail = f"HTTP {response.status_code} ({method} {url})"
            if response.text:
                detail += f" {response.text[:500]}"
            raise ApiError(what, detail, response.status_code)
        return response

    raise AssertionError("unreachable: the loop either returns or raises")


def _wait_to_retry(what: str, method: str, url: str, failure: str, attempt: int, attempts: int) -> None:
    logger.warning("%s: %s (%s %s) - attempt %s of %s failed, retrying in %ss",
                   what, failure, method, url, attempt, attempts,
                   settings.HTTP_RETRY_PAUSE_SECONDS)
    time.sleep(settings.HTTP_RETRY_PAUSE_SECONDS)


# ---------------------------------------------------------------------------
# Reading
# ---------------------------------------------------------------------------

def list_deposits(page: int, page_size: int, newest_first: bool = False,
                  created_after: str | None = None) -> dict[str, Any]:
    """
    One page of deposits.

    Ordered by creation ASCENDING by default, so that paging is stable while the platform is in use:
    a deposit created during the walk sorts after everything already seen, so it cannot shift a later
    page underneath us. `newest_first` reverses that and gives up the guarantee - it is for sampling
    a live system, not for a campaign that has to see everything.

    `created_after` narrows to deposits created at or after that moment, server-side - the survey's
    resume cursor. The comparison is inclusive, so the boundary deposit comes back again; the caller
    already knows its path, and paying one duplicate row beats losing one to a fencepost.

    Deliberately no Archived parameter: it is a filter, not an include - Archived=true returns ONLY
    archived deposits (this tool's original mistake, which silently hid every Archival Group whose
    deposits had not yet been tidied away). Omitting it, with ShowAll for the active/inactive axis,
    is what actually means "every deposit there has ever been".
    """
    params = {
        "ShowAll": "true",
        "OrderBy": "Created",
        "Ascending": "false" if newest_first else "true",
        "Page": page,
        "PageSize": page_size,
    }
    if created_after:
        params["CreatedAfter"] = created_after
    response = _request("GET", "/deposits", "Could not list deposits", params=params)
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


def normalise_mets_ids(deposit_id: str, mets_etag: str | None = None) -> dict[str, Any]:
    """
    Rewrite the deposit's METS IDs. Returns the report; `changed` false means it wrote nothing.
    Requires FeatureFlags:EnableMetsIdNormalisation on the Preservation API.

    `mets_etag` is sent as If-Match when given: the API then answers 409 instead of overwriting a
    METS that changed since we looked - which for this tool means since the deposit was created.
    """
    kwargs: dict[str, Any] = {}
    if mets_etag:
        kwargs["headers_extra"] = {"If-Match": mets_etag}
    response = _request("POST", f"/deposits/{deposit_id}/mets/normalise",
                        f"Could not normalise METS IDs for deposit {deposit_id}", **kwargs)
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
