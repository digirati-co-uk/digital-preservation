from __future__ import annotations
from collections.abc import Callable
from dataclasses import dataclass, field
from kiota_abstractions.base_request_builder import BaseRequestBuilder
from kiota_abstractions.base_request_configuration import RequestConfiguration
from kiota_abstractions.default_query_parameters import QueryParameters
from kiota_abstractions.get_path_parameters import get_path_parameters
from kiota_abstractions.method import Method
from kiota_abstractions.request_adapter import RequestAdapter
from kiota_abstractions.request_information import RequestInformation
from kiota_abstractions.request_option import RequestOption
from kiota_abstractions.serialization import Parsable, ParsableFactory
from typing import Any, Optional, TYPE_CHECKING, Union
from warnings import warn

if TYPE_CHECKING:
    from ..models.forbidden_response import ForbiddenResponse
    from ..models.identifier import Identifier
    from ..models.identifier_metadata import IdentifierMetadata
    from ..models.unauthorised_response import UnauthorisedResponse
    from .get_orderby_query_parameter_type import GetOrderbyQueryParameterType
    from .get_order_query_parameter_type import GetOrderQueryParameterType
    from .get_status_query_parameter_type import GetStatusQueryParameterType
    from .get_s_query_parameter_type import GetSQueryParameterType
    from .ids_get_response import IdsGetResponse
    from .item.ids_item_request_builder import IdsItemRequestBuilder

class IdsRequestBuilder(BaseRequestBuilder):
    """
    Builds and executes requests for operations under /ids
    """
    def __init__(self,request_adapter: RequestAdapter, path_parameters: Union[str, dict[str, Any]]) -> None:
        """
        Instantiates a new IdsRequestBuilder and sets the default values.
        param path_parameters: The raw url or the url-template parameters for the request.
        param request_adapter: The request adapter to use to execute the requests.
        Returns: None
        """
        super().__init__(request_adapter, "{+baseurl}/ids{?created_from*,created_to*,order*,orderby*,page*,per_page*,q*,s*,status*,updated_from*,updated_to*}", path_parameters)
    
    def by_id(self,id: str) -> IdsItemRequestBuilder:
        """
        Gets an item from the id_client.ids.item collection
        param id: Identifier used in the service
        Returns: IdsItemRequestBuilder
        """
        if id is None:
            raise TypeError("id cannot be null.")
        from .item.ids_item_request_builder import IdsItemRequestBuilder

        url_tpl_params = get_path_parameters(self.path_parameters)
        url_tpl_params["id"] = id
        return IdsItemRequestBuilder(self.request_adapter, url_tpl_params)
    
    async def get(self,request_configuration: Optional[RequestConfiguration[IdsRequestBuilderGetQueryParameters]] = None) -> Optional[IdsGetResponse]:
        """
        Returns a paginated list of identifiers in JSON format. Also accepts query parameters to allow searching identifiers.
        param request_configuration: Configuration for the request such as headers, query parameters, and middleware options.
        Returns: Optional[IdsGetResponse]
        """
        request_info = self.to_get_request_information(
            request_configuration
        )
        from ..models.forbidden_response import ForbiddenResponse
        from ..models.unauthorised_response import UnauthorisedResponse

        error_mapping: dict[str, type[ParsableFactory]] = {
            "401": UnauthorisedResponse,
            "403": ForbiddenResponse,
        }
        if not self.request_adapter:
            raise Exception("Http core is null") 
        from .ids_get_response import IdsGetResponse

        return await self.request_adapter.send_async(request_info, IdsGetResponse, error_mapping)
    
    async def post(self,body: IdentifierMetadata, request_configuration: Optional[RequestConfiguration[QueryParameters]] = None) -> Optional[list[Identifier]]:
        """
        Mint a new identifier with the service
        param body: The request body
        param request_configuration: Configuration for the request such as headers, query parameters, and middleware options.
        Returns: Optional[list[Identifier]]
        """
        if body is None:
            raise TypeError("body cannot be null.")
        request_info = self.to_post_request_information(
            body, request_configuration
        )
        from ..models.forbidden_response import ForbiddenResponse
        from ..models.unauthorised_response import UnauthorisedResponse

        error_mapping: dict[str, type[ParsableFactory]] = {
            "401": UnauthorisedResponse,
            "403": ForbiddenResponse,
        }
        if not self.request_adapter:
            raise Exception("Http core is null") 
        from ..models.identifier import Identifier

        return await self.request_adapter.send_collection_async(request_info, Identifier, error_mapping)
    
    def to_get_request_information(self,request_configuration: Optional[RequestConfiguration[IdsRequestBuilderGetQueryParameters]] = None) -> RequestInformation:
        """
        Returns a paginated list of identifiers in JSON format. Also accepts query parameters to allow searching identifiers.
        param request_configuration: Configuration for the request such as headers, query parameters, and middleware options.
        Returns: RequestInformation
        """
        request_info = RequestInformation(Method.GET, self.url_template, self.path_parameters)
        request_info.configure(request_configuration)
        request_info.headers.try_add("Accept", "application/json")
        return request_info
    
    def to_post_request_information(self,body: IdentifierMetadata, request_configuration: Optional[RequestConfiguration[QueryParameters]] = None) -> RequestInformation:
        """
        Mint a new identifier with the service
        param body: The request body
        param request_configuration: Configuration for the request such as headers, query parameters, and middleware options.
        Returns: RequestInformation
        """
        if body is None:
            raise TypeError("body cannot be null.")
        request_info = RequestInformation(Method.POST, self.url_template, self.path_parameters)
        request_info.configure(request_configuration)
        request_info.headers.try_add("Accept", "application/json")
        request_info.set_content_from_parsable(self.request_adapter, "application/json", body)
        return request_info
    
    def with_url(self,raw_url: str) -> IdsRequestBuilder:
        """
        Returns a request builder with the provided arbitrary URL. Using this method means any other path or query parameters are ignored.
        param raw_url: The raw URL to use for the request builder.
        Returns: IdsRequestBuilder
        """
        if raw_url is None:
            raise TypeError("raw_url cannot be null.")
        return IdsRequestBuilder(self.request_adapter, raw_url)
    
    import datetime

    @dataclass
    class IdsRequestBuilderGetQueryParameters():
        import datetime

        """
        Returns a paginated list of identifiers in JSON format. Also accepts query parameters to allow searching identifiers.
        """
        # Limit the results of the query to identifiers created after the date specified
        created_from: Optional[datetime.date] = None

        # Limit the results of the query to identifiers created before the date specified
        created_to: Optional[datetime.date] = None

        # List identifiers in either ascending or descending order
        order: Optional[GetOrderQueryParameterType] = None

        # List identifiers in an order determined by the value of this field
        orderby: Optional[GetOrderbyQueryParameterType] = None

        # The number of the page to display. Determines how pagination of results works in conjunction with the per-page parameter (pp)
        page: Optional[int] = None

        # The number of records to display on a page. Determines how pagination of results works in conjunction with the page parameter (p)
        per_page: Optional[int] = None

        # Text used in a search for an identifier. The search is performed across the metadata associated with an identifier.
        q: Optional[str] = None

        # A parameter which identifies one or more of the metadata types used for identifiers
        s: Optional[GetSQueryParameterType] = None

        # Limit the results of the query to identifiers with the given status. Only live and deleted are currently supported
        status: Optional[GetStatusQueryParameterType] = None

        # Limit the results of the query to identifiers updated after the date specified
        updated_from: Optional[datetime.date] = None

        # Limit the results of the query to identifiers updated after the date specified
        updated_to: Optional[datetime.date] = None

    
    @dataclass
    class IdsRequestBuilderGetRequestConfiguration(RequestConfiguration[IdsRequestBuilderGetQueryParameters]):
        """
        Configuration for the request such as headers, query parameters, and middleware options.
        """
        warn("This class is deprecated. Please use the generic RequestConfiguration class generated by the generator.", DeprecationWarning)
    
    @dataclass
    class IdsRequestBuilderPostRequestConfiguration(RequestConfiguration[QueryParameters]):
        """
        Configuration for the request such as headers, query parameters, and middleware options.
        """
        warn("This class is deprecated. Please use the generic RequestConfiguration class generated by the generator.", DeprecationWarning)
    

