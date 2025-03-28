import hashlib
import base64
import urllib.parse
# the above two are only needed for the fake ID service

from kiota_abstractions.authentication import ApiKeyAuthenticationProvider, KeyLocation
from kiota_abstractions.base_request_configuration import RequestConfiguration
from kiota_abstractions.request_information import QueryParameters
from kiota_http.httpx_request_adapter import HttpxRequestAdapter

from app import settings
from app.result import Result
from id_client.identity_service_client import IdentityServiceClient

# Auth configuration for Leeds identity service
auth_provider = ApiKeyAuthenticationProvider(
    KeyLocation.Header,
    settings.IDENTITY_SERVICE_API_KEY,
    settings.IDENTITY_SERVICE_API_HEADER,
    []
)

request_adapter = HttpxRequestAdapter(auth_provider)
request_adapter.base_url = settings.IDENTITY_SERVICE_BASE_URL
client = IdentityServiceClient(request_adapter)


async def get_identities_from_archival_group_fake(archival_group_uri:str) -> Result:
    path = urllib.parse.urlparse(archival_group_uri).path
    fake_pid = base64.urlsafe_b64encode(hashlib.md5(path.encode('utf-8')).digest()).decode('utf-8').rstrip('=').lower()
    fake_results = {
        "results": [
            {
                "id": fake_pid,
                "manifesturi": f"https://iiif.leeds.ac.uk/presentation/cc/{fake_pid}",
                "catalogueapiuri": f"https://catalogue.leeds.ac.uk/{fake_pid}",
                "repositoryuri": archival_group_uri
            }
        ]
    }
    return Result.success({
        "pid": fake_results["results"][0]["id"],
        "manifest_uri": fake_results["results"][0]["manifesturi"],
        "catalogue_api_uri": fake_results["results"][0]["catalogueapiuri"]
    })



async def get_identities_from_archival_group(archival_group_uri) -> Result:
    # Is this the right way to build this?
    request_config = RequestConfiguration(query_parameters={
        "s": "repositoryuri",
        "q": archival_group_uri
    })
    search_results = await client.ids.get(request_config)
    result_count =  len(search_results["results"])
    if result_count == 0:
        return Result(False, f"No results for AG {archival_group_uri}")
    if result_count > 1:
        return Result(False, f"Multiple results ({result_count}) for AG {archival_group_uri}")
    return Result.success({
        "pid": search_results["results"][0]["id"],
        "manifest_uri": search_results["results"][0]["manifesturi"],
        "catalogue_api_uri": search_results["results"][0]["catalogueapiuri"]
    })
    # Example:
    # {
    #   "manifest_uri": "https://iiif.leeds.ac.uk/presentation/sc/abcd1234",
    #   "catalogue_api_uri": "https://catalogue-api.leeds.ac.uk/id/abcd1234",
    # }


def get_internal_iiif_uris(public_manifest_uri):
    # This allows the ID service to only worry about the _rewritten_ public URI
    # This needs to be much more robust obvs!
    path_part = public_manifest_uri.lstrip(settings.REWRITTEN_PUBLIC_IIIF_PRESENTATION_PREFIX)
    flat_id = path_part.replace("/", "_")
    return {
        "public_manifest_uri": f"{settings.IIIF_CS_PRESENTATION_HOST}/{settings.IIIF_CS_CUSTOMER_ID}/{path_part}",
        "api_manifest_uri":    f"{settings.IIIF_CS_PRESENTATION_HOST}/{settings.IIIF_CS_CUSTOMER_ID}/manifests/{flat_id}"
    }

