using FluentAssertions;
using DigitalPreservation.Common.Model;
using DigitalPreservation.UI.Pages;

namespace DigitalPreservation.UI.Tests.Pages;

/// <summary>
/// The "Created Container" banner links to the new container. It is rendered raw, so whatever came
/// from the person creating it has to be encoded when the message is built.
/// </summary>
public class ContainerCreatedMessageTests
{
    [Fact]
    public void The_Name_Links_To_The_New_Container()
    {
        var container = new Container { Id = new Uri("https://preservation.test/repository/_for_testing/iiif"), Name = "IIIF tests" };

        BrowseModel.ContainerCreatedMessage(container)
            .Should().Be("Created Container 📁 <a href=\"/browse/_for_testing/iiif\">IIIF tests</a>");
    }

    [Fact]
    public void A_Name_Is_Encoded_Not_Rendered()
    {
        var container = new Container { Id = new Uri("https://preservation.test/repository/a/b"), Name = "<script>x</script> & co" };

        BrowseModel.ContainerCreatedMessage(container)
            .Should().Contain("&lt;script&gt;x&lt;/script&gt; &amp; co").And.NotContain("<script>");
    }

    [Fact]
    public void Without_A_Name_It_Uses_The_Path_Segment()
    {
        var container = new Container { Id = new Uri("https://preservation.test/repository/a/my-folder") };

        BrowseModel.ContainerCreatedMessage(container).Should().EndWith(">my-folder</a>");
    }
}
