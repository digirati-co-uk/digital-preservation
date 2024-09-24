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


    public async Task<IActionResult> OnPost(string? pathUnderRoot, string? containerSlug, string? containerTitle)
    {
        if (containerSlug.IsNullOrWhiteSpace())
        {
            TempData["CreateContainerError"] = "Missing container name";
            await BindResource(pathUnderRoot);
            return Redirect(Request.Path);
        }
        
        var slug = containerSlug.ToLowerInvariant();
        if (ValidateNewContainer(pathUnderRoot, slug, containerTitle))
        {
            var newContainer = await mediator.Send(new CreateContainer(pathUnderRoot, slug, containerTitle));
            if (newContainer != null)
            {
                TempData["ContainerCreated"] = "Created Container " + newContainer;
            }
            else
            {
                TempData["CreateContainerError"] = "Failed to create Container";
            }
            return Redirect(Request.Path);
        }

        TempData["CreateContainerError"] = "Invalid container name";
        return Redirect(Request.Path);
    }

    private bool ValidateNewContainer(string? path, string slug, string? containerTitle)
    {
        return PreservedResource.ValidSlug(slug);
    }
}