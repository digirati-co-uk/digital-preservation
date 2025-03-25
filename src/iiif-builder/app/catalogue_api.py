from app import settings
from app.result import Result

async def read_catalogue_api(session, catalogue_api_uri) -> Result:

    fake_pid = catalogue_api_uri.split('=')[-1]
    fake_result = {
        "success": True,
        "error": None,
        "data": {
            "Title": f"A book with PID: {fake_pid}",
            "Shelfmark": f"FAKE/SHELF/{fake_pid}",
            "Date": "1971",
            "Description": f"A Description of {fake_pid}\n\n\nOn multiple lines.",
            "Attribution": "Image Credit : Leeds University Library",
            "Homepage": f"https://explore.library.leeds.ac.uk/special-collections-explore/{fake_pid}",
            "Rights": [
                "https://creativecommons.org/publicdomain/mark/1.0/"
            ],
            "Collections": [
                "Leeds Archive of Vernacular Culture"
            ],
            "Creators": [
                f"Author 1 {fake_pid}",
                f"Author 2 {reversed(fake_pid)}",
            ]
        }
    }
    return Result.success(fake_result)


async def read_catalogue_api_for_real(session, catalogue_api_uri) -> Result:

    response = await session.get(catalogue_api_uri, headers={
        settings.MVP_CATALOGUE_API_KEY_HEADER: settings.MVP_CATALOGUE_API_KEY_VALUE
    })
    if response.status != 200:
        return Result(False, f"Catalogue API returned HTTP status {response.status_code}")
    json = await response.json()
    return Result.success(json)