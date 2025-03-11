import datetime
import aiohttp
import asyncio
import app.settings as settings
from logzero import logger

from signal_handler import SignalHandler
from app.db import db, ArchivalGroupActivity
from preservation_api import get_activities, load_archival_group, load_mets
from identity_service import get_public_manifest_uri, get_catalogue_api_uri, make_api_manifest_uri
from catalogue_api import read_catalogue_api
from boilerplate import get_boilerplate_manifest
from manifest_decorator import add_descriptive_metadata_to_manifest, add_painted_resources
from iiif_cloud_services import put_manifest


async def read_stream():
    logger.info("starting iiif-builder...")
    await db.set_bind(settings.POSTGRES_CONNECTION)

    signal_handler = SignalHandler()

    try:
        async with aiohttp.ClientSession() as session:
            while not signal_handler.cancellation_requested():
                last_event_time = await ArchivalGroupActivity.get_latest_end_time()
                activities = get_activities(settings.PRESERVATION_ACTIVITY_STREAM, session, last_event_time)
                for activity in activities:
                    logger.debug(f"Processing activity with endTime={activity.end_time}")
                    process_activity(activity, session)

                logger.debug(f"sleeping for {settings.ACTIVITY_STREAM_READ_INTERVAL}s")
                await asyncio.sleep(settings.ACTIVITY_STREAM_READ_INTERVAL)
    except Exception as e:
        logger.error(f"Fatal error in iiif-builder: {e}")
        raise e

    logger.info("stopping iiif-builder..")



async def process_activity(activity, session):

    archival_group_uri = activity["object"]["id"]
    stored = ArchivalGroupActivity.create(
        activity_end_time = activity["endTime"],
        archival_group_uri = archival_group_uri,
        activity_type = activity["type"],
        started = datetime.now(datetime.timezone.utc)
    )

    logger.debug(f"Loading archival group from {archival_group_uri}")
    archival_group_result = await load_archival_group(session, archival_group_uri)
    if archival_group_result.failure:
        logger.error(f"Failed to load archival group: {archival_group_result.error}")
        await stored.update(error_message=archival_group_result.error).apply()
        return

    logger.debug(f"Loading METS for archival group {archival_group_uri}")
    mets_result = await load_mets(session, archival_group_uri)
    if mets_result.failure:
        logger.error(f"Failed to load METS for archival group: {mets_result.error}")
        await stored.update(error_message=mets_result.error).apply()
        return

    logger.debug(f"Calling identity service for archival group {archival_group_uri}")
    # These next two might come back from the identity service in the same operation
    public_manifest_uri_result = await get_public_manifest_uri(session, archival_group_uri)
    if public_manifest_uri_result.failure:
        logger.error(f"Failed to get public IIIF URL for archival group: {public_manifest_uri_result.error}")
        await stored.update(error_message=public_manifest_uri_result.error).apply()
        return

    public_manifest_uri = public_manifest_uri_result.value
    api_manifest_uri = make_api_manifest_uri(public_manifest_uri)
    await stored.update(
        public_manifest_uri=public_manifest_uri,
        api_manifest_uri=api_manifest_uri).apply()

    catalogue_api_uri_result = await get_catalogue_api_uri(session, archival_group_uri)
    if catalogue_api_uri_result.failure:
        logger.error(f"Failed to find out Catalogue API URL for archival group: {catalogue_api_uri_result.error}")
        await stored.update(error_message=catalogue_api_uri_result.error).apply()
        return

    catalogue_api_uri = catalogue_api_uri_result.value
    await stored.update(catalogue_api_uri=catalogue_api_uri).apply()

    logger.debug(f"Getting descriptive metadata from catalogue API for {catalogue_api_uri}")
    descriptive_metadata_result = await read_catalogue_api(session, catalogue_api_uri)
    if descriptive_metadata_result.failure:
        logger.error(f"Failed to load descriptive metadata from catalogue API: {descriptive_metadata_result.error}")
        await stored.update(error_message=descriptive_metadata_result.error).apply()
        return

    manifest = get_boilerplate_manifest()
    add_descriptive_metadata_to_manifest(manifest, descriptive_metadata_result.value)

    logger.debug(f"Adding painted resources to manifest {public_manifest_uri}")
    add_painted_resources_result = add_painted_resources(manifest, archival_group_result.value, mets_result.value)
    if add_painted_resources_result.failure:
        logger.error(f"Failed to add painted resources to Manifest: {add_painted_resources_result.error}")
        await stored.update(error_message=add_painted_resources_result.error).apply()
        return

    logger.debug(f"Saving Manifest to IIIF-CS: {public_manifest_uri}")
    put_manifest_result = put_manifest(api_manifest_uri, manifest)
    if put_manifest_result.failure:
        logger.error(f"Failed to PUT Manifest to IIIF-CS: {put_manifest_result.error}")
        await stored.update(error_message=put_manifest_result.error).apply()
        return

    await stored.update(finished=datetime.now(datetime.timezone.utc)).apply()






