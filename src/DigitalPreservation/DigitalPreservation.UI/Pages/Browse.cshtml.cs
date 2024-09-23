using DigitalPreservation.Common.Model;
using DigitalPreservation.UI.Features.Repository.Requests;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigitalPreservation.UI.Pages;

public class BrowseModel(IMediator mediator) : PageModel
{
    // Assumes Resource is always a container for now
    [BindProperty] public Container? Resource { get; set; }
    
    public async Task OnGet(string? path)
    {
        var resourcePath = $"{PreservedResource.BasePathElement}/{path ?? string.Empty}";
        Resource = await mediator.Send(new GetResource(resourcePath)) as Container;
    }
}