using DigitalPreservation.UI.TagHelpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace DigitalPreservation.UI.Tests.TagHelpers;

/// <summary>
/// StatusMessage centralises the alert-banner markup and the raw-vs-encoded rendering decision
/// that six pages used to repeat, each slightly differently (issue #388 item 4.2). These pin that
/// a missing key renders nothing, that a plain message is HTML-encoded by default, that `raw`
/// preserves deliberately-embedded markup, and that the dismiss button only appears when asked for.
/// </summary>
public class StatusMessageTests
{
    private static (TagHelperContext context, TagHelperOutput output) MakeTagHelper()
    {
        var context = new TagHelperContext(
            tagName: "status-message",
            allAttributes: new TagHelperAttributeList(),
            items: new Dictionary<object, object>(),
            uniqueId: "test");

        var output = new TagHelperOutput(
            "status-message",
            attributes: new TagHelperAttributeList(),
            getChildContentAsync: (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        return (context, output);
    }

    private static ViewContext MakeViewContext(ITempDataDictionary tempData) =>
        new() { TempData = tempData };

    private static ITempDataDictionary MakeTempData(string? key = null, string? value = null)
    {
        var tempData = new TempDataDictionary(new DefaultHttpContext(), new NullTempDataProvider());
        if (key != null)
        {
            tempData[key] = value;
        }
        return tempData;
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    [Fact]
    public void Suppresses_output_when_key_is_not_set()
    {
        var (context, output) = MakeTagHelper();
        var tag = new StatusMessage { ViewContext = MakeViewContext(MakeTempData()), Key = "Missing" };

        tag.Process(context, output);

        output.TagName.Should().BeNull("SuppressOutput clears the tag name");
    }

    [Fact]
    public void Html_encodes_a_plain_message_by_default()
    {
        var (context, output) = MakeTagHelper();
        var tag = new StatusMessage
        {
            ViewContext = MakeViewContext(MakeTempData("Error", "<script>alert(1)</script>")),
            Key = "Error",
            Variant = "danger"
        };

        tag.Process(context, output);

        var html = output.Content.GetContent();
        html.Should().Contain("&lt;script&gt;");
        html.Should().NotContain("<script>");
        output.Attributes["class"].Value.Should().Be("alert alert-danger alert-dismissible fade show");
    }

    [Fact]
    public void Preserves_markup_when_raw_is_true()
    {
        var (context, output) = MakeTagHelper();
        var tag = new StatusMessage
        {
            ViewContext = MakeViewContext(MakeTempData("CreateDepositFail", "There is already a deposit.<br/><a href=\"/deposits/x\">x</a>")),
            Key = "CreateDepositFail",
            Variant = "danger",
            Raw = true
        };

        tag.Process(context, output);

        output.Content.GetContent().Should().Contain("<br/><a href=\"/deposits/x\">x</a>");
    }

    [Fact]
    public void Omits_the_dismiss_button_and_classes_when_not_dismissible()
    {
        var (context, output) = MakeTagHelper();
        var tag = new StatusMessage
        {
            ViewContext = MakeViewContext(MakeTempData("Error", "Something went wrong")),
            Key = "Error",
            Variant = "danger",
            Dismissible = false
        };

        tag.Process(context, output);

        output.Attributes["class"].Value.Should().Be("alert alert-danger");
        output.Content.GetContent().Should().NotContain("btn-close");
    }

    [Fact]
    public void Includes_the_dismiss_button_and_classes_when_dismissible()
    {
        var (context, output) = MakeTagHelper();
        var tag = new StatusMessage
        {
            ViewContext = MakeViewContext(MakeTempData("Valid", "Deposit locked.")),
            Key = "Valid",
            Variant = "success"
        };

        tag.Process(context, output);

        output.Attributes["class"].Value.Should().Be("alert alert-success alert-dismissible fade show");
        output.Content.GetContent().Should().Contain("btn-close");
    }
}
