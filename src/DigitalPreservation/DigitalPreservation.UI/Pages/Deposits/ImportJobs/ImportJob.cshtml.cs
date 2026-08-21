using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.UI.Features.Preservation.Requests;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigitalPreservation.UI.Pages.Deposits.ImportJobs;

public class ImportJobModel(IMediator mediator, IConfiguration configuration) : PageModel
{
    /// <summary>
    /// Whether to offer "keep this out of the Activity Stream" alongside Preserve. The same flag
    /// that puts "Normalise METS IDs" on the deposit's Actions menu, because that is the change it
    /// is for - a migration done by hand should be able to leave the same clean history the bulk
    /// tool does.
    /// </summary>
    public bool ShowNormaliseMetsIds =>
        configuration.GetValue<bool?>("FeatureFlags:ShowNormaliseMetsIds") ?? false;

    public async Task OnGet(string depositId, string importJobId)
    {
        ImportJobId = importJobId;
        if (importJobId == "diff")
        {
            ViewData["Title"] = "Diff for deposit " + depositId;
            var result = await mediator.Send(new GetDiffImportJob(depositId));
            List<PreservedResource> itemsWithInvalidSlugs = [];
            string? invalidSlugMessage = null;
            if (result.Value != null)
            {
                (itemsWithInvalidSlugs, invalidSlugMessage) = result.Value.ItemsWithInvalidSlugs();
            }
            if (result is { Success: true, Value: not null })
            {
                ImportJob = result.Value;
                ItemsWithInvalidSlugs = itemsWithInvalidSlugs;
                ItemsWithInvalidSlugsMessage = invalidSlugMessage;
                AddedBinariesWithInvalidContentTypes = ImportJob.AddedBinariesWithInvalidContentTypes();
                ViewData["Title"] = $"Diff from {depositId} to {ImportJob.ArchivalGroupName ?? ImportJob.ArchivalGroup.GetPathUnderRoot()}";
                return;
            }
            TempData["Error"] = result.CodeAndMessage() + (invalidSlugMessage != null ? $" ({invalidSlugMessage})" : "");
        }
        else if (importJobId == "custom")
        {
            ViewData["Title"] = "To be implemented" ;
            TempData["Error"] = "Custom import Job UI not implemented yet";
        }
        else
        {
            ViewData["Title"] = "Import Job Result " + importJobId ;
            var result = await mediator.Send(new GetImportJobResult(depositId, importJobId));
            if (result.Success)
            {
                // Display the existing job, which may be running or may not
                ImportJobResult = result.Value;
                return;
            }
            TempData["Error"] = result.CodeAndMessage();
        }
    }

    public async Task<IActionResult> OnPostExecuteDiffDirect(
        [FromRoute] string depositId,
        [FromRoute] string importJobId,
        [FromForm] bool suppressActivityStreamEvent = false)
    {
        // Belt and braces: the checkbox is only rendered under the flag, but a posted form is not
        // where the decision should be trusted from.
        var suppress = suppressActivityStreamEvent && ShowNormaliseMetsIds;
        var result = await mediator.Send(new SendDiffImportJob(depositId, suppress));
        if (result.Success)
        {
            var importJobResult = result.Value;
            return Redirect($"/deposits/{depositId}/importjobs/{importJobResult!.Id!.GetSlug()}");
        }
        
        TempData["Error"] = result.CodeAndMessage();
        return Redirect($"/deposits/{depositId}/importjobs/diff");
    }

    public string? ImportJobId { get; set; }

    public ImportJob? ImportJob { get; set; }
    public List<PreservedResource> ItemsWithInvalidSlugs { get; set; } = [];
    public string? ItemsWithInvalidSlugsMessage { get; set; }
    public List<Binary> AddedBinariesWithInvalidContentTypes { get; set; } = [];
    
    public ImportJobResult? ImportJobResult { get; set; }
}