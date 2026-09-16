using System.Xml.Linq;
using DigitalPreservation.Mets;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;

namespace Preservation.API.Tests.Features.Repository;

/// <summary>
/// A path in a METS document becomes a LocalPath in the deposit's working tree, and a METS-only
/// entry's LocalPath becomes an S3 key to delete when tool output is refreshed. So a METS may not
/// name anything outside the deposit root. These take a real, parseable sample and corrupt one
/// path at a time.
/// </summary>
public class MetsParserPathTests
{
    private static readonly XNamespace Mets = "http://www.loc.gov/METS/";
    private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";
    private static readonly Uri MetsUri = new("s3://deposits/dep-1/mets.xml");

    private static XDocument Sample() => XDocument.Load(Path.Combine("Samples", "mets-sample-001.xml"));

    private static MetsParser Parser() => new(A.Fake<IMetsLoader>(), NullLogger<MetsParser>.Instance);

    [Fact]
    public void The_Sample_Parses_As_It_Is()
    {
        var result = Parser().GetMetsFileWrapperFromXDocument(MetsUri, Sample());

        result.Success.Should().BeTrue();
        result.Value!.Files.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("objects/../../other-deposit/objects/x.jpg")]
    [InlineData("../x.jpg")]
    [InlineData("objects/%2e%2e/x.jpg")]
    [InlineData("objects/./x.jpg")]
    [InlineData("objects\\x.jpg")]
    public void An_FLocat_That_Could_Leave_The_Deposit_Is_Refused(string href)
    {
        var doc = Sample();
        var flocat = doc.Descendants(Mets + "FLocat").First();
        flocat.SetAttributeValue(XLink + "href", href);

        var act = () => Parser().GetMetsFileWrapperFromXDocument(MetsUri, doc);

        act.Should().Throw<NotSupportedException>().WithMessage("*may not climb out of it*");
    }

    [Theory]
    [InlineData("objects/%2e%2e%2fother-deposit/x.jpg")]
    [InlineData("objects/%2E%2E%2Fother-deposit/x.jpg")]
    [InlineData("objects%2f..%2fx.jpg")]
    [InlineData("objects/%2e%2e%5cother-deposit/x.jpg")]
    [InlineData("objects%5c..%5cx.jpg")]
    public void An_Encoded_Separator_In_An_FLocat_Is_Refused(string href)
    {
        // Nothing downstream decodes a METS path, so these cannot traverse today; a boundary check
        // refuses the alternate spelling rather than depending on that staying true.
        var doc = Sample();
        var flocat = doc.Descendants(Mets + "FLocat").First();
        flocat.SetAttributeValue(XLink + "href", href);

        var act = () => Parser().GetMetsFileWrapperFromXDocument(MetsUri, doc);

        act.Should().Throw<NotSupportedException>().WithMessage("*may not climb out of it*");
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("objects/../../outside")]
    [InlineData("objects/%2e%2e/outside")]
    [InlineData("objects\\outside")]
    [InlineData("objects%2f..%2foutside")]
    public void An_OriginalName_That_Could_Leave_The_Deposit_Is_Refused(string name)
    {
        // premis:originalName is the other path the parser reads, and a directory entry's feeds FindDirectory.
        var doc = Sample();
        var originalName = doc.Descendants().First(e => e.Name.LocalName == "originalName" && e.Value.Contains('/'));
        originalName.Value = name;

        var act = () => Parser().GetMetsFileWrapperFromXDocument(MetsUri, doc);

        act.Should().Throw<NotSupportedException>().WithMessage("*premis:originalName*");
    }

    [Fact]
    public void An_Ordinary_FLocat_Is_Still_Accepted()
    {
        var doc = Sample();
        var flocat = doc.Descendants(Mets + "FLocat").First();
        flocat.SetAttributeValue(XLink + "href", "objects/new-test-folder-inside-objects/renamed().jpg");

        var result = Parser().GetMetsFileWrapperFromXDocument(MetsUri, doc);

        result.Success.Should().BeTrue();
    }
}
