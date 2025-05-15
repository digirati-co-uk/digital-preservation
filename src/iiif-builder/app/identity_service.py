import urllib

from aiohttp import ClientSession

from app import settings
from app.result import Result

container_aliases = {}
if settings.PRESERVATION_COLLECTIONS_CONTAINER_ALIASES and not settings.PRESERVATION_COLLECTIONS_CONTAINER_ALIASES.isspace():
    for pairs in settings.PRESERVATION_COLLECTIONS_CONTAINER_ALIASES.split(','):
        pair = pairs.split(':')
        container_aliases[pair[0].strip()] = pair[1].strip()

host_aliases = {}
if settings.PRESERVATION_COLLECTIONS_HOST_ALIASES and not settings.PRESERVATION_COLLECTIONS_HOST_ALIASES.isspace():
    for pairs in settings.PRESERVATION_COLLECTIONS_HOST_ALIASES.split(','):
        pair = pairs.split(':')
        host_aliases[pair[0].strip()] = pair[1].strip()


def mutate(archival_group_uri):
    # for dev and testing - call the id service with its expected archival group uri rather than
    # the actual one
    ag_url = urllib.parse.urlparse(archival_group_uri)

    ag_path = ag_url.path.lstrip('/').lstrip('repository/')
    ag_path_parts = ag_path.split('/')
    top_level_container = ag_path_parts[-2]
    container_alias = container_aliases.get(top_level_container, None)
    if container_alias:
        old_end = f"{top_level_container}/{ag_path_parts[-1]}"
        new_end = f"{container_alias}/{ag_path_parts[-1]}"
        archival_group_uri = f"{archival_group_uri.removesuffix(old_end)}{new_end}"

    ag_host = ag_url.hostname
    host_alias = host_aliases.get(ag_host, None)
    if host_alias:
        archival_group_uri = archival_group_uri.replace(ag_host, host_alias)
        archival_group_uri = archival_group_uri.replace(f":{ag_url.port}", "")

    return archival_group_uri


async def get_identities_from_archival_group(session: ClientSession, archival_group_uri) -> Result:

    for_query = mutate(archival_group_uri)

    headers = {
        settings.IDENTITY_SERVICE_API_HEADER: settings.IDENTITY_SERVICE_API_KEY
    }
    query_url = f"{settings.IDENTITY_SERVICE_BASE_URL}/ids?q={for_query}&s=repositoryuri"
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

