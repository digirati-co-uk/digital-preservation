from app import settings
from app.result import Result


async def read_catalogue_api(session, catalogue_api_uri) -> Result:

    response = await session.get(catalogue_api_uri, headers={
        settings.MVP_CATALOGUE_API_KEY_HEADER: settings.MVP_CATALOGUE_API_KEY_VALUE
    })
    if response.status != 200:
        return Result(False, f"Catalogue API returned HTTP status {response.status_code}")
    json = await response.json()
    return Result.success(json)