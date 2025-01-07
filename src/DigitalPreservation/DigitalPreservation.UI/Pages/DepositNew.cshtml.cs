using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.UI.Features.Preservation;
using DigitalPreservation.UI.Features.Preservation.Requests;
using DigitalPreservation.UI.Features.Repository.Requests;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigitalPreservation.UI.Pages;


public class DepositNewModel(IMediator mediator, ILogger<DepositNewModel> logger) : PageModel
{
    public NewDepositModel? NewDeposit { get; set; }
    public void OnGet()
    {
        // list deposits
    }

    public async Task<IActionResult> OnPostCreateForArchivalGroup(string archivalGroupPath, bool export, string? submissionText)
    {
        var resourcePath = $"{PreservedResource.BasePathElement}/{archivalGroupPath}";
        var result = await mediator.Send(new GetResource(resourcePath));
        if (result.Success)
        {
            var model = new NewDepositModel
            {
                ArchivalGroupPathUnderRoot = archivalGroupPath,
                ArchivalGroupProposedName = result.Value!.Name!,
                SubmissionText = submissionText,
                Export = export
            };
            return await OnPostCreate(model);
        }
        TempData["CreateDepositFail"] = result.CodeAndMessage();
        return Page();
    }

    public async Task<IActionResult> OnPostCreate(NewDepositModel newDepositModel)
    {
        NewDeposit = newDepositModel;
        
        logger.LogDebug("OnPostNewDeposit(NewDepositModel newDepositModel)");
        if (await ValidateAndNormaliseNewDeposit(newDepositModel))
        {
            var result = await mediator.Send(new CreateDeposit(
                newDepositModel.ArchivalGroupPathUnderRoot,
                newDepositModel.ArchivalGroupProposedName,
                newDepositModel.SubmissionText,
                newDepositModel.UseObjectTemplate,
                newDepositModel.Export,
                exportVersion: null // always do this from UI; only supports exporting HEAD
                ));
            if (result.Success)
            {
                TempData["CreateDepositSuccess"] = "Created new deposit";
                return Redirect("/deposits/" + result.Value!.Id!.GetSlug());
            }
            TempData["CreateDepositFail"] = $"{result.CodeAndMessage()}";
        }
        return Redirect(Request.Path);
    }


    private async Task<bool> ValidateAndNormaliseNewDeposit(NewDepositModel newDepositModel)
    {
        if (newDepositModel.FromBrowseContext && newDepositModel.ArchivalGroupSlug.HasText())
        {
            newDepositModel.ArchivalGroupPathUnderRoot ??= StringUtils.BuildPath(false,
                newDepositModel.ParentPathUnderRoot, newDepositModel.ArchivalGroupSlug);
        }
        var pathUnderRoot = newDepositModel.ArchivalGroupPathUnderRoot;
        if (pathUnderRoot.IsNullOrWhiteSpace())
        {
            // nothing to validate, as an intended AG has not been specified
            return true;
        }

        var agSlug = pathUnderRoot.GetSlug();
        if(!PreservedResource.ValidSlug(agSlug))
        {
            TempData["CreateDepositFail"] = $"Not a valid path name: {agSlug}";
            return false;
        }
        var parentPath = pathUnderRoot.GetParent();
        if (parentPath.IsNullOrWhiteSpace())
        {
            TempData["CreateDepositFail"] = "You can't create a deposit for an Archival Group at the repository root.";
            return false;
        }
        if (!PreservedResource.ValidPath(pathUnderRoot))
        {
            TempData["CreateDepositFail"] = $"Path {pathUnderRoot} is invalid";
            return false;
        }
        
        var archivalGroupRepositoryPath = pathUnderRoot.GetRepositoryPath()!;
        var browsePath = "/browse/" + pathUnderRoot;
        var depositsForArchivalGroupResult = await mediator.Send(new GetDeposits(new DepositQuery{ArchivalGroupPath = archivalGroupRepositoryPath}));
        if (depositsForArchivalGroupResult.Success)
        {
            if (depositsForArchivalGroupResult.Value == null) // empty list is OK though, and expected a lot of the time
            {
                TempData["CreateDepositFail"] = "Can't obtain details of existing deposits for the archival group.";
                return false;
            }

            if (depositsForArchivalGroupResult.Value is { Count: > 0 })
            {
                var activeDeposit = depositsForArchivalGroupResult.Value.FirstOrDefault(d => d.Active); // should be SingleOrDefault
                if (activeDeposit is { Active: true })
                {
                    var depositPath = "/deposits/" + activeDeposit.Id!.GetSlug();
                    TempData["CreateDepositFail"] = "There is already an ACTIVE deposit for the archival group.<br/>" +
                                                    $"<a href=\"{depositPath}\">{depositPath}</a>";
                    return false;
                }
            }
        }
        else
        {
            TempData["CreateDepositFail"] = "Unable to query for existing deposits: " + depositsForArchivalGroupResult.CodeAndMessage();
            return false;
        }
        
        var existingResourceResult = await mediator.Send(new GetResource(archivalGroupRepositoryPath));

        if (newDepositModel.FromBrowseContext)
        {
            // User intends to make a NEW deposit
            if (existingResourceResult.ErrorCode != ErrorCodes.NotFound || existingResourceResult.Value != null) // this is belt and braces really
            {
                TempData["CreateDepositFail"] = "There is already an archival group at " + pathUnderRoot + ".<br/>" +
                                                "<a href=\"" + browsePath + "\">" + pathUnderRoot + "</a></br>" +
                                                "You can visit it to create a new Deposit (and optionally Export).";
                return false;
            }

            var parentRepositoryPath = parentPath.GetRepositoryPath();
            var parentResult = await mediator.Send(new GetResource(parentRepositoryPath!));
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

            return true;
        }

        if (existingResourceResult.Value is ArchivalGroup && existingResourceResult.ErrorCode.IsNullOrWhiteSpace())
        {
            return true;
        }
        if (existingResourceResult is { Value: ArchivalGroup, ErrorCode: not null })
        {
            return true;
        }
        TempData["CreateDepositFail"] = "We expected to create a deposit for an existing Archival Group," +
                                        "but " + archivalGroupRepositoryPath + " could not be found.<br/>";
        return false;
    }
}