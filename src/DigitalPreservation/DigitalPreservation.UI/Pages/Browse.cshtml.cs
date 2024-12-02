using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.UI.Features.Preservation.Requests;
using DigitalPreservation.UI.Features.Repository.Requests;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigitalPreservation.UI.Pages;

public class BrowseModel(IMediator mediator) : PageModel
{
    public PreservedResource? Resource { get; set; }
    public string? PathUnderRoot { get; set; }
    public string? ArchivalGroupPath { get; set; }
    
    // When we are on an archival group
    public List<Deposit> Deposits { get; set; } = [];

    public async Task OnGet(string? pathUnderRoot)
    {
        var resourcePath = $"{PreservedResource.BasePathElement}/{pathUnderRoot ?? string.Empty}";
        var result = await mediator.Send(new GetResource(resourcePath));
        if (result.Success)
        {
            Resource = result.Value;
            PathUnderRoot = pathUnderRoot;
            var name = Resource!.Name ?? Resource!.Id!.GetSlug();
            switch (Resource!.Type)
            {
                case nameof(ArchivalGroup):
                    ViewData["Title"] = $"📦 {name}";
                    ArchivalGroupPath = PathUnderRoot;
                    var query = new DepositQuery
                    {
                        ArchivalGroupPath = PathUnderRoot,
                        OrderBy = DepositQuery.Created,
                        ShowAll = true
                    };
                    var depositsResult = await mediator.Send(new GetDeposits(query));
                    if (depositsResult.Success)
                    {
                        Deposits = depositsResult.Value ?? [];
                    }
                    break;
                case nameof(Container):
                    ViewData["Title"] = $"📁 {name}";
                    break;
                case nameof(Binary):
                    ViewData["Title"] = $"🗎 {name}";
                    break;
                case "RepositoryRoot":
                    ViewData["Title"] = $"Browse Repository";
                    break;
            }

            if (Resource.PartOf != null)
            {
                ArchivalGroupPath = Resource.PartOf.GetPathUnderRoot();
            }
        }
    }

    public async Task<IActionResult> OnPostDeleteContainerOutsideArchivalGroup(string? pathUnderRoot, bool purgeCheck)
    {
        throw new NotImplementedException();
        //var result = await mediator.Send(new DeleteContainer(pathUnderRoot, purgeCheck));
        
    }
    
    public async Task<IActionResult> OnPost(string? pathUnderRoot, string? containerSlug, string? containerTitle)
    {
        if (containerSlug.IsNullOrWhiteSpace())
        {
            TempData["CreateContainerError"] = "Missing container name";
            return Redirect(Request.Path);
        }
        
        var slug = containerSlug.ToLowerInvariant();
        if (ValidateNewContainer(pathUnderRoot, slug, containerTitle))
        {
            var result = await mediator.Send(new CreateContainer(pathUnderRoot, slug, containerTitle));
            if(result is { Success: true, Value: not null })
            {
                TempData["ContainerCreated"] = "Created Container " + result.Value;
            }
            else
            {
                TempData["CreateContainerError"] = "Failed to create Container: " + result.CodeAndMessage();
            }
            return Redirect(Request.Path);
        }

        TempData["CreateContainerError"] = "Invalid container file path - only a-z, 0-9 and .-_ are allowed.";
        return Redirect(Request.Path);
    }

    private bool ValidateNewContainer(string? path, string slug, string? containerTitle)
    {
        return PreservedResource.ValidSlug(slug);
    }
}