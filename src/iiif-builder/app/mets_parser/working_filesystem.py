from datetime import datetime
from itertools import islice

from util import get_parent, get_slug


class WorkingBase:
    def __init__(self):
        self.type:str=None
        self.local_path:str=None
        self.name:str=None
        self.modified:datetime=None
        self.access_condition:str=None
        self.rights:str=None

    def get_slug(self):
        return self.local_path.split("/")[-1]


class WorkingFile(WorkingBase):

    def __init__(self):
        super().__init__()
        self.type = "WorkingFile"
        self.content_type:str=None
        self.digest:str=None
        self.size:int=None



class WorkingDirectory(WorkingBase):

    def __init__(self):
        super().__init__()
        self.type = "WorkingDirectory"
        self.files:list[WorkingFile] = []
        self.directories:list[WorkingDirectory] = []


    def find_file(self, path:str):
        parent = self.find_directory(get_parent(path))
        if parent is None:
            return None
        slug = get_slug(path)
        matches = list(filter(lambda f: f.get_slug() == slug, self.files))
        if len(matches) > 0:
            return matches[0]
        return None



    def find_directory(self, path:str, create:bool=False)-> 'WorkingDirectory':
        if path is None or path == '' or path.strip() == '' or path == "/":
            return self
        parts = list(filter(None, path.split('/')))
        directory = self
        for index, part in enumerate(parts):
            matches = list(filter(lambda d: d.get_slug() == part, directory.directories))
            potential_directory = None
            if len(matches) > 0:
                potential_directory = matches[0]
            if create:
                if potential_directory is None:
                    potential_directory = WorkingDirectory()
                    potential_directory.local_path = '/'.join(list(islice(parts, index + 1)))
                    directory.directories.append(potential_directory)
            else:
                if potential_directory is None:
                    return None

            directory = potential_directory

        return directory

