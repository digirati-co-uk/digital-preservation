using System.Text;
using DigitalPreservation.Common.Model.Transit;
using FluentAssertions;

namespace DigitalPreservation.Common.Model.Tests.Transit;

/// <summary>
/// Issue #388 item 3.2: this parsing was previously copied word for word into
/// DigitalPreservation.Workspace's MetadataReader and Pipeline.API's ExecutePipelineJob, including
/// a shared bug - splitting every line on every space and keeping only lines that came out to
/// exactly two parts, which silently drops any path containing a space (something BagIt allows).
/// </summary>
public class BagItManifestTests
{
    [Fact]
    public void Parse_Maps_Path_To_Digest_For_Each_Line()
    {
        var manifest = "aaa111  objects/one.jpg\nbbb222  objects/two.jpg\n";

        var result = BagItManifest.Parse(manifest);

        result.Should().HaveCount(2);
        result["objects/one.jpg"].Should().Be("aaa111");
        result["objects/two.jpg"].Should().Be("bbb222");
    }

    [Fact]
    public void Parse_Keeps_A_Path_Containing_A_Space()
    {
        // The bug both previous copies had: splitting on every space and requiring exactly two
        // parts meant this line produced three parts and was silently dropped.
        var manifest = "aaa111  objects/file with a space.jpg\n";

        var result = BagItManifest.Parse(manifest);

        result.Should().ContainSingle();
        result["objects/file with a space.jpg"].Should().Be("aaa111");
    }

    [Fact]
    public void Parse_Tolerates_A_Single_Space_As_Well_As_BagIts_Usual_Two()
    {
        var manifest = "aaa111 objects/one.jpg\n";

        var result = BagItManifest.Parse(manifest);

        result["objects/one.jpg"].Should().Be("aaa111");
    }

    [Fact]
    public void Parse_Skips_Blank_Lines_And_Tolerates_CRLF()
    {
        var manifest = "aaa111  objects/one.jpg\r\n\r\n   \r\nbbb222  objects/two.jpg\r\n";

        var result = BagItManifest.Parse(manifest);

        result.Should().HaveCount(2);
        result["objects/one.jpg"].Should().Be("aaa111");
        result["objects/two.jpg"].Should().Be("bbb222");
    }

    [Fact]
    public void Parse_Skips_A_Line_With_No_Path()
    {
        var manifest = "aaa111\nbbb222  objects/two.jpg\n";

        var result = BagItManifest.Parse(manifest);

        result.Should().ContainSingle();
        result["objects/two.jpg"].Should().Be("bbb222");
    }

    [Fact]
    public async Task ParseAsync_Reads_The_Whole_Stream()
    {
        var bytes = Encoding.UTF8.GetBytes("aaa111  objects/one.jpg\n");
        using var stream = new MemoryStream(bytes);

        var result = await BagItManifest.ParseAsync(stream);

        result["objects/one.jpg"].Should().Be("aaa111");
    }
}
