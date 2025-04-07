using DigitalPreservation.Common.Model.ChangeDiscovery;
using DigitalPreservation.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Preservation.Client;

namespace DigitalPreservation.UI.Pages;


public class Changes(IPreservationApiClient preservationApiClient) : PageModel
{
    public string? Stream { get; set; }
    public int? PageIndex { get; set; }
    
    public OrderedCollectionPage? OrderedCollectionPage { get; set; }
    public OrderedCollection? OrderedCollection { get; set; }
    
    public async Task<IActionResult> OnGet([FromRoute] string stream, [FromRoute] int? index = null)
    {
        Stream = stream;
        PageIndex = index;
        
        if (index.HasValue)
        {
            OrderedCollectionPage = await preservationApiClient.GetOrderedCollectionPage(stream, index.Value);
            ViewData["Title"] = $"Activity Page: {stream}/pages/{index.Value}";
        }
        else
        {
            OrderedCollection = await preservationApiClient.GetOrderedCollection(stream);
            ViewData["Title"] = $"Activity Collection: {stream}";
        }
        return Page();
    }

    public string GetPageLink(OrderedCollectionPage ocp)
    {
        var page = ocp.Id.GetSlug();
        return $"/changes/{Stream}/{page}";
    }
}