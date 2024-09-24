using DigitalPreservation.Common.Model;
using DigitalPreservation.Core.Utils;
using DigitalPreservation.UI.Features.Repository.Requests;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigitalPreservation.UI.Pages;

public class BrowseModel(IMediator mediator) : PageModel
{
    // Assumes Resource is always a container for now
    [BindProperty] public Container? Resource { get; set; }
    
    public async Task OnGet(string? pathUnderRoot)
    {
        await BindResource(pathUnderRoot);
    }

    private async Task BindResource(string? pathUnderRoot)
    {
        var resourcePath = $"{PreservedResource.BasePathElement}/{pathUnderRoot ?? string.Empty}";
        Resource = await mediator.Send(new GetResource(resourcePath)) as Container;
    }


    public async Task OnPost(string? pathUnderRoot, string? containerSlug, string? containerTitle)
    {
        if (containerSlug.IsNullOrWhiteSpace())
        {
            TempData["CreateContainerError"] = "Missing container name";
            await BindResource(pathUnderRoot);
            return;
        }
        
        var slug = containerSlug.ToLowerInvariant();
        if (ValidateNewContainer(pathUnderRoot, slug, containerTitle))
        {
            var newContainer = await mediator.Send(new CreateContainer(pathUnderRoot, slug, containerTitle));
        }
        else
        {
            TempData["CreateContainerError"] = "Invalid container name";
        }
        await BindResource(pathUnderRoot);
    }

    private bool ValidateNewContainer(string? path, string slug, string? containerTitle)
    {
        return PreservedResource.ValidSlug(slug);
    }
}