import urllib

from aiohttp import ClientSession

from app import settings
from app.result import Result


async def get_identities_from_archival_group(session: ClientSession, archival_group_uri) -> Result:

    for_query = mutate(archival_group_uri)

    # This is a workaround while the ID service is incomplete.
    # FOR NOW It assumes that the archival_group_uri ends with an 8-char slug and that this slug is the PID
    # We will still validate that the PID exists, though.

    pid = archival_group_uri.split('/')[-1]
    ag_path = urllib.parse.urlparse(archival_group_uri).path.lstrip('/').lstrip('repository/')

    headers = {
        settings.IDENTITY_SERVICE_API_HEADER: settings.IDENTITY_SERVICE_API_KEY
    }
    query_url = f"{settings.IDENTITY_SERVICE_BASE_URL}/ids/q={for_query}&s=repositoryuri"
    # pid_url = f"{settings.IDENTITY_SERVICE_BASE_URL}/ids/{pid}"
    response = await session.get(query_url, headers=headers)
    if response.status != 200:
        return Result(False, response.status)

    results_page = await response.json()
    results = results_page.get('results', [])
    if len(results) == 0:
        return Result(False, "No results found")
    if len(results) > 1:
        return Result(False, "Multiple results found")

    result = results[0]

    # if from pid direct
    # result = await response.json()

    return Result.success({
        "pid": result.get('id'), # should be same as pid
        "manifest_uri": result.get('manifesturi'),
        "catalogue_api_uri": result.get('catalogueapiuri'),
        "catirn": result.get('catirn')
    })


def get_internal_iiif_uris(public_manifest_uri):
    # This allows the ID service to only worry about the _rewritten_ public URI
    # This needs to be much more robust obvs!
    path_part = public_manifest_uri.lstrip(settings.REWRITTEN_PUBLIC_IIIF_PRESENTATION_PREFIX)
    flat_id = path_part.replace("/", "_")
    return {
        "public_manifest_uri": f"{settings.IIIF_CS_PRESENTATION_HOST}/{settings.IIIF_CS_CUSTOMER_ID}/{path_part}",
        "api_manifest_uri":    f"{settings.IIIF_CS_PRESENTATION_HOST}/{settings.IIIF_CS_CUSTOMER_ID}/manifests/{flat_id}"
    }

