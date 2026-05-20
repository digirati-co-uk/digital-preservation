using DigitalPreservation.Common.Model.Search;
using DigitalPreservation.Core.Web;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Storage.API.Features.Repository.Requests;
using System.Net;

namespace Storage.API.Features.Repository;


[Route(  "FedoraSearch/")]
[ApiController]
public class FedoraSearchController(IMediator mediator) : ControllerBase
{
    

    [HttpGet(Name = "GetSimpleSearch")]
    [ProducesResponseType<SearchResultFedora[]>(200, "application/json")]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]

    public async Task<ActionResult<SearchCollectiveFedora?>> GetSimpleSearch(
        string text,
        int? page = null, 
        int? pageSize = null)
    {

        if (string.IsNullOrWhiteSpace(text))
        {
            var problem = new ProblemDetails
            {
                Status = (int)HttpStatusCode.BadRequest,
                Title = "Missing search text",
                Detail = "A search text parameter is required."
            };
            return BadRequest(problem);
        }

        if (page< 0 || pageSize <= 0 || pageSize > 500)
        {
            var problem = new ProblemDetails
            {
                Status = (int)HttpStatusCode.BadRequest,
                Title = "Invalid paging parameters",
                Detail = "Page number and page size must be positive, and page size must be below 500."
            };
            return BadRequest(problem);
        }


        var result = await mediator.Send(new SearchFromFedoraSimple(text, page, pageSize));
        return this.StatusResponseFromResult(result);

    }

}
