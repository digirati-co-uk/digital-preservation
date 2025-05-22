import datetime
import traceback

import msal

from aiohttp import ClientSession
from logzero import logger

from app import settings
from app.mets_parser.mets_parser import get_mets_wrapper_from_file_like_object, get_mets_wrapper_from_string
from app.result import Result


preservation_confidential_client = msal.ConfidentialClientApplication(
    client_id=settings.PRESERVATION_CLIENT_ID,
    client_credential=settings.PRESERVATION_CLIENT_SECRET,
    authority=settings.PRESERVATION_AUTHORITY_URL
)


def get_preservation_headers():
    result = preservation_confidential_client.acquire_token_silent(settings.PRESERVATION_SCOPE, account=None)
    if not result:
        logger.info("No Preservation auth token exists in cache, fetching a new one from AAD.")
        result = preservation_confidential_client.acquire_token_for_client(scopes=[settings.PRESERVATION_SCOPE])

    if "access_token" in result:
        return {
            "Authorization": f"Bearer {result['access_token']}",
            settings.PRESERVATION_CLIENT_IDENTITY_HEADER: settings.IIIF_BUILDER_IDENTITY
        }

    logger.error("No access token obtained from AAD.")
    return None


def get_verify_ssl(uri):
    return not uri.startswith("https://localhost:")


async def get_activities(stream_uri: str, session: ClientSession, last_event_time: datetime.datetime) -> Result:

    verify_ssl = get_verify_ssl(stream_uri)
    try:
        activities = []
        headers = get_preservation_headers()
        coll_response = await session.get(stream_uri, headers=headers, verify_ssl=verify_ssl)
        coll = await coll_response.json()
        page_uri = coll.get("last", {}).get("id", None)
        while page_uri is not None:
            page_response = await session.get(page_uri, headers=headers, verify_ssl=verify_ssl)
            page = await page_response.json()
            ordered_items = page.get("orderedItems", [])
            for activity in reversed(ordered_items):
                end_time = activity.get("endTime", None)
                if end_time is None: continue
                end_time_date = datetime.datetime.fromisoformat(end_time)
                if end_time_date > last_event_time:
                    activities.append(activity)
                else:
                    break
            page_uri = page.get("prev", {}).get("id", None)

        return Result.success(activities)

    except Exception as e:
        et = traceback.format_exc()
        logger.error(f"Error getting activities (traceback): {et}")
        logger.error(f"Error getting activities: {repr(e)}")
        return Result(False, "Unable to get activities")


async def load_archival_group(session: ClientSession, archival_group_uri: str) -> Result:

    verify_ssl = get_verify_ssl(archival_group_uri)
    try:
        ag_response = await session.get(archival_group_uri, headers=get_preservation_headers(), verify_ssl=verify_ssl)
        ag = await ag_response.json()
        return Result.success(ag)
    except Exception as e:
        logger.error(f"Error getting archival group: {repr(e)}")
        return Result(False, "Unable to load Archival Group")


async def load_mets(session: ClientSession, archival_group_uri:str) -> Result:

    verify_ssl = get_verify_ssl(archival_group_uri)
    try:
        mets_response = await session.get(f"{archival_group_uri}?view=mets", headers=get_preservation_headers(), verify_ssl=verify_ssl)
        # mets_wrapper = get_mets_wrapper_from_file_like_object(mets_response.content)
        mets_str = await mets_response.text()
        mets_wrapper = get_mets_wrapper_from_string(mets_str)
        return Result.success(mets_wrapper)

    except Exception as e:
        logger.error(f"Error getting mets: {repr(e)}")
        return Result(False, "Unable to load Mets")