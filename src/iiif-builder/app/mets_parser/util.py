from itertools import islice


def get_parent(path:str)->str:
    parts = list(filter(None, path.split('/')))
    if len(parts) == 0:
        return None
    joined = '/'.join(list(islice(parts, 0, len(parts) - 1)))
    if path.startswith('/'):
        return f"/{joined}"
    return joined


def get_slug(path:str)->str:
    if path.endswith('/'):
        path = path.rstrip('/')
    return path.split('/')[-1];

