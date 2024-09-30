using DigitalPreservation.UI.Features.Preservation;
using DigitalPreservation.UI.Features.Preservation.Requests;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigitalPreservation.UI.Pages;


public class DepositTestModel(IMediator mediator, ILogger<Deposits.IndexModel> logger) : PageModel
{
    public void OnGet()
    {
        // list deposits
    }

    public void OnPost()
    {
        logger.LogDebug("OnPost");
    }

    public void OnPost(NewDepositModel newDepositModel)
    {
        logger.LogDebug("OnPost(NewDepositModel newDepositModel)");
    }

    // public void OnPostNewDeposit()
    // {
    //     logger.LogDebug("OnPostNewDeposit()");
    // }
    

    public async Task<IActionResult> OnPostNewDeposit(NewDepositModel newDepositModel)
    {
        logger.LogDebug("OnPostNewDeposit(NewDepositModel newDepositModel)");
        if (ValidateAndNormaliseNewDeposit(newDepositModel))
        {
            var result = await mediator.Send(new CreateDeposit(
                newDepositModel.ArchivalGroupPathUnderRoot,
                newDepositModel.ArchivalGroupProposedName,
                newDepositModel.SubmissionText));
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