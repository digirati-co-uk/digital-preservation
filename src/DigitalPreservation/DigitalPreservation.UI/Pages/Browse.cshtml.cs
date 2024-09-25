using DigitalPreservation.Common.Model;
using DigitalPreservation.UI.Features.Repository.Requests;
using DigitalPreservation.Utils;
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
        var result = await mediator.Send(new GetResource(resourcePath));
        if (result.Success)
        {
            Resource = result.Value as Container;
        }
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
            var result = await mediator.Send(new CreateContainer(pathUnderRoot, slug, containerTitle));
            if(result is { Success: true, Value: not null })
            {
                TempData["ContainerCreated"] = "Created Container " + result.Value;
            }
            else
            {
                TempData["CreateContainerError"] = "Failed to create Container: " + result.ErrorCode + ": " + result.ErrorMessage;
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