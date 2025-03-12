from __future__ import annotations
from collections.abc import Callable
from dataclasses import dataclass, field
from kiota_abstractions.serialization import AdditionalDataHolder, Parsable, ParseNode, SerializationWriter
from typing import Any, Optional, TYPE_CHECKING, Union

@dataclass
class Pagination(AdditionalDataHolder, Parsable):
    # Stores additional data not described in the OpenAPI description found when deserializing. Can be used for serialization as well.
    additional_data: dict[str, Any] = field(default_factory=dict)

    # True if the current page is not the last page
    has_next: Optional[bool] = None
    # True if the current page is not the first page
    has_prev: Optional[bool] = None
    # The next page number, or zero if this is the last page
    next_num: Optional[int] = None
    # URL for previous page in results
    next_url: Optional[str] = None
    # Current page number
    page: Optional[int] = None
    # Total number of pages
    pages: Optional[int] = None
    # Number of results to include in each page
    per_page: Optional[int] = None
    # The previous page number, or zero if this is the first page
    prev_num: Optional[int] = None
    # URL for next page in results
    prev_url: Optional[str] = None
    # Search terms
    query: Optional[str] = None
    # The schemas property
    schemas: Optional[list[str]] = None
    # Total number of items across all pages
    total: Optional[int] = None
    
    @staticmethod
    def create_from_discriminator_value(parse_node: ParseNode) -> Pagination:
        """
        Creates a new instance of the appropriate class based on discriminator value
        param parse_node: The parse node to use to read the discriminator value and create the object
        Returns: Pagination
        """
        if parse_node is None:
            raise TypeError("parse_node cannot be null.")
        return Pagination()
    
    def get_field_deserializers(self,) -> dict[str, Callable[[ParseNode], None]]:
        """
        The deserialization information for the current model
        Returns: dict[str, Callable[[ParseNode], None]]
        """
        fields: dict[str, Callable[[Any], None]] = {
            "has_next": lambda n : setattr(self, 'has_next', n.get_bool_value()),
            "has_prev": lambda n : setattr(self, 'has_prev', n.get_bool_value()),
            "next_num": lambda n : setattr(self, 'next_num', n.get_int_value()),
            "next_url": lambda n : setattr(self, 'next_url', n.get_str_value()),
            "page": lambda n : setattr(self, 'page', n.get_int_value()),
            "pages": lambda n : setattr(self, 'pages', n.get_int_value()),
            "per_page": lambda n : setattr(self, 'per_page', n.get_int_value()),
            "prev_num": lambda n : setattr(self, 'prev_num', n.get_int_value()),
            "prev_url": lambda n : setattr(self, 'prev_url', n.get_str_value()),
            "query": lambda n : setattr(self, 'query', n.get_str_value()),
            "schemas": lambda n : setattr(self, 'schemas', n.get_collection_of_primitive_values(str)),
            "total": lambda n : setattr(self, 'total', n.get_int_value()),
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
        writer.write_bool_value("has_next", self.has_next)
        writer.write_bool_value("has_prev", self.has_prev)
        writer.write_int_value("next_num", self.next_num)
        writer.write_str_value("next_url", self.next_url)
        writer.write_int_value("page", self.page)
        writer.write_int_value("pages", self.pages)
        writer.write_int_value("per_page", self.per_page)
        writer.write_int_value("prev_num", self.prev_num)
        writer.write_str_value("prev_url", self.prev_url)
        writer.write_str_value("query", self.query)
        writer.write_collection_of_primitive_values("schemas", self.schemas)
        writer.write_int_value("total", self.total)
        writer.write_additional_data_value(self.additional_data)
    

