from datetime import datetime, timezone
import aiohttp
import asyncio
import app.settings as settings
from logzero import logger

from signal_handler import SignalHandler
from app.db import ArchivalGroupActivity
from preservation_api import get_activities, load_archival_group, load_mets
from identity_service import get_identities_from_archival_group, get_internal_iiif_uris
from catalogue_api import read_catalogue_api
from boilerplate import get_boilerplate_manifest
from manifest_decorator import add_descriptive_metadata_to_manifest, add_painted_resources
from iiif_cloud_services import put_manifest


async def read_stream():
    logger.info("starting iiif-builder...")
    signal_handler = SignalHandler()

    try:
        async with aiohttp.ClientSession() as session:
            while not signal_handler.cancellation_requested():
                last_event_time = ArchivalGroupActivity.get_latest_end_time()
                activities_result = await get_activities(settings.PRESERVATION_ACTIVITY_STREAM, session, last_event_time)
                if activities_result.success():
                    for activity in activities_result.value:
                        logger.debug(f"Processing activity with endTime={activity.end_time}")
                        await process_activity(activity, session)
                else:
                    logger.error(f"Could not read activities: {activities_result.error}")

                logger.debug(f"sleeping for {settings.ACTIVITY_STREAM_READ_INTERVAL}s")
                await asyncio.sleep(settings.ACTIVITY_STREAM_READ_INTERVAL)
    except Exception as e:
        logger.error(f"Fatal error in iiif-builder: {e}")
        raise e

    logger.info("stopping iiif-builder..")



async def process_activity(activity, session):

    job: ArchivalGroupActivity = ArchivalGroupActivity.new_activity(
        activity_end_time = activity["endTime"],
        archival_group_uri = activity["object"]["id"],
        activity_type = activity["type"]
    )

    logger.debug(f"Loading archival group from {job.archival_group_uri}")
    archival_group_result = await load_archival_group(session, job.archival_group_uri)
    if archival_group_result.failure:
        logger.error(f"Failed to load archival group: {archival_group_result.error}")
        job.error_message = archival_group_result.error
        job.save()
        return

    logger.debug(f"Loading METS for archival group {job.archival_group_uri}")
    mets_result = await load_mets(session, job.archival_group_uri)
    if mets_result.failure:
        logger.error(f"Failed to load METS for archival group: {mets_result.error}")
        job.error_message = mets_result.error
        job.save()
        return

    logger.debug(f"Calling identity service for archival group {job.archival_group_uri}")

    identities_result = await get_identities_from_archival_group(job.archival_group_uri)
    if identities_result.failure:
        logger.error(f"Failed to get Identities for archival group{job.archival_group_uri}: {identities_result.error}")
        job.error_message = identities_result.error
        job.save()
        return

    job.id_service_pid = identities_result.value["pid"]
    if settings.USE_MVP_CATALOGUE_API:
        job.catalogue_api_uri = f"{settings.MVP_CATALOGUE_API_PREFIX}{job.id_service_pid}"
    else:
        job.catalogue_api_uri = identities_result.value["catalogue_api_uri"]

    internal_iiif_uris = get_internal_iiif_uris(identities_result.value["manifest_uri"])
    job.internal_public_manifest_uri = internal_iiif_uris["public_manifest_uri"]
    job.internal_api_manifest_uri = internal_iiif_uris["api_manifest_uri"]
    job.save()

    logger.debug(f"Getting descriptive metadata from catalogue API for {job.catalogue_api_uri}")
    descriptive_metadata_result = await read_catalogue_api(session, job.catalogue_api_uri)
    if descriptive_metadata_result.failure:
        logger.error(f"Failed to load descriptive metadata from catalogue API: {descriptive_metadata_result.error}")
        job.error_message = descriptive_metadata_result.error
        job.save()
        return

    manifest = get_boilerplate_manifest()
    add_descriptive_metadata_to_manifest(manifest, descriptive_metadata_result.value)

    logger.debug(f"Adding painted resources to manifest {job.internal_public_manifest_uri}")
    add_painted_resources_result = add_painted_resources(manifest, archival_group_result.value, mets_result.value)
    if add_painted_resources_result.failure:
        logger.error(f"Failed to add painted resources to Manifest: {add_painted_resources_result.error}")
        job.error_message = add_painted_resources_result.error
        job.save()
        return

    logger.debug(f"Saving Manifest to IIIF-CS: {job.internal_public_manifest_uri}")
    put_manifest_result = await put_manifest(session, job.internal_api_manifest_uri, manifest)
    if put_manifest_result.failure:
        logger.error(f"Failed to PUT Manifest to IIIF-CS: {put_manifest_result.error}")
        job.error_message = put_manifest_result.error
        job.save()
        return

    job.finished = datetime.now(timezone.utc)
    job.save()






