from enum import Enum

class GetStatusQueryParameterType(str, Enum):
    Live = "live",
    Deleted = "deleted",

