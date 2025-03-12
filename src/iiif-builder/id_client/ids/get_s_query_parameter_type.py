from enum import Enum

class GetSQueryParameterType(str, Enum):
    Title = "title",
    Desc = "desc",
    Catirn = "catirn",
    Accirn = "accirn",
    Ptyirn = "ptyirn",
    Siteirn = "siteirn",
    Epid = "epid",
    Ludosid = "ludosid",
    Doi = "doi",
    Redirect = "redirect",
    Provenance = "provenance",

