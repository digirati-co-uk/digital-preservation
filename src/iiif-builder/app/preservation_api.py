from app.result import Result


async def get_activities(stream_uri, session, last_event_time):
    return []

async def load_archival_group(session, archival_group_uri) -> Result:
    message = f"Could not load Archival Group from {archival_group_uri}: (error message)"
    return {}

async def load_mets(session, archival_group_uri) -> Result:
    return {}