using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Core.Configuration;
using DigitalPreservation.UI.Features.Preservation.Requests;
using DigitalPreservation.UI.Infrastructure;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace DigitalPreservation.UI.Pages.Deposits.ImportJobs;

public class ImportJobModel(IMediator mediator, IOptions<FeatureFlagOptions> featureFlags) : PageModel
{
    /// <summary>
    /// Whether to offer "keep this out of the Activity Stream" alongside Preserve. The same flag
    /// that puts "Normalise METS IDs" on the deposit's Actions menu, because that is the change it
    /// is for - a migration done by hand should be able to leave the same clean history the bulk
    /// tool does.
    /// </summary>
    public bool ShowNormaliseMetsIds => featureFlags.Value.ShowNormaliseMetsIds;

    public async Task OnGet(string depositId, string importJobId, CancellationToken cancellationToken)
    {
        ImportJobId = importJobId;
        if (importJobId == "diff")
        {
            ViewData["Title"] = "Diff for deposit " + depositId;
            var result = await mediator.Send(new GetDiffImportJob(depositId), cancellationToken);
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
                RenameRefusalMessage = ImportJob.RenameRefusalMessage();
                ViewData["Title"] = $"Diff from {depositId} to {ImportJob.ArchivalGroupName ?? ImportJob.ArchivalGroup.GetPathUnderRoot()}";
                return;
            }
            TempData[TempDataKeys.Error] = result.CodeAndMessage() + (invalidSlugMessage != null ? $" ({invalidSlugMessage})" : "");
        }
        else if (importJobId == "custom")
        {
            ViewData["Title"] = "To be implemented" ;
            TempData[TempDataKeys.Error] = "Custom import Job UI not implemented yet";
        }
        else
        {
            ViewData["Title"] = "Import Job Result " + importJobId ;
            var result = await mediator.Send(new GetImportJobResult(depositId, importJobId), cancellationToken);
            if (result.Success)
            {
                // Display the existing job, which may be running or may not
                ImportJobResult = result.Value;
                return;
            }
            TempData[TempDataKeys.Error] = result.CodeAndMessage();
        }
    }

    public async Task<IActionResult> OnPostExecuteDiffDirect(
        [FromRoute] string depositId,
        [FromRoute] string importJobId,
        [FromForm] bool suppressActivityStreamEvent)
    {
        // Belt and braces: the checkbox is only rendered under the flag, but a posted form is not
        // where the decision should be trusted from.
        var suppress = suppressActivityStreamEvent && ShowNormaliseMetsIds;

        // Deliberately CancellationToken.None, not the request's own token (review feedback on
        // #418): SendDiffImportJob's round trip to the Preservation API hands the job to the
        // Storage API and then records it in the Preservation API's own database - once the
        // Storage API has accepted the job, it is already running, so a browser disconnect at that
        // exact moment must not be allowed to abort the follow-up save and leave the job queued in
        // Storage but unrecorded here (see issue #428, which fixes the same gap from the Preservation
        // API's side for every caller, not just this one). Every other call on this page is a single
        // round trip to one system, where cancelling on disconnect is the right default and stays on
        // the real token.
        var result = await mediator.Send(new SendDiffImportJob(depositId, suppress), CancellationToken.None);
        if (result.Success)
        {
            var importJobResult = result.Value;
            return Redirect($"/deposits/{depositId}/importjobs/{importJobResult!.Id!.GetSlug()}");
        }
        
        TempData[TempDataKeys.Error] = result.CodeAndMessage();
        return Redirect($"/deposits/{depositId}/importjobs/diff");
    }

    public string? ImportJobId { get; set; }

    public ImportJob? ImportJob { get; set; }
    public List<PreservedResource> ItemsWithInvalidSlugs { get; set; } = [];
    public string? ItemsWithInvalidSlugsMessage { get; set; }
    public List<Binary> AddedBinariesWithInvalidContentTypes { get; set; } = [];
    public string? RenameRefusalMessage { get; set; }
    
    public ImportJobResult? ImportJobResult { get; set; }
}