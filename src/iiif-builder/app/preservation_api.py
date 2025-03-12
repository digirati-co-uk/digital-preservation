import datetime

import metsrw
from aiohttp import ClientSession
from app.result import Result


async def get_activities(stream_uri: str, session: ClientSession, last_event_time: datetime.datetime) -> Result:

    # TODO: No Auth, No Error Handling!
    activities = []
    coll_response = await session.get(stream_uri)
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

    # TODO: No Auth, No Error Handling!
    ag_response = await session.get(archival_group_uri)
    ag = await ag_response.json()
    return Result.success(ag)


async def load_mets(session: ClientSession, archival_group_uri:str) -> Result:

    # TODO: No Auth, No Error Handling!
    mets_response = await session.get(f"{archival_group_uri}?view=mets")
    mets_xml_string = await mets_response.text()
    mets = metsrw.METSDocument.fromstring(mets_xml_string)
    return Result.success(mets)