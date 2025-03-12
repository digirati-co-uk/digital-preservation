from enum import Enum

class GetOrderbyQueryParameterType(str, Enum):
    Created = "created",
    Updated = "updated",
    Noid = "noid",

