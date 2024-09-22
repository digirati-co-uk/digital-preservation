using DigitalPreservation.Common.Model;
using DigitalPreservation.UI.Features.Repository.Requests;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigitalPreservation.UI.Pages;

public class IndexModel(IMediator mediator) : PageModel
{
    [BindProperty] public Container? RepositoryRoot { get; set; }
    
    public async Task OnGet()
    {
        RepositoryRoot = await mediator.Send(new GetResource("repository")) as Container;
    }
}