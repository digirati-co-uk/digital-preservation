from app.result import Result


async def read_catalogue_api(session, catalogue_api_uri) -> Result:
    message = f"Could not obtain descriptive metadata from Catalogue API at {catalogue_api_uri}: (error message)"
    return {}