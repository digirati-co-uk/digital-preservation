using DigitalPreservation.Common.Model.Download;
using DigitalPreservation.Core.Auth;
using DigitalPreservation.Core.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Preservation.API.Features.Downloads;

/// <summary>
/// Downloading preserved files (RFC 011): ask for a file, wait for its copy to be ready, then take
/// a link to it. Every route is behind <see cref="DownloadGate"/>.
/// </summary>
[Route("[controller]")]
[ApiController]
[ServiceFilter<DownloadGate>]
public class DownloadsController(Downloads downloads) : ControllerBase
{
    [HttpPost(Name = "RequestDownload")]
    [ProducesResponseType<Download>(200, "application/json")]
    [ProducesResponseType<ProblemDetails>(403, "application/json")]
    [ProducesResponseType<ProblemDetails>(404, "application/json")]
    public async Task<IActionResult> RequestDownload([FromBody] NewDownload newDownload, CancellationToken cancellationToken) =>
        this.StatusResponseFromResult(await downloads.Request(newDownload, User.GetCallerIdentity(), cancellationToken));

    [HttpGet(Name = "ListDownloads")]
    [ProducesResponseType<List<Download>>(200, "application/json")]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(await downloads.List(cancellationToken));

    [HttpGet("{id}", Name = "GetDownload")]
    [ProducesResponseType<Download>(200, "application/json")]
    [ProducesResponseType<ProblemDetails>(404, "application/json")]
    public async Task<IActionResult> Get([FromRoute] string id, CancellationToken cancellationToken) =>
        this.StatusResponseFromResult(await downloads.Get(id, cancellationToken));

    /// <summary>A link to the file that works for a short time. Ask again for another.</summary>
    [HttpPost("{id}/link", Name = "IssueDownloadLink")]
    [ProducesResponseType<DownloadLink>(200, "application/json")]
    [ProducesResponseType<ProblemDetails>(404, "application/json")]
    [ProducesResponseType<ProblemDetails>(409, "application/json")]
    public async Task<IActionResult> IssueLink([FromRoute] string id, CancellationToken cancellationToken) =>
        this.StatusResponseFromResult(await downloads.IssueLink(id, User.GetCallerIdentity(), cancellationToken));

    /// <summary>Removes the copy of the file now. The record of the request stays.</summary>
    [HttpDelete("{id}", Name = "DeleteDownload")]
    [ProducesResponseType(204)]
    [ProducesResponseType<ProblemDetails>(404, "application/json")]
    public async Task<IActionResult> Delete([FromRoute] string id, CancellationToken cancellationToken) =>
        this.StatusResponseFromResult(await downloads.Delete(id, cancellationToken), 204);
}

/// <summary>Refuses, with a 403, any caller that <see cref="DownloadOptions.Roles"/> does not permit.</summary>
public class DownloadGate(IOptions<DownloadOptions> options) : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (options.Value.Permits(context.HttpContext.User))
        {
            return;
        }
        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = 403,
            Title = "Forbidden",
            Detail = "You do not have permission to download preserved files."
        }) { StatusCode = 403 };
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        // Nothing to do after the action.
    }
}
