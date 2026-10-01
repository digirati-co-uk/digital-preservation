using DigitalPreservation.Common.Model;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Preservation.Client;

namespace DigitalPreservation.UI.Pages;

/// <summary>
/// Gives every agent-link href (AgentLink TagHelper) somewhere to go, rather than the 404 it got
/// before (issue #268 item 1). Built entirely from the route slug - there is no Agent resource in
/// the Preservation API to call.
/// </summary>
public class AgentModel(IOptions<PreservationOptions> preservationOptions) : PageModel
{
    public string Name { get; private set; } = string.Empty;
    public Uri AgentUri { get; private set; } = null!;

    public string CreatedByDepositsUrl { get; private set; } = string.Empty;
    public string LastModifiedByDepositsUrl { get; private set; } = string.Empty;
    public string PreservedByDepositsUrl { get; private set; } = string.Empty;
    public string ExportedByDepositsUrl { get; private set; } = string.Empty;

    public void OnGet(string slug)
    {
        // Routing has already unescaped the slug - the same unescaped name AgentLink displays, and
        // the same raw form ResourceMutator.GetAgentUri uses to mint the Agent URI in the first place.
        Name = slug;
        AgentUri = new Uri(preservationOptions.Value.Root, $"{Agent.BasePathElement}/{slug}");

        CreatedByDepositsUrl = DepositsUrl("createdBy", slug);
        LastModifiedByDepositsUrl = DepositsUrl("lastModifiedBy", slug);
        PreservedByDepositsUrl = DepositsUrl("preservedBy", slug);
        ExportedByDepositsUrl = DepositsUrl("exportedBy", slug);
    }

    // showAll=true so inactive deposits are included - an agent's only deposits may not be active.
    private static string DepositsUrl(string filterName, string name) =>
        QueryHelpers.AddQueryString("/deposits", new Dictionary<string, string?>
        {
            [filterName] = name,
            ["showAll"] = "true"
        });
}
