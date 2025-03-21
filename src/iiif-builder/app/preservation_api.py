import datetime

from aiohttp import ClientSession

from app.iiif_builder import get_preservation_headers
from app.mets_parser.mets_parser import get_mets_wrapper_from_file_like_object
from app.result import Result


async def get_activities(stream_uri: str, session: ClientSession, last_event_time: datetime.datetime) -> Result:

    # TODO: No Error Handling!
    activities = []
    coll_response = await session.get(stream_uri, headers=get_preservation_headers())
    coll = await coll_response.json()
    page_uri = coll.get("last", {}).get("id", None)
    while page_uri is not None:
        page_response = await session.get(page_uri)
        page = await page_response.json()
        ordered_items = page.get("orderedItems", [])
        for activity in reversed(ordered_items):
            end_time = activity.get("endTime", None)
            if end_time is None: continue
            if end_time > last_event_time:
                activities.append(activity)
            else:
                break
        page_uri = page.get("prev", {}).get("id", None)

    return Result.success(activities)


async def load_archival_group(session: ClientSession, archival_group_uri: str) -> Result:

    # TODO: No Error Handling!
    ag_response = await session.get(archival_group_uri, headers=get_preservation_headers())
    ag = await ag_response.json()
    return Result.success(ag)


async def load_mets(session: ClientSession, archival_group_uri:str) -> Result:

    # TODO: No Error Handling!
    mets_response = await session.get(f"{archival_group_uri}?view=mets", headers=get_preservation_headers())
    mets_wrapper = get_mets_wrapper_from_file_like_object(mets_response.content)
    return Result.success(mets_wrapper)