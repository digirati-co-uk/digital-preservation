from __future__ import annotations
from collections.abc import Callable
from kiota_abstractions.api_client_builder import enable_backing_store_for_serialization_writer_factory, register_default_deserializer, register_default_serializer
from kiota_abstractions.base_request_builder import BaseRequestBuilder
from kiota_abstractions.get_path_parameters import get_path_parameters
from kiota_abstractions.request_adapter import RequestAdapter
from kiota_abstractions.serialization import ParseNodeFactoryRegistry, SerializationWriterFactoryRegistry
from kiota_serialization_form.form_parse_node_factory import FormParseNodeFactory
from kiota_serialization_form.form_serialization_writer_factory import FormSerializationWriterFactory
from kiota_serialization_json.json_parse_node_factory import JsonParseNodeFactory
from kiota_serialization_json.json_serialization_writer_factory import JsonSerializationWriterFactory
from kiota_serialization_multipart.multipart_serialization_writer_factory import MultipartSerializationWriterFactory
from kiota_serialization_text.text_parse_node_factory import TextParseNodeFactory
from kiota_serialization_text.text_serialization_writer_factory import TextSerializationWriterFactory
from typing import Any, Optional, TYPE_CHECKING, Union

if TYPE_CHECKING:
    from .catirn.catirn_request_builder import CatirnRequestBuilder
    from .ids.ids_request_builder import IdsRequestBuilder
    from .ptyirn.ptyirn_request_builder import PtyirnRequestBuilder
    from .siteirn.siteirn_request_builder import SiteirnRequestBuilder

class IdentityServiceClient(BaseRequestBuilder):
    """
    The main entry point of the SDK, exposes the configuration and the fluent API.
    """
    def __init__(self,request_adapter: RequestAdapter) -> None:
        """
        Instantiates a new IdentityServiceClient and sets the default values.
        param request_adapter: The request adapter to use to execute the requests.
        Returns: None
        """
        if request_adapter is None:
            raise TypeError("request_adapter cannot be null.")
        super().__init__(request_adapter, "{+baseurl}", None)
        register_default_serializer(JsonSerializationWriterFactory)
        register_default_serializer(TextSerializationWriterFactory)
        register_default_serializer(FormSerializationWriterFactory)
        register_default_serializer(MultipartSerializationWriterFactory)
        register_default_deserializer(JsonParseNodeFactory)
        register_default_deserializer(TextParseNodeFactory)
        register_default_deserializer(FormParseNodeFactory)
        if not self.request_adapter.base_url:
            self.request_adapter.base_url = "https://id.library.leeds.ac.uk/api/v1"
        self.path_parameters["base_url"] = self.request_adapter.base_url
    
    @property
    def catirn(self) -> CatirnRequestBuilder:
        """
        The catirn property
        """
        from .catirn.catirn_request_builder import CatirnRequestBuilder

        return CatirnRequestBuilder(self.request_adapter, self.path_parameters)
    
    @property
    def ids(self) -> IdsRequestBuilder:
        """
        The ids property
        """
        from .ids.ids_request_builder import IdsRequestBuilder

        return IdsRequestBuilder(self.request_adapter, self.path_parameters)
    
    @property
    def ptyirn(self) -> PtyirnRequestBuilder:
        """
        The ptyirn property
        """
        from .ptyirn.ptyirn_request_builder import PtyirnRequestBuilder

        return PtyirnRequestBuilder(self.request_adapter, self.path_parameters)
    
    @property
    def siteirn(self) -> SiteirnRequestBuilder:
        """
        The siteirn property
        """
        from .siteirn.siteirn_request_builder import SiteirnRequestBuilder

        return SiteirnRequestBuilder(self.request_adapter, self.path_parameters)
    

