from app.result import Result


async def get_public_manifest_uri(session, archival_group_uri) -> Result:
    message = f"Could not obtain IIIF Manifest URI for {archival_group_uri}: (error message)"
    return {}


def make_api_manifest_uri(public_manifest_uri):
    return ""


async def get_catalogue_api_uri(session, archival_group_uri) -> Result:
    message = f"Could not obtain Catalogue API URI for {archival_group_uri}: (error message)"
    return {}