using System.Net;
using DigitalPreservation.Common.Model.Search;
using DigitalPreservation.Core.Web;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.Repository.Requests;


namespace Preservation.API.Features.Search;


[Route("search")]
[ApiController]
public class SearchController(IMediator mediator) : Controller
{
    
    [HttpGet(Name = "Search")]
    [ProducesResponseType<SearchCollection>(200, "application/json")]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<SearchCollection?>> Search(
        string text,
        int? pageNumber = 0,
        int? pageSize = 50,
        SearchType type = SearchType.All,
        int? otherPage = 0
        )
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

        if (pageNumber < 0 || pageSize <= 0 || pageSize > 500)
        {
            var problem = new ProblemDetails
            {
                Status = (int)HttpStatusCode.BadRequest,
                Title = "Invalid paging parameters",
                Detail = "Page number and page size must be positive, and page size must be below 500."
            };
            return BadRequest(problem);
        }

        var result = await mediator.Send(new SearchRequest(text, pageNumber.Value, pageSize.Value, type, otherPage.Value));

        return this.StatusResponseFromResult(result);
    }
}
