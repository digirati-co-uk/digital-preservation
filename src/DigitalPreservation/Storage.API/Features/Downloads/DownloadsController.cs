using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Download;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Core.Web;
using Microsoft.AspNetCore.Mvc;

namespace Storage.API.Features.Downloads;

/// <summary>
/// Copies of preserved files for people to download (RFC 011). The Preservation API decides who
/// may have a file and keeps the record; this makes the copy, signs links to it and removes it.
/// </summary>
[Route("[controller]")]
[ApiController]
public class DownloadsController(DownloadCopies copies, DownloadCopyQueue queue) : ControllerBase
{
    /// <summary>Starts a copy. It is ready when GET says so, usually within seconds.</summary>
    [HttpPost(Name = "StartDownloadCopy")]
    [ProducesResponseType(202)]
    public async Task<IActionResult> Start([FromBody] DownloadCopy copy, CancellationToken cancellationToken)
    {
        if (!copies.HasBucket)
        {
            return NoBucket();
        }

        var located = await copies.Locate(copy);
        if (located.Failure)
        {
            return this.StatusResponseFromResult(located);
        }

        await queue.Add(located.Value!, cancellationToken);
        return Accepted();
    }

    [HttpGet("{id}", Name = "GetDownloadCopyStatus")]
    [ProducesResponseType<DownloadCopyStatus>(200, "application/json")]
    public async Task<IActionResult> GetStatus([FromRoute] string id, CancellationToken cancellationToken)
    {
        if (!copies.HasBucket)
        {
            return NoBucket();
        }
        return Ok(new DownloadCopyStatus { Ready = await copies.IsReady(id, cancellationToken) });
    }

    [HttpGet("{id}/link", Name = "GetDownloadLink")]
    [ProducesResponseType<DownloadLink>(200, "application/json")]
    [ProducesResponseType<ProblemDetails>(404, "application/json")]
    public async Task<IActionResult> GetLink([FromRoute] string id, CancellationToken cancellationToken)
    {
        if (!copies.HasBucket)
        {
            return NoBucket();
        }
        var link = await copies.GetLink(id, cancellationToken);
        return link is null
            ? this.ProblemFor(ErrorCodes.NotFound, "No copy for download " + id)
            : Ok(link);
    }

    [HttpDelete("{id}", Name = "DeleteDownloadCopy")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Delete([FromRoute] string id, CancellationToken cancellationToken)
    {
        if (!copies.HasBucket)
        {
            return NoBucket();
        }
        await copies.Delete(id, cancellationToken);
        return NoContent();
    }

    private ActionResult NoBucket() =>
        this.ProblemFor(ErrorCodes.Unprocessable, "This Storage API has no downloads bucket configured.");
}
