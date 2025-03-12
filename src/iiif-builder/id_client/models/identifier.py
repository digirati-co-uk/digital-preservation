from __future__ import annotations
import datetime
from collections.abc import Callable
from dataclasses import dataclass, field
from kiota_abstractions.serialization import AdditionalDataHolder, Parsable, ParseNode, SerializationWriter
from typing import Any, Optional, TYPE_CHECKING, Union

if TYPE_CHECKING:
    from .identifier_status import Identifier_status

@dataclass
class Identifier(AdditionalDataHolder, Parsable):
    # Stores additional data not described in the OpenAPI description found when deserializing. Can be used for serialization as well.
    additional_data: dict[str, Any] = field(default_factory=dict)

    # EMu Accession IRN
    accirn: Optional[str] = None
    # Catalogue API URI
    catalogueapiuri: Optional[str] = None
    # EMu Catalogue IRN
    catirn: Optional[str] = None
    # Created date
    created: Optional[datetime.datetime] = None
    # Description
    description: Optional[str] = None
    # Digital Object Identifier (DOI)
    doi: Optional[str] = None
    # EPrint ID
    epid: Optional[str] = None
    # identifier
    id: Optional[str] = None
    # LUDOS ID
    ludosid: Optional[str] = None
    # IIIF Manifest URI
    manifesturi: Optional[str] = None
    # Provenance
    provenance: Optional[str] = None
    # EMu Party IRN
    ptyirn: Optional[str] = None
    # Redirection URL
    redirect: Optional[str] = None
    # Archival Group URI in repository
    repositoryuri: Optional[str] = None
    # EMu Site IRN
    siteirn: Optional[str] = None
    # Status
    status: Optional[Identifier_status] = None
    # Title
    title: Optional[str] = None
    # Last-modified date
    updated: Optional[datetime.datetime] = None
    
    @staticmethod
    def create_from_discriminator_value(parse_node: ParseNode) -> Identifier:
        """
        Creates a new instance of the appropriate class based on discriminator value
        param parse_node: The parse node to use to read the discriminator value and create the object
        Returns: Identifier
        """
        if parse_node is None:
            raise TypeError("parse_node cannot be null.")
        return Identifier()
    
    def get_field_deserializers(self,) -> dict[str, Callable[[ParseNode], None]]:
        """
        The deserialization information for the current model
        Returns: dict[str, Callable[[ParseNode], None]]
        """
        from .identifier_status import Identifier_status

        from .identifier_status import Identifier_status

        fields: dict[str, Callable[[Any], None]] = {
            "accirn": lambda n : setattr(self, 'accirn', n.get_str_value()),
            "catalogueapiuri": lambda n : setattr(self, 'catalogueapiuri', n.get_str_value()),
            "catirn": lambda n : setattr(self, 'catirn', n.get_str_value()),
            "created": lambda n : setattr(self, 'created', n.get_datetime_value()),
            "description": lambda n : setattr(self, 'description', n.get_str_value()),
            "doi": lambda n : setattr(self, 'doi', n.get_str_value()),
            "epid": lambda n : setattr(self, 'epid', n.get_str_value()),
            "id": lambda n : setattr(self, 'id', n.get_str_value()),
            "ludosid": lambda n : setattr(self, 'ludosid', n.get_str_value()),
            "manifesturi": lambda n : setattr(self, 'manifesturi', n.get_str_value()),
            "provenance": lambda n : setattr(self, 'provenance', n.get_str_value()),
            "ptyirn": lambda n : setattr(self, 'ptyirn', n.get_str_value()),
            "redirect": lambda n : setattr(self, 'redirect', n.get_str_value()),
            "repositoryuri": lambda n : setattr(self, 'repositoryuri', n.get_str_value()),
            "siteirn": lambda n : setattr(self, 'siteirn', n.get_str_value()),
            "status": lambda n : setattr(self, 'status', n.get_enum_value(Identifier_status)),
            "title": lambda n : setattr(self, 'title', n.get_str_value()),
            "updated": lambda n : setattr(self, 'updated', n.get_datetime_value()),
        }
        return fields
    
    def serialize(self,writer: SerializationWriter) -> None:
        """
        Serializes information the current object
        param writer: Serialization writer to use to serialize this model
        Returns: None
        """
        if writer is None:
            raise TypeError("writer cannot be null.")
        writer.write_str_value("accirn", self.accirn)
        writer.write_str_value("catalogueapiuri", self.catalogueapiuri)
        writer.write_str_value("catirn", self.catirn)
        writer.write_datetime_value("created", self.created)
        writer.write_str_value("description", self.description)
        writer.write_str_value("doi", self.doi)
        writer.write_str_value("epid", self.epid)
        writer.write_str_value("id", self.id)
        writer.write_str_value("ludosid", self.ludosid)
        writer.write_str_value("manifesturi", self.manifesturi)
        writer.write_str_value("provenance", self.provenance)
        writer.write_str_value("ptyirn", self.ptyirn)
        writer.write_str_value("redirect", self.redirect)
        writer.write_str_value("repositoryuri", self.repositoryuri)
        writer.write_str_value("siteirn", self.siteirn)
        writer.write_enum_value("status", self.status)
        writer.write_str_value("title", self.title)
        writer.write_datetime_value("updated", self.updated)
        writer.write_additional_data_value(self.additional_data)
    

