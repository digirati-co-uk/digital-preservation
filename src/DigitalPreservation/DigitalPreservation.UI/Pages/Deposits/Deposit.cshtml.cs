using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigitalPreservation.UI.Pages.Deposits;

public class DepositModel : PageModel
{
    public required string Id { get; set; }
    
    public void OnGet([FromRoute] string id)
    {
        Id = id;
    }
}