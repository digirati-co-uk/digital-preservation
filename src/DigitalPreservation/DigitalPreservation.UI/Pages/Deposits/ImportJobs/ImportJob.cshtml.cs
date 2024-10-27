using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.UI.Features.Preservation.Requests;
using MediatR;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigitalPreservation.UI.Pages.Deposits.ImportJobs;

public class ImportJobModel(IMediator mediator) : PageModel
{
    public async Task OnGet(string depositId, string importJobId)
    {
        ImportJobId = importJobId;
        if (importJobId == "diff")
        {
            // generate the page UI to submit the diff job
        }
        else if (importJobId == "custom")
        {
            // generate the page UI to submit a custom job
        }
        else
        {
            var result = await mediator.Send(new GetImportJobResult(depositId, importJobId));
            if (result.Success)
            {
                // Display the existing job, which may be running or may not
                ImportJobResult = result.Value;
            }
        }
    }

    public string ImportJobId { get; set; }
    
    public ImportJobResult? ImportJobResult { get; set; }
}