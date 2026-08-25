import os

from dotenv import load_dotenv

load_dotenv()

#: Root of the Preservation API, e.g. https://dlip-pres-preservation.example.ac.uk
PRESERVATION_API = os.environ.get("PRESERVATION_API", "").rstrip("/")

# OAuth2 (MS flavoured) settings for calling Preservation API, as iiif-builder uses.
PRESERVATION_CLIENT_ID = os.environ.get("PRESERVATION_CLIENT_ID")
PRESERVATION_CLIENT_SECRET = os.environ.get("PRESERVATION_CLIENT_SECRET")
PRESERVATION_TENANT_ID = os.environ.get("PRESERVATION_TENANT_ID")
PRESERVATION_SCOPE = f"api://{PRESERVATION_CLIENT_ID}/.default"
PRESERVATION_AUTHORITY_URL = f"https://login.microsoftonline.com/{PRESERVATION_TENANT_ID}"

#: Set when the API has auth disabled (FeatureFlags:DisableAuth), as it is locally.
DISABLE_AUTH = os.environ.get("DISABLE_AUTH", "false").lower() == "true"

#: Sent as X-Client-Identity, and so recorded as the deposit's createdBy and preservedBy. Giving
#: the migration its own identity is what lets a later reader tell a migration version from one a
#: person made, using nothing but the API.
CLIENT_IDENTITY = os.environ.get("CLIENT_IDENTITY", "mets-id-migration")

#: Goes into every deposit's submissionText, so the whole campaign is queryable afterwards through
#: the deposits endpoint.
SUBMISSION_TEXT = os.environ.get("SUBMISSION_TEXT", "METS ID normalisation (issue #188 step 3)")

#: Where the ledger lives. One file, safe to copy, and the record of what was done. One per
#: deployment: it is bound to the PRESERVATION_API it was first opened against and refuses any
#: other, because its rows are keyed on Archival Group paths, which dev and prod have in common.
LEDGER_PATH = os.environ.get("LEDGER_PATH", "ledger.sqlite")

#: Root of the UI, if there is one. Only used to turn the candidate list into links someone can
#: follow, for the case where the list turns out to be short enough to work through by hand.
UI_BASE_URL = os.environ.get("UI_BASE_URL", "").rstrip("/")

#: How long to wait between finishing one Archival Group and starting the next. Every migration
#: emits an import job and a (suppressed) event, and the point of pacing is to leave the platform
#: room for the people using it.
PAUSE_SECONDS = float(os.environ.get("PAUSE_SECONDS", "1.0"))

#: The same, for the survey, which is read-only and so unpaced by default. Reading one Archival
#: Group is still several Fedora requests - a HEAD, the version list, and one per container in the
#: tree - and the survey makes them back to back for as long as it runs. On a system nobody else is
#: using that is fine; against production, set this (or pass --pause) so the walk leaves gaps.
SURVEY_PAUSE_SECONDS = float(os.environ.get("SURVEY_PAUSE_SECONDS", "0"))

#: Depositor identities (comma-separated; bare id or agent URI) whose Archival Groups the survey
#: records as skipped-by-depositor WITHOUT reading their METS. The lever that makes a very large
#: deployment surveyable: production's ~100,000 EPrints groups were deposited by
#: eprints-migration-app and their METS is EPrints' own, which was never affected by #188. It is a
#: heuristic about the depositor rather than a verdict about the document - `verify-skipped`
#: samples the skipped rows to check it held. Empty by default: nothing is skipped unless asked.
SKIP_CREATED_BY = [value.strip() for value in os.environ.get("SKIP_CREATED_BY", "").split(",")
                   if value.strip()]

#: How long to wait for one import job to finish before giving up on it.
IMPORT_JOB_TIMEOUT_SECONDS = float(os.environ.get("IMPORT_JOB_TIMEOUT_SECONDS", "600"))
IMPORT_JOB_POLL_SECONDS = float(os.environ.get("IMPORT_JOB_POLL_SECONDS", "5"))

#: Page size for the deposits query used to discover candidates.
DEPOSIT_PAGE_SIZE = int(os.environ.get("DEPOSIT_PAGE_SIZE", "100"))

#: Keep the migration out of the published Activity Stream. On by default because that is the
#: whole point: renaming identifiers inside a METS gives the stream's readers nothing to rebuild.
#: Turn it off only to watch one migration travel the normal path end to end.
SUPPRESS_ACTIVITY_STREAM_EVENT = \
    os.environ.get("SUPPRESS_ACTIVITY_STREAM_EVENT", "true").lower() == "true"

#: Requests timeout, seconds. Exporting a METS can take a moment on a large Archival Group.
HTTP_TIMEOUT_SECONDS = float(os.environ.get("HTTP_TIMEOUT_SECONDS", "120"))

#: How many extra tries a GET gets when it times out or hits a gateway error (502/503/504/429),
#: and how long to wait between tries. Only GETs are retried - they are safe to repeat, where a
#: retried POST could create a deposit or preserve a version twice.
HTTP_RETRIES = int(os.environ.get("HTTP_RETRIES", "2"))
HTTP_RETRY_PAUSE_SECONDS = float(os.environ.get("HTTP_RETRY_PAUSE_SECONDS", "5"))


def check() -> list[str]:
    """Configuration problems that would make the tool fail later and less clearly."""
    problems = []
    if not PRESERVATION_API:
        problems.append("PRESERVATION_API is not set")
    if not DISABLE_AUTH and not (
            PRESERVATION_CLIENT_ID and PRESERVATION_CLIENT_SECRET and PRESERVATION_TENANT_ID):
        problems.append(
            "PRESERVATION_CLIENT_ID, PRESERVATION_CLIENT_SECRET and PRESERVATION_TENANT_ID are "
            "required unless DISABLE_AUTH is true")
    return problems
