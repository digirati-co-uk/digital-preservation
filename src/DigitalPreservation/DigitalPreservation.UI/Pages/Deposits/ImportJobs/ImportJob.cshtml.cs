using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.UI.Features.Preservation.Requests;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigitalPreservation.UI.Pages.Deposits.ImportJobs;

public class ImportJobModel(IMediator mediator) : PageModel
{
    public async Task OnGet(string depositId, string importJobId)
    {
        ImportJobId = importJobId;
        if (importJobId == "diff")
        {
            ViewData["Title"] = "Diff for deposit " + depositId;
            var result = await mediator.Send(new GetDiffImportJob(depositId));
            if (result.Success)
            {
                ImportJob = result.Value;
                ViewData["Title"] = $"Diff from {depositId} to {ImportJob!.ArchivalGroupName ?? ImportJob.ArchivalGroup.GetPathUnderRoot()}";
                return;
            }
            TempData["Error"] = result.CodeAndMessage();
        }
        else if (importJobId == "custom")
        {
            ViewData["Title"] = "To be implemented" ;
            // TODO: generate the page UI to submit a custom job
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
        [FromRoute] string importJobId)
    {
        var result = await mediator.Send(new SendDiffImportJob(depositId));
        if (result.Success)
        {
            var importJobResult = result.Value;
            return Redirect($"/deposits/{depositId}/importjobs/results/{importJobResult!.Id!.GetSlug()}");
        }
        
        TempData["Error"] = result.CodeAndMessage();
        return Redirect($"/deposits/{depositId}/importjobs/diff");
    }

    public string ImportJobId { get; set; }

    public ImportJob? ImportJob { get; set; }
    
    public ImportJobResult? ImportJobResult { get; set; }
}