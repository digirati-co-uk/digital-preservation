using DigitalPreservation.Common.Model.Search;
using DigitalPreservation.UI.Features.Repository.Requests;
using DigitalPreservation.UI.ViewComponents;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;



namespace DigitalPreservation.UI.Pages;

public class SearchModel(IMediator mediator) : PageModel
{
    private const int DefaultPageSize = 20;

    [BindProperty(SupportsGet=true)]
    public SearchCollection? SearchModelData { get; set; } // = new SearchCollection();


    [BindProperty(SupportsGet=true)]
    public PagerValues? DepositPagerValues { get; set; }


    [BindProperty(SupportsGet=true)]
    public PagerValues? FedoraPagerValues { get; set; }

    private void UpdatePageValues()
    {
        var depositIndex = 1;
        var fedoraIndex = 1;

        if (SearchModelData?.DepositSearch is not null)
        {
            var dVal = SearchModelData.DepositSearch.Page;
            depositIndex = (dVal ?? 0) + 1;
        }
        if (SearchModelData?.FedoraSearch is not null)
        {
            var dVal = SearchModelData.FedoraSearch.Page;
            fedoraIndex = (dVal ?? 0) + 1;
        }
        
        if (SearchModelData?.DepositSearch is not null)
        {
            DepositPagerValues
                = new PagerValues(
                    new QueryString(
                        $"?handler=PageChange&type={SearchType.Deposits}&otherPage={fedoraIndex}&text={SearchModelData.text?.EscapeForUri()}"),
                    SearchModelData.DepositSearch.Total ?? 0,
                    SearchModelData.DepositSearch.PageSize ?? 50)
                {
                    Index = depositIndex
                };
        }

        if (SearchModelData?.FedoraSearch is not null)
        {
            FedoraPagerValues
                = new PagerValues(
                    new QueryString(
                        $"?handler=PageChange&type={SearchType.Fedora}&otherPage={depositIndex}&text={SearchModelData.text?.EscapeForUri()}"),
                    SearchModelData.FedoraSearch.Total ?? 0,
                    SearchModelData.FedoraSearch.PageSize ?? 50)
                {
                    Index = fedoraIndex
                };
        }
    }
    
    public async Task OnGet(string? text = "") =>
        await GetResults(text?.Trim());
    
    public async Task OnPostSearchAsync(string? text, int page = 1, int pageSize = DefaultPageSize) =>
        await GetResults(text, page, SearchType.All, pageSize);
    

    public async Task OnGetPageChangeAsync([FromQuery] int page, [FromQuery] string text, [FromQuery] int otherPage,  [FromQuery] SearchType type) =>
        await GetResults(text, page, type, DefaultPageSize, otherPage);
    

    private async Task GetResults(string? text, int page = 1, SearchType type = SearchType.All, int pageSize = DefaultPageSize, int otherPage = 0)
    {
        try
        {
            ModelState.Clear();

            if (string.IsNullOrWhiteSpace(text))
            {
                SearchModelData = new SearchCollection();
                return;
            }

            await Validation(text, page, type, pageSize, otherPage);

            if (!ModelState.IsValid)
            {
                return;
            }

            var defaultPage = page > 0 ? page - 1 : page;
            var defaultOther = otherPage > 0 ? otherPage - 1 : otherPage;
            var searchResults = await mediator.Send(new SearchRequest(text, defaultPage, pageSize, type, defaultOther));
            var result = searchResults.Value ?? new SearchCollection();
            result.SearchType = type;
            result.text = text;
            SearchModelData = result;

            UpdatePageValues();
        }
        catch (Exception e)
        {
            ModelState.AddModelError(nameof(text), e.Message);
        }
    }


    private Task Validation(string text, int page, SearchType type , int pageSize, int otherPage)
    {
        if(text.Length > 500)
        {
            ModelState.AddModelError(nameof(text), "Search text too long, 500 max.");
        }

        if (page < 0)
        {
            ModelState.AddModelError(nameof(page), "Page number must be positive.");
        }
        if (otherPage < 0)
        {
            ModelState.AddModelError(nameof(otherPage), "OtherPage number must be positive.");
        }
        if (pageSize is <= 1 or > 500)
        {
            ModelState.AddModelError(nameof(pageSize), "Page size must be between 1 and 500.");
        }

        return Task.CompletedTask;
    }
}
