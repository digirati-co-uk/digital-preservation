using DigitalPreservation.Common.Model.Search;
using DigitalPreservation.UI.Features.Repository.Requests;
using DigitalPreservation.UI.ViewComponents;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.CodeAnalysis.CSharp.Syntax;


namespace DigitalPreservation.UI.Pages;

public class SearchModel(IMediator mediator) : PageModel
{
    private const int DefaultPageSize = 5;

    [BindProperty(SupportsGet=true)]
    public SearchCollection SearchModelData { get; set; } // = new SearchCollection();


    [BindProperty(SupportsGet=true)]
    public PagerValues? DepositPagerValues { get; set; }


    [BindProperty(SupportsGet=true)]
    public PagerValues? FedoraPagerValues { get; set; }

    private async Task UpdatePageValues()
    {
        var depositIndex = 1;
        var fedoraIndex = 1;

        if (SearchModelData.DepositSearch is not null)
        {
            var dVal = SearchModelData.DepositSearch.Page;
            depositIndex = (dVal ?? 0) + 1;
        }
        if (SearchModelData.FedoraSearch is not null)
        {
            var dVal = SearchModelData.FedoraSearch.Page;
            fedoraIndex = (dVal ?? 0) + 1;
        }


        if (SearchModelData.DepositSearch is not null)
        {
            DepositPagerValues
                = new PagerValues(
                    new QueryString(
                        $"?handler=PageChange&type={SearchType.Deposits}&otherpage={fedoraIndex}&text={SearchModelData.text?.EscapeForUri()}"),
                    SearchModelData.DepositSearch.Total ?? 0,
                    SearchModelData.DepositSearch.PageSize ?? 50);

            DepositPagerValues!.Index = SearchModelData.DepositSearch.Page.Value + 1;
        }

        if (SearchModelData.FedoraSearch is not null)
        {
            FedoraPagerValues
                = new PagerValues(
                    new QueryString(
                        $"?handler=PageChange&type={SearchType.Fedora}&otherpage={depositIndex}&text={SearchModelData.text?.EscapeForUri()}"),
                    SearchModelData.FedoraSearch.Total ?? 0,
                    SearchModelData.FedoraSearch.PageSize ?? 50);

            FedoraPagerValues!.Index = SearchModelData.FedoraSearch.Page.Value + 1;
        }
        
    }


    public async Task OnGet(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            // Default to first page with default page size
            await GetResults(text, 1, SearchType.All,DefaultPageSize);
        }
    }
    

    public async Task OnPostSearchAsync(string text, int page = 1, int pageSize = DefaultPageSize)
    {
        await GetResults(text, page, SearchType.All, pageSize);
    }

    public async Task OnGetPageChangeAsync([FromQuery] int page, [FromQuery] string text, [FromQuery] int otherpage,  [FromQuery] SearchType type)
    {
        await GetResults(text, page, type, DefaultPageSize, otherpage);
    }


    private async Task GetResults(string text, int page = 1, SearchType type = SearchType.All, int pageSize = DefaultPageSize, int otherpage = 0)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var defaultPage = page > 0 ? page - 1 : page;
        var searchResults = await mediator.Send(new SearchRequest(text, defaultPage, pageSize, type, otherpage - 1));
        var result = searchResults.Value ?? new SearchCollection();
        result.SearchType = type;

        result.text = text; 
        SearchModelData = result;
        
        await UpdatePageValues();

    }

}

