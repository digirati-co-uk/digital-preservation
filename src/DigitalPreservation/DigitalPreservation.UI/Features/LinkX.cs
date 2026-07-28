using System.Net;
using Microsoft.AspNetCore.Html;

namespace DigitalPreservation.UI.Features;

public static class LinkX
{
    // Renders an <a> only when there's text to show it for. An <a> with no accessible
    // name (empty text, no aria-label) is a WCAG 4.1.2/link-name failure, and several
    // Container/Binary rows have no Name/LastModifiedBy/CreatedBy set.
    public static IHtmlContent LinkOrPlain(string? text, string href)
    {
        if (string.IsNullOrEmpty(text))
        {
            return HtmlString.Empty;
        }
        return new HtmlString($"<a href=\"{WebUtility.HtmlEncode(href)}\">{WebUtility.HtmlEncode(text)}</a>");
    }
}
