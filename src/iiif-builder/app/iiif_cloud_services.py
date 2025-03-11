from app.result import Result

async def put_manifest(api_manifest_uri, manifest) -> Result:
    # Needs to have all the check logic for reingest
    message = f"Could not save Manifest: (error)"
    return {}