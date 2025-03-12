from __future__ import annotations
from collections.abc import Callable
from kiota_abstractions.base_request_builder import BaseRequestBuilder
from kiota_abstractions.get_path_parameters import get_path_parameters
from kiota_abstractions.request_adapter import RequestAdapter
from typing import Any, Optional, TYPE_CHECKING, Union

if TYPE_CHECKING:
    from .item.with_irn_item_request_builder import WithIrnItemRequestBuilder

class CatirnRequestBuilder(BaseRequestBuilder):
    """
    Builds and executes requests for operations under /catirn
    """
    def __init__(self,request_adapter: RequestAdapter, path_parameters: Union[str, dict[str, Any]]) -> None:
        """
        Instantiates a new CatirnRequestBuilder and sets the default values.
        param path_parameters: The raw url or the url-template parameters for the request.
        param request_adapter: The request adapter to use to execute the requests.
        Returns: None
        """
        super().__init__(request_adapter, "{+baseurl}/catirn", path_parameters)
    
    def by_irn(self,irn: str) -> WithIrnItemRequestBuilder:
        """
        Gets an item from the id_client.catirn.item collection
        param irn: EMu Internal Resource Number (IRN) used in different modules of the EMu Application
        Returns: WithIrnItemRequestBuilder
        """
        if irn is None:
            raise TypeError("irn cannot be null.")
        from .item.with_irn_item_request_builder import WithIrnItemRequestBuilder

        url_tpl_params = get_path_parameters(self.path_parameters)
        url_tpl_params["irn"] = irn
        return WithIrnItemRequestBuilder(self.request_adapter, url_tpl_params)
    

