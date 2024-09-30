using DigitalPreservation.Common.Model;
using DigitalPreservation.UI.Features.Preservation;
using DigitalPreservation.UI.Features.Preservation.Requests;
using DigitalPreservation.UI.Features.Repository.Requests;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigitalPreservation.UI.Pages;


public class DepositNewModel(IMediator mediator, ILogger<Deposits.IndexModel> logger) : PageModel
{
    public NewDepositModel? NewDeposit { get; set; }
    public void OnGet()
    {
        // list deposits
    }

    public async Task<IActionResult> OnPostCreate(NewDepositModel newDepositModel)
    {
        NewDeposit = newDepositModel;
        
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


    private async Task<bool> ValidateAndNormaliseNewDeposit(NewDepositModel newDepositModel)
    {
        newDepositModel.ArchivalGroupPathUnderRoot ??= StringUtils.BuildPath(false,
            newDepositModel.ArchivalGroupPathUnderRoot, newDepositModel.ArchivalGroupProposedName);

        var parentPath = newDepositModel.ArchivalGroupPathUnderRoot.GetParent();
        if (parentPath.IsNullOrWhiteSpace())
        {
            TempData["CreateDepositFail"] = "You can't create a deposit for an Archival Group at the repository root.";
            return false;
        }
        
        var archivalGroupRepositoryPath = newDepositModel.ArchivalGroupPathUnderRoot.GetRepositoryPath();
        var depositsForArchivalGroupResult = await mediator.Send(new GetDepositsForArchivalGroup(archivalGroupRepositoryPath));
        if (depositsForArchivalGroupResult.Success)
        {
            // Need to establish a contract that the active one (if there is one) will be first, then the rest, up to a limit (a big limit, eg 100)
            if (depositsForArchivalGroupResult.Value == null) // empty list is OK though, and expected a lot of the time
            {
                TempData["CreateDepositFail"] = "Can't obtain details of existing deposits for the archival group.";
                return false;
            }
            if(depositsForArchivalGroupResult.Value != null && depositsForArchivalGroupResult.Value.Count > 0)
        }
        
        var existingResourceResult = await mediator.Send(new GetResource(archivalGroupRepositoryPath));

        if (newDepositModel.ExpectedToBeNewArchivalGroup)
        {
            if (existingResourceResult.ErrorCode != ErrorCodes.NotFound || existingResourceResult.Value != null) // this is belt and braces really
            {
                TempData["CreateDepositFail"] = "There is already an archival group at " + archivalGroupRepositoryPath + ".<br/>" +
                                                "<a href=\"" + archivalGroupRepositoryPath + "\">" + archivalGroupRepositoryPath + "</a></br>" +
                                                "You can visit it to create a new Deposit (and optionally Export).";
                return false;
            }

            var parentRepositoryPath = parentPath.GetRepositoryPath();
            var parentResult = await mediator.Send(new GetResource(parentRepositoryPath));
            if(parentResult.ErrorCode == ErrorCodes.NotFound || parentResult.Value == null)
            {
                TempData["CreateDepositFail"] = "There is no parent resource at " + parentRepositoryPath + ".";
                return false;
            }
            var parent = parentResult.Value;
            if (parent is not Container)
            {
                TempData["CreateDepositFail"] = "The parent resource at " + parentRepositoryPath + " is not a Container.";
                return false;
            }

            if (parent.PartOf != null)
            {
                TempData["CreateDepositFail"] = "The parent resource is already inside an Archival Group:<br/>" +
                                                "See <a href=\"/browse/" + parent.PartOf.GetPathUnderRoot() + "\">parent.PartOf.GetPathUnderRoot()</a>";
                return false;
            }
        }
        else
        {
            if (existingResourceResult is { Value: ArchivalGroup, ErrorCode: not null })
            {
                return true;
            }
            TempData["CreateDepositFail"] = "We expected to create a deposit for an existing Archival Group," +
                                            "but " + archivalGroupRepositoryPath + " could not be found.<br/>";
            return false;
        }
        TempData["CreateDepositFail"] = "Reason for failure";
        return false;
    }
}