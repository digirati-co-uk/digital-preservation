using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.UI.Features.Preservation.Requests;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigitalPreservation.UI.Pages.Deposits;

public class DepositModel(IMediator mediator) : PageModel
{
    public required string Id { get; set; }
    
    public Deposit? Deposit { get; set; }
    
    public async Task OnGet([FromRoute] string id)
    {
        Id = id;
        var result = await mediator.Send(new GetDeposit(id));
        if (result.Success)
        {
            Deposit = result.Value;
        }
        else
        {
            TempData["Error"] = result.CodeAndMessage();
        }
    }
}