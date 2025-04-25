import json
import base64

from aiohttp import ClientSession
from logzero import logger

from app import settings
from app.result import Result

headers_show_extras = {
    "Authorization": f"Basic {base64.b64encode(settings.IIIF_CS_BASIC_CREDENTIALS.encode("utf-8")).decode("ascii")}",
    "X-IIIF-CS-Show-Extras": "All"
}


async def put_manifest(session: ClientSession, api_manifest_uri:str, manifest) -> Result:

    logger.info(f"See if a Manifest already exists at {api_manifest_uri}")
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

    logger.info(f"Sending PUT to {api_manifest_uri}")
    initial_put_response = await session.put(api_manifest_uri, headers=headers, json=manifest)
    if not (initial_put_response.status == 202 or initial_put_response.status == 200):
        msg = f"PUT to {api_manifest_uri} returned status {initial_put_response.status} - cannot continue"
        logger.warning(msg)
        logger.debug(json.dumps(manifest, indent=2))
        return Result(False, msg)

    logger.debug(f"PUT to {api_manifest_uri} has been sent")
    return Result.success(manifest)

def painted_resources_have_same_asset(p1, p2)->bool:
    return p1["asset"]["id"] == p2["asset"]["id"]


def update_ingest_status(existing_manifest, new_manifest):
    # You could leave the IIIF-CS to decide whether or not to reingest a re-supplied asset.
    # But it plays it quite safe and may reingest things that haven't changed.
    # This is true for IIIF-CS but these requests won't make it "past" IIIF-P for that to kick in.
    # If an asset is repeated (appears more than once)
    # we only need to tell it to reingest once.
    logger.info("Checking for assets that have changed")
    logger.info(f"Existing manifest has {len(existing_manifest.get('paintedResources', []))} painted resources")
    logger.info(f"New manifest has {len(new_manifest.get('paintedResources', []))} painted resources")
    seen_ids = []
    for new_painted_resource in new_manifest.get("paintedResources", []):
        asset_id = new_painted_resource["asset"]["id"]
        if asset_id in seen_ids:
            logger.info(f"Asset {asset_id} has already been seen, skipping")
            continue
        seen_ids.append(asset_id)
        existing_painted_resource = None
        for pr in existing_manifest.get("paintedResources", []):
            if pr["asset"]["id"] == asset_id:
                logger.info(f"Found painted resource for asset {asset_id} in existing Manifest")
                existing_painted_resource = pr
                break

        if existing_painted_resource is None:
            logger.info(f"No existing painted resource for asset {asset_id}, so set reingest:true")
            new_painted_resource["reingest"] = True
            continue

        existing_origin = existing_painted_resource["asset"]["origin"]
        new_origin = new_painted_resource["asset"]["origin"]
        if existing_origin != new_origin:
            logger.info(f"Existing painted resource for asset {asset_id} has different existing origin {existing_origin} and new origin {new_origin}, so set reingest:true")
            new_painted_resource["reingest"] = True
