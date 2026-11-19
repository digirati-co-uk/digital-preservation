using System.Net;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace DigitalPreservation.UI.TagHelpers;

/// <summary>
/// Renders one TempData-backed status banner - the markup six pages used to repeat for their own
/// TempData keys, each slightly differently (issue #388 item 4.2). Renders nothing when the key
/// isn't set.
/// </summary>
[HtmlTargetElement("status-message")]
public class StatusMessage : TagHelper
{
    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    /// <summary>The TempData key to read - one of <see cref="Infrastructure.TempDataKeys"/>.</summary>
    public required string Key { get; set; }

    /// <summary>Bootstrap alert variant: "danger" or "success".</summary>
    public string Variant { get; set; } = "success";

    /// <summary>Whether the alert gets a dismiss button.</summary>
    public bool Dismissible { get; set; } = true;

    /// <summary>
    /// True only for a message a handler deliberately built as HTML (a link, a line break).
    /// Every other message is HTML-encoded here - the one place that decision is made, instead of
    /// the WebUtility.HtmlEncode calls individual handlers used to remember, or not, to make.
    /// </summary>
    public bool Raw { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var value = ViewContext.TempData[Key] as string;
        if (string.IsNullOrEmpty(value))
        {
            output.SuppressOutput();
            return;
        }

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;

        var cssClass = $"alert alert-{Variant}";
        if (Dismissible)
        {
            cssClass += " alert-dismissible fade show";
        }
        output.Attributes.SetAttribute("class", cssClass);
        output.Attributes.SetAttribute("role", "alert");

        var content = Raw ? value : WebUtility.HtmlEncode(value);
        var closeButton = Dismissible
            ? """<button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>"""
            : "";
        output.Content.SetHtmlContent($"<p>{content}</p>{closeButton}");
    }
}
