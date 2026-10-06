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

        // Defaults are applied here, before validation, so nothing downstream ever sees an absent
        // page or pageSize - an unvalidated null previously reached the SQL as LIMIT NULL, which
        // Postgres treats as no limit at all (#272).
        var effectivePage = page ?? 0;
        var effectivePageSize = pageSize ?? 50;

        if (effectivePage < 0 || effectivePageSize <= 0 || effectivePageSize > 500)
        {
            var problem = new ProblemDetails
            {
                Status = (int)HttpStatusCode.BadRequest,
                Title = "Invalid paging parameters",
                Detail = "Page number must be zero or more, and page size must be between 1 and 500."
            };
            return BadRequest(problem);
        }


        var result = await mediator.Send(new SearchFromFedoraSimple(text, effectivePage, effectivePageSize));
        return this.StatusResponseFromResult(result);

    }

}
