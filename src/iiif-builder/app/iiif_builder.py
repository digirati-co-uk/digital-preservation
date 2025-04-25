import urllib
from datetime import datetime, timezone
import aiohttp
import asyncio
import app.settings as settings
from logzero import logger

from app.signal_handler import SignalHandler
from app.db import ArchivalGroupActivity
from app.preservation_api import get_activities, load_archival_group, load_mets
from app.identity_service import get_identities_from_archival_group, get_internal_iiif_uris
from app.catalogue_api import read_catalogue_api
from app.boilerplate import get_boilerplate_manifest
from app.manifest_decorator import add_descriptive_metadata_to_manifest, add_painted_resources
from app.iiif_cloud_services import put_manifest

archival_group_prefixes = settings.ARCHIVAL_GROUP_PREFIXES_TO_PROCESS.split(',')

async def read_stream():
    logger.info("starting iiif-builder...")
    signal_handler = SignalHandler()

    try:
        async with aiohttp.ClientSession() as session:
            while not signal_handler.cancellation_requested():
                last_event_time = ArchivalGroupActivity.get_latest_end_time()
                activities_result = await get_activities(settings.PRESERVATION_ACTIVITY_STREAM, session, last_event_time)
                if activities_result.success:
                    for activity in reversed(activities_result.value):
                        logger.debug(f"Processing activity with endTime={activity["endTime"]}")
                        await process_activity(activity, session)
                else:
                    logger.error(f"Could not read activities: {activities_result.error}")

                logger.debug(f"Sleeping for {settings.ACTIVITY_STREAM_READ_INTERVAL}s")
                await asyncio.sleep(settings.ACTIVITY_STREAM_READ_INTERVAL)
    except Exception as e:
        logger.error(f"Fatal error in iiif-builder: {e}")
        raise e

    logger.info("stopping iiif-builder..")


def should_process(archival_group_uri):
    ag_path = urllib.parse.urlparse(archival_group_uri).path.lstrip('/').lstrip('repository/')
    for prefix in archival_group_prefixes:
        if ag_path.startswith(f"{prefix.rstrip('/')}/"):
            return True

    return False


async def process_activity(activity, session):

    job: ArchivalGroupActivity = ArchivalGroupActivity.new_activity(
        activity_end_time_date = datetime.fromisoformat(activity["endTime"]),
        archival_group_uri = activity["object"]["id"],
        activity_type = activity["type"]
    )

    if not should_process(job.archival_group_uri):
        # Not really an error though.
        message = "Skipping because AG URI doesn't match configured prefix(es)"
        logger.error(message)
        job.error_message = message
        job.finished = datetime.now(timezone.utc)
        job.save()
        return

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

    identities_result = await get_identities_from_archival_group(session, job.archival_group_uri)
    if identities_result.failure:
        logger.error(f"Failed to get Identities for archival group{job.archival_group_uri}: {identities_result.error}")
        job.error_message = identities_result.error
        job.save()
        return

    job.id_service_pid = identities_result.value["pid"]
    if settings.CONSTRUCT_CATALOGUE_API_URI:
        job.catalogue_api_uri = f"{settings.MVP_CATALOGUE_API_PREFIX}{job.id_service_pid}"
    else:
        job.catalogue_api_uri = identities_result.value["catalogue_api_uri"]

    public_manifest_uri = identities_result.value["manifest_uri"]
    # This allows the ID service to only worry about the _rewritten_ public URI
    path_part = public_manifest_uri.removeprefix(settings.REWRITTEN_PUBLIC_IIIF_PRESENTATION_PREFIX)
    iiif_cs = f"{settings.IIIF_CS_PRESENTATION_HOST}/{settings.IIIF_CS_CUSTOMER_ID}"
    job.internal_public_manifest_uri = f"{iiif_cs}/{path_part}"
    job.internal_api_manifest_uri    = f"{iiif_cs}/manifests/{job.id_service_pid}"
    canvas_id_prefix                 = f"{iiif_cs}/canvases/{job.id_service_pid}_"
    asset_prefix                     = f"{job.id_service_pid}_"
    job.save()

    logger.debug(f"Getting descriptive metadata from catalogue API for {job.catalogue_api_uri}")
    descriptive_metadata_result = await read_catalogue_api(session, job.catalogue_api_uri)
    if descriptive_metadata_result.failure:
        logger.error(f"Failed to load descriptive metadata from catalogue API: {descriptive_metadata_result.error}")
        job.error_message = descriptive_metadata_result.error
        job.save()
        return

    manifest = get_boilerplate_manifest()
    manifest["publicId"] = job.internal_public_manifest_uri
    add_descriptive_metadata_result = add_descriptive_metadata_to_manifest(manifest, descriptive_metadata_result.value)
    if add_descriptive_metadata_result.failure:
        logger.error(f"Failed to parse descriptive metadata from catalogue API: {add_descriptive_metadata_result.error}")
        job.error_message = add_descriptive_metadata_result.error
        job.save()
        return

    logger.debug(f"Adding painted resources to manifest {job.internal_public_manifest_uri}")
    add_painted_resources_result = add_painted_resources(manifest, archival_group_result.value, mets_result.value, canvas_id_prefix, asset_prefix)
    if add_painted_resources_result.failure:
        logger.error(f"Failed to add painted resources to Manifest: {add_painted_resources_result.error}")
        job.error_message = add_painted_resources_result.error
        job.save()
        return
    logger.info(f"Added {len(manifest['paintedResources'])} painted resources to Manifest {job.internal_public_manifest_uri}")

    logger.debug(f"Saving Manifest to IIIF-CS: {job.internal_public_manifest_uri}")
    put_manifest_result = await put_manifest(session, job.internal_api_manifest_uri, manifest)
    if put_manifest_result.failure:
        logger.error(f"Failed to PUT Manifest to IIIF-CS: {put_manifest_result.error}")
        job.error_message = put_manifest_result.error
        job.save()
        return

    job.finished = datetime.now(timezone.utc)
    job.save()




