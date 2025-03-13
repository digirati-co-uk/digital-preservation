from aiohttp import ClientSession
from logzero import logger

from app import settings
from app.result import Result

headers_show_extras = {
    "Authorization": f"Basic {settings.IIIF_CS_BASIC_CREDENTIALS}",
    "X-IIIF-CS-Show-Extras": "All"
}


async def put_manifest(session: ClientSession, api_manifest_uri:str, manifest) -> Result:

    # TODO: Needs Auth - IIIF-CS API Key
    # This is using the same session as calls to the preservation API and catalogue API, but not
    # calls to ID service which uses the kiota-generated client.
    existing_manifest_response = await session.get(api_manifest_uri, headers=headers_show_extras)
    etag = None
    if existing_manifest_response.status == 404:
        logger.debug(f"Manifest {api_manifest_uri} does not already exist")
    elif existing_manifest_response.status == 200:
        etag = existing_manifest_response.headers["etag"] # check case
        logger.debug(f"Manifest {api_manifest_uri} already exists, etag is {etag}")
        existing_manifest = await existing_manifest_response.json()
        update_ingest_status(existing_manifest, manifest)
    else:
        msg = f"Manifest {api_manifest_uri} returned status {existing_manifest_response.status} - cannot process atm"
        logger.warning(msg)
        return Result(False, msg)

    if etag is None:
        headers = headers_show_extras
    else:
        headers = headers_show_extras.copy()
        headers["If-Match"] = etag

    initial_put_response = await session.put(api_manifest_uri, headers=headers, json=manifest)
    if initial_put_response.status != 202:
        msg = f"PUT to {api_manifest_uri} returned status {initial_put_response.status} - cannot continue"
        logger.warning(msg)
        return Result(False, msg)

    logger.information(f"PUT to {api_manifest_uri} has been sent")
    return Result.success(manifest)

def painted_resources_have_same_asset(p1, p2)->bool:
    return p1["asset"]["id"] == p2["asset"]["id"]


def update_ingest_status(existing_manifest, new_manifest):
    # You could leave the IIIF-CS to decide whether or not to reingest a re-supplied asset.
    # But it plays it quite safe and may reingest things that haven't changed.

    # If an asset is repeated (appears more than once)
    # we only need to tell it to reingest once.
    seen_ids = []
    for new_painted_resource in new_manifest.get("paintedResources", []):
        if new_painted_resource["asset"]["id"] not in seen_ids:
            seen_ids.append(new_painted_resource["asset"]["id"])
            existing_painted_resource = next(filter(painted_resources_have_same_asset, existing_manifest["paintedResources"]), None)

            if existing_painted_resource is None:
                new_painted_resource["reingest"] = True
                continue

            if existing_painted_resource["origin"] != new_painted_resource["origin"]:
                new_painted_resource["reingest"] = True