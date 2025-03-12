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
    from ...models.deletion_metadata import DeletionMetadata
    from ...models.forbidden_response import ForbiddenResponse
    from ...models.identifier import Identifier
    from ...models.identifier_metadata import IdentifierMetadata
    from ...models.not_found_response import NotFoundResponse
    from ...models.success_response import SuccessResponse
    from ...models.unauthorised_response import UnauthorisedResponse

class IdsItemRequestBuilder(BaseRequestBuilder):
    """
    Builds and executes requests for operations under /ids/{id}
    """
    def __init__(self,request_adapter: RequestAdapter, path_parameters: Union[str, dict[str, Any]]) -> None:
        """
        Instantiates a new IdsItemRequestBuilder and sets the default values.
        param path_parameters: The raw url or the url-template parameters for the request.
        param request_adapter: The request adapter to use to execute the requests.
        Returns: None
        """
        super().__init__(request_adapter, "{+baseurl}/ids/{id}", path_parameters)
    
    async def delete(self,request_configuration: Optional[RequestConfiguration[QueryParameters]] = None) -> Optional[SuccessResponse]:
        """
        Removes all data for the identifier from the Noid and NoidMeta database tables
        param request_configuration: Configuration for the request such as headers, query parameters, and middleware options.
        Returns: Optional[SuccessResponse]
        """
        request_info = self.to_delete_request_information(
            request_configuration
        )
        from ...models.forbidden_response import ForbiddenResponse
        from ...models.not_found_response import NotFoundResponse
        from ...models.unauthorised_response import UnauthorisedResponse

        error_mapping: dict[str, type[ParsableFactory]] = {
            "401": UnauthorisedResponse,
            "403": ForbiddenResponse,
            "404": NotFoundResponse,
        }
        if not self.request_adapter:
            raise Exception("Http core is null") 
        from ...models.success_response import SuccessResponse

        return await self.request_adapter.send_async(request_info, SuccessResponse, error_mapping)
    
    async def get(self,request_configuration: Optional[RequestConfiguration[QueryParameters]] = None) -> Optional[Identifier]:
        """
        Returns a description of a single identifier
        param request_configuration: Configuration for the request such as headers, query parameters, and middleware options.
        Returns: Optional[Identifier]
        """
        request_info = self.to_get_request_information(
            request_configuration
        )
        from ...models.forbidden_response import ForbiddenResponse
        from ...models.not_found_response import NotFoundResponse
        from ...models.unauthorised_response import UnauthorisedResponse

        error_mapping: dict[str, type[ParsableFactory]] = {
            "401": UnauthorisedResponse,
            "403": ForbiddenResponse,
            "404": NotFoundResponse,
        }
        if not self.request_adapter:
            raise Exception("Http core is null") 
        from ...models.identifier import Identifier

        return await self.request_adapter.send_async(request_info, Identifier, error_mapping)
    
    async def patch(self,body: DeletionMetadata, request_configuration: Optional[RequestConfiguration[QueryParameters]] = None) -> Optional[Identifier]:
        """
        Updates the metadata associated with an existing identifier and marks its status as deleted
        param body: The request body
        param request_configuration: Configuration for the request such as headers, query parameters, and middleware options.
        Returns: Optional[Identifier]
        """
        if body is None:
            raise TypeError("body cannot be null.")
        request_info = self.to_patch_request_information(
            body, request_configuration
        )
        from ...models.forbidden_response import ForbiddenResponse
        from ...models.not_found_response import NotFoundResponse
        from ...models.unauthorised_response import UnauthorisedResponse

        error_mapping: dict[str, type[ParsableFactory]] = {
            "401": UnauthorisedResponse,
            "403": ForbiddenResponse,
            "404": NotFoundResponse,
        }
        if not self.request_adapter:
            raise Exception("Http core is null") 
        from ...models.identifier import Identifier

        return await self.request_adapter.send_async(request_info, Identifier, error_mapping)
    
    async def post(self,body: IdentifierMetadata, request_configuration: Optional[RequestConfiguration[QueryParameters]] = None) -> Optional[Identifier]:
        """
        Updates the metadata associated with an existing identifier
        param body: The request body
        param request_configuration: Configuration for the request such as headers, query parameters, and middleware options.
        Returns: Optional[Identifier]
        """
        if body is None:
            raise TypeError("body cannot be null.")
        request_info = self.to_post_request_information(
            body, request_configuration
        )
        from ...models.forbidden_response import ForbiddenResponse
        from ...models.not_found_response import NotFoundResponse
        from ...models.unauthorised_response import UnauthorisedResponse

        error_mapping: dict[str, type[ParsableFactory]] = {
            "401": UnauthorisedResponse,
            "403": ForbiddenResponse,
            "404": NotFoundResponse,
        }
        if not self.request_adapter:
            raise Exception("Http core is null") 
        from ...models.identifier import Identifier

        return await self.request_adapter.send_async(request_info, Identifier, error_mapping)
    
    def to_delete_request_information(self,request_configuration: Optional[RequestConfiguration[QueryParameters]] = None) -> RequestInformation:
        """
        Removes all data for the identifier from the Noid and NoidMeta database tables
        param request_configuration: Configuration for the request such as headers, query parameters, and middleware options.
        Returns: RequestInformation
        """
        request_info = RequestInformation(Method.DELETE, self.url_template, self.path_parameters)
        request_info.configure(request_configuration)
        request_info.headers.try_add("Accept", "application/json")
        return request_info
    
    def to_get_request_information(self,request_configuration: Optional[RequestConfiguration[QueryParameters]] = None) -> RequestInformation:
        """
        Returns a description of a single identifier
        param request_configuration: Configuration for the request such as headers, query parameters, and middleware options.
        Returns: RequestInformation
        """
        request_info = RequestInformation(Method.GET, self.url_template, self.path_parameters)
        request_info.configure(request_configuration)
        request_info.headers.try_add("Accept", "application/json")
        return request_info
    
    def to_patch_request_information(self,body: DeletionMetadata, request_configuration: Optional[RequestConfiguration[QueryParameters]] = None) -> RequestInformation:
        """
        Updates the metadata associated with an existing identifier and marks its status as deleted
        param body: The request body
        param request_configuration: Configuration for the request such as headers, query parameters, and middleware options.
        Returns: RequestInformation
        """
        if body is None:
            raise TypeError("body cannot be null.")
        request_info = RequestInformation(Method.PATCH, self.url_template, self.path_parameters)
        request_info.configure(request_configuration)
        request_info.headers.try_add("Accept", "application/json")
        request_info.set_content_from_parsable(self.request_adapter, "application/json", body)
        return request_info
    
    def to_post_request_information(self,body: IdentifierMetadata, request_configuration: Optional[RequestConfiguration[QueryParameters]] = None) -> RequestInformation:
        """
        Updates the metadata associated with an existing identifier
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
    
    def with_url(self,raw_url: str) -> IdsItemRequestBuilder:
        """
        Returns a request builder with the provided arbitrary URL. Using this method means any other path or query parameters are ignored.
        param raw_url: The raw URL to use for the request builder.
        Returns: IdsItemRequestBuilder
        """
        if raw_url is None:
            raise TypeError("raw_url cannot be null.")
        return IdsItemRequestBuilder(self.request_adapter, raw_url)
    
    @dataclass
    class IdsItemRequestBuilderDeleteRequestConfiguration(RequestConfiguration[QueryParameters]):
        """
        Configuration for the request such as headers, query parameters, and middleware options.
        """
        warn("This class is deprecated. Please use the generic RequestConfiguration class generated by the generator.", DeprecationWarning)
    
    @dataclass
    class IdsItemRequestBuilderGetRequestConfiguration(RequestConfiguration[QueryParameters]):
        """
        Configuration for the request such as headers, query parameters, and middleware options.
        """
        warn("This class is deprecated. Please use the generic RequestConfiguration class generated by the generator.", DeprecationWarning)
    
    @dataclass
    class IdsItemRequestBuilderPatchRequestConfiguration(RequestConfiguration[QueryParameters]):
        """
        Configuration for the request such as headers, query parameters, and middleware options.
        """
        warn("This class is deprecated. Please use the generic RequestConfiguration class generated by the generator.", DeprecationWarning)
    
    @dataclass
    class IdsItemRequestBuilderPostRequestConfiguration(RequestConfiguration[QueryParameters]):
        """
        Configuration for the request such as headers, query parameters, and middleware options.
        """
        warn("This class is deprecated. Please use the generic RequestConfiguration class generated by the generator.", DeprecationWarning)
    

