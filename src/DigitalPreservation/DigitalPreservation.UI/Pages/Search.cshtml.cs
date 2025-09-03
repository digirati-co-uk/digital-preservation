using DigitalPreservation.Common.Model.Search;
using DigitalPreservation.UI.Features.Repository.Requests;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;


namespace DigitalPreservation.UI.Pages;

public class SearchModel(IMediator mediator) : PageModel
{

    [BindProperty]
    public SearchCollection SearchModelData { get; set; } = new SearchCollection();

    

    public async Task OnGet(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            // Default to first page with default page size
            await GetResults(text, 0, 50);
        }
    }



    public async Task OnPostSearchAsync(string text, int page = 0, int pageSize = 50)
    {
        await GetResults(text, page, pageSize);
    }

    public async Task OnPostPageChangeAsync(int page, string text)
    {
        await GetResults(text, page, SearchModelData.pageSize ?? 50);
    }


    private async Task GetResults(string text, int page = 0, int pageSize = 50)
    {
        ModelState.Clear();

        if (string.IsNullOrWhiteSpace(text))
        {
            ModelState.AddModelError("Search", "Please enter search text");
            SearchModelData = new SearchCollection();
            return;
        }

        var searchResults = await mediator.Send(new SearchRequest(text, page, pageSize));
        var result = searchResults.Value ?? new SearchCollection();
        
        result.text = text;
        result.pageSize = pageSize;
        result.page = page;

        SearchModelData = result;
    }

}

