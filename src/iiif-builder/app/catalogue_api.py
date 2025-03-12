from app.result import Result

async def read_catalogue_api(session, catalogue_api_uri) -> Result:
    response = await session.get(catalogue_api_uri)
    if response.status_code != 200:
        return Result(False, f"Catalogue API returned HTTP status {response.status_code}")
    json = await response.json()
    return Result.success(json)