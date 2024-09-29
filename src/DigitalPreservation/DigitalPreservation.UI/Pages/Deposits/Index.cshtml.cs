using DigitalPreservation.UI.Features.Preservation;
using DigitalPreservation.UI.Features.Preservation.Requests;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigitalPreservation.UI.Pages.Deposits;

public class IndexModel(IMediator mediator) : PageModel
{
    public void OnGet()
    {
        // list deposits
    }

    public async Task<IActionResult> OnPostAsync(NewDepositModel NewDepositModel)
    {
        if (ValidateAndNormaliseNewDeposit(NewDepositModel))
        {
            var result = await mediator.Send(new CreateDeposit(
                NewDepositModel.ArchivalGroupPathUnderRoot,
                NewDepositModel.ArchivalGroupProposedName,
                NewDepositModel.SubmissionText));
            if (result.Success)
            {
                TempData["CreateDepositSuccess"] = "Created new deposit";
                return Redirect("/deposits/" + result.Value!.Id!.GetSlug());
            }
            TempData["CreateDepositFail"] = $"{result.ErrorCode}: {result.ErrorMessage}";
        }
        return Redirect(Request.Path);
    }

    private bool ValidateAndNormaliseNewDeposit(NewDepositModel newDepositModel)
    {
        TempData["CreateDepositFail"] = "Reason for failure";
        return false;
    }
}