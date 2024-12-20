using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.UI.Features.Preservation.Requests;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace DigitalPreservation.UI.Pages.Deposits;

public class IndexModel(IMediator mediator) : PageModel
{
    public List<Deposit> Deposits { get; set; } = [];
    
    public DepositQuery Query { get; set; } = new DepositQuery();

    public List<string> Agents { get; set; } = [];
    public string[] Statuses { get; set; } = DepositStates.All;

    public async Task<IActionResult> OnGet([FromQuery] DepositQuery? query)
    {
        // first, tidy the query string
        var qs = Request.QueryString.ToString();
        if (qs.EndsWith("=") || qs.Contains("=&"))
        {
            var mutated = MutateQuery(
                Request.Query["orderBy"].ToString(),
                Request.Query["ascending"].ToString().ToLowerInvariant() == "true",
                Request.Query["showAll"].ToString().ToLowerInvariant() == "true",
                Request.Query["showForm"].ToString().ToLowerInvariant() == "true"
            );
            return Redirect(mutated);
        }
        var result = await mediator.Send(new GetDeposits(query));
        if (query != null)
        {
            Query = query;
        }
        Deposits = result.Value!;
        
        var agentResult = await mediator.Send(new GetAllAgents());
        Agents = agentResult.Value!.Select(uri => uri.GetSlug()).OrderBy(s => s).ToList()!;
        return Page();
    }
    
    public string MutateQuery(string? orderBy, bool? ascending, bool showAll, bool showForm)
    {
        var queryDictionary = QueryHelpers.ParseQuery(Request.QueryString.Value);
        if (orderBy.HasText())
        {
            var obKey = queryDictionary.Keys.SingleOrDefault(k => k.ToLowerInvariant() == "orderby");
            if (obKey != null)
            {
                queryDictionary[obKey] = orderBy;
            }
            else
            {
                queryDictionary["orderby"] = orderBy;
            }
        }

        if (ascending is true)
        {
            queryDictionary["ascending"] = "true";
        }
        else
        {
            queryDictionary.Remove("ascending");
        }

        if (showAll)
        {
            queryDictionary["showAll"] = "true";
        }
        else
        {
            var saKey = queryDictionary.Keys.SingleOrDefault(k => k.ToLowerInvariant() == "showall");
            if (saKey.HasText())
            {
                queryDictionary.Remove(saKey);
            }
        }
        if (showForm)
        {
            queryDictionary["showForm"] = "true";
        }
        else
        {
            var sfKey = queryDictionary.Keys.SingleOrDefault(k => k.ToLowerInvariant() == "showform");
            if (sfKey.HasText())
            {
                queryDictionary.Remove(sfKey);
            }
        }
        var emptyKeys = queryDictionary.Keys.Where(
            k => queryDictionary[k] == StringValues.Empty || queryDictionary[k].ToString().IsNullOrWhiteSpace());
        foreach (var key in emptyKeys)
        {
            queryDictionary.Remove(key);
        }
        return QueryHelpers.AddQueryString($"deposits", queryDictionary);
    }




}