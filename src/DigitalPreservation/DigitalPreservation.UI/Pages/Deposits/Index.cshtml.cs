using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.UI.Features.Preservation.Requests;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigitalPreservation.UI.Pages.Deposits;

public class IndexModel(IMediator mediator) : PageModel
{
    public List<Deposit> Deposits { get; set; } = [];


    // public void OnGet()
    // {
    //     Deposits =
    //     [
    //         new Deposit { Id = new Uri("https://example.com/deposits/1") }
    //     ];
    // }

    public async Task OnGet([FromQuery] DepositQuery? query)
    {
        var result = await mediator.Send(new GetDeposits(query));
        Deposits = result.Value!;
    }




}