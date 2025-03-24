from app.mets_parser.working_filesystem import WorkingDirectory, WorkingFile

class MetsWrapper:
    def __init__(self):
        self.name:str
        self.agent:str
        self.physical_structure:WorkingDirectory
        self.files:list[WorkingFile]=[]
        self.amd_map:dict={}
        self.file_map:dict={}
        self.tech_map:dict={}


