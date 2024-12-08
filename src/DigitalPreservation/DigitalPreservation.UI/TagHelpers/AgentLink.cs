using DigitalPreservation.Common.Model;
using DigitalPreservation.Utils;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace DigitalPreservation.UI.TagHelpers;

public class AgentLink : TagHelper
{
    public Uri? Uri { get; set; }
    
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (Uri == null)
        {
            output.TagName = "span";
            output.Content.SetHtmlContent("-");
        }
        else
        {
            var slug = Uri.GetSlug();
            output.TagName = "a";
            output.Attributes.SetAttribute("class", "dlip-agent");
            var path = $"/{Agent.BasePathElement}/{slug}";
            output.Attributes.SetAttribute("href", path);
            output.Content.SetHtmlContent(slug);
        }
    }
}