using System.Text.Encodings.Web;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Workspace;
using Microsoft.AspNetCore.Mvc;
using Preservation.Client;
using Storage.Repository.Common;

namespace DigitalPreservation.UI.Controllers;

[Route("deposits/{id}/tool-output")]
public class DepositToolOutputController(
    IPreservationApiClient preservationApiClient,
    IStorage storage) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Get([FromRoute] string id, [FromQuery] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return BadRequest();

        var depositResult = await preservationApiClient.GetDeposit(id, CancellationToken.None);
        if (!depositResult.Success || depositResult.Value?.Files == null)
            return NotFound();

        var metadataReader = await MetadataReader.Create(storage, depositResult.Value.Files);
        var workingFile = new WorkingFile { LocalPath = path };
        metadataReader.Decorate(workingFile);

        var toolOutput = workingFile.Metadata?.OfType<ToolOutput>().FirstOrDefault();
        if (toolOutput == null)
            return NotFound();

        var encodedPath = HtmlEncoder.Default.Encode(path);
        var header = $$"""
            <style>body { padding-top: 4em; }</style>
            <header>
                <p>{{encodedPath}}</p>
                <nav>...in deposit <a href="/deposits/{{id}}">{{id}}</a></nav>
            </header>
            """;
        var html = toolOutput.Content
            .Replace("<title>Metadata report</title>", $"<title>{encodedPath} - Metadata report</title>")
            .Replace("<body>", $"<body>{header}");
        return Content(html, toolOutput.ContentType);
    }
}
