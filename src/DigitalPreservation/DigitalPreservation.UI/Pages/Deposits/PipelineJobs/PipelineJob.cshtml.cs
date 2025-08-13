using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.UI.Features.Preservation.Requests;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigitalPreservation.UI.Pages.Deposits.PipelineJobs;

public class PipelineJobModel(IMediator mediator) : PageModel
{
    public async Task OnGet(string depositId, string pipelineJobId)
    {
        PipelineJobId = pipelineJobId;

        ViewData["Title"] = "Import Job Result " + pipelineJobId;
        var result = await mediator.Send(new GetPipelineJobResult(depositId, pipelineJobId));
        if (result.Success)
        {
            // Display the existing job, which may be running or may not
            PipelineJobResult = result.Value;
            return;
        }
        TempData["Error"] = result.CodeAndMessage();
    }


    public string? PipelineJobId { get; set; }

    public PipelineJob? PipelineJob { get; set; }
    
    public ProcessPipelineResult? PipelineJobResult { get; set; }
}