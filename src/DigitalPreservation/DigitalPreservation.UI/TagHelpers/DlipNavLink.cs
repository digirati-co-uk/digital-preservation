using Microsoft.AspNetCore.Razor.TagHelpers;

namespace DigitalPreservation.UI.TagHelpers;

public class DlipNavLink : TagHelper
{
    public required string IconId { get; set; }
    public required string Label { get; set; }
    public required string Section { get; set; }
    public required string ActiveSection { get; set; }
    public required string Href { get; set; }
    
    // <a class="nav-link d-flex align-items-center gap-2 active" aria-current="page" href="/">
    //    <svg class="bi"><use xlink:href="#house-fill"/></svg>
    //    Dashboard
    // </a>
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        bool active = Section == ActiveSection;
        output.TagName = "a";
        const string cssClass = "nav-link d-flex align-items-center gap-2";
        output.Attributes.SetAttribute("class", active ? cssClass + " active" : cssClass);
        if (active)
        {
            output.Attributes.SetAttribute("aria-current", "page");
        }
        output.Attributes.SetAttribute("href", Href); 
        
        output.Content.SetHtmlContent($"""
                                          <svg class="bi"><use xlink:href="#{IconId}"/></svg>
                                          {Label}
                                      """);
    }
    
}