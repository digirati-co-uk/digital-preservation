using System.Xml;
using System.Xml.Serialization;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XmlGen.Tests.Experimental.Parsing;

/// <summary>
/// Samples/goobi-2026.xml is a modern Goobi/MyCoRe METS from Rostock University Library
/// (DFG-Viewer profile, "Goobi MyCoRe Importer 1.4.4", 2022) - a deliberately different
/// flavour from the older Wellcome Goobi fixtures. Key differences:
/// <list type="bullet">
/// <item>FLocat hrefs are ABSOLUTE web URLs (IIIF Image API endpoints, ALTO download URLs),
/// not deposit-relative paths - this is a presentation METS whose content lives behind web
/// services, not a transfer METS travelling with its files;</item>
/// <item>fileGrp USEs are DFG-Viewer vocabulary (DEFAULT/FULLTEXT/THUMBS/DOWNLOAD/TEASER),
/// not the OBJECTS/ALTO the Wellcome fixtures use;</item>
/// <item>no PREMIS anywhere: the single amdSec holds DFG-Viewer dv:rights/dv:links; no
/// digests, sizes or formats exist for any file;</item>
/// <item>the physSequence root div carries its own fptrs (DOWNLOAD/TEASER).</item>
/// </list>
/// These tests document what our code currently makes of it - including the current
/// limitation that MetsParser cannot yet extract a physical structure from it.
/// </summary>
public class ParseGoobiRostock2026
{
    private readonly MetsParser parser;

    public ParseGoobiRostock2026()
    {
        var serviceProvider = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        var factory = serviceProvider.GetService<ILoggerFactory>();
        var parserLogger = factory!.CreateLogger<MetsParser>();
        var metsLoader = new FileSystemMetsLoader();
        parser = new MetsParser(metsLoader, parserLogger);
    }

    private static DigitalPreservation.XmlGen.Mets.Mets LoadTyped()
    {
        var serializer = new XmlSerializer(typeof(DigitalPreservation.XmlGen.Mets.Mets));
        using var reader = XmlReader.Create("Samples/goobi-2026.xml");
        return (DigitalPreservation.XmlGen.Mets.Mets)serializer.Deserialize(reader)!;
    }

    [Fact]
    public void Typed_Model_Deserialises_The_Dfg_Viewer_Mets()
    {
        var mets = LoadTyped();

        mets.MetsHdr.Agent[0].Name.Should().Be("Rostock University Library");
        mets.StructMap.Select(sm => sm.Type).Should().BeEquivalentTo("PHYSICAL", "LOGICAL");

        // DFG-Viewer fileGrp vocabulary, not the Wellcome OBJECTS/ALTO
        mets.FileSec.FileGrp.Select(g => (g.Use, g.File.Count)).Should().BeEquivalentTo(
        [
            ("FULLTEXT", 29), ("DEFAULT", 29), ("THUMBS", 29), ("DOWNLOAD", 1), ("TEASER", 1)
        ]);

        // Every FLocat href is an absolute web URL - nothing is deposit-relative
        mets.FileSec.FileGrp
            .SelectMany(g => g.File)
            .Select(f => f.FLocat[0].Href)
            .Should().OnlyContain(href => href!.StartsWith("https://"));

        // The physSequence root div carries its own fptrs (DOWNLOAD and TEASER)
        var physRoot = mets.StructMap.Single(sm => sm.Type == "PHYSICAL").Div;
        physRoot.Type.Should().Be("physSequence");
        physRoot.Fptr.Should().HaveCount(2);
        physRoot.Div.Should().HaveCount(29).And.OnlyContain(d => d.Type == "page");

        // No PREMIS: one amdSec holding DFG-Viewer rights/links only
        mets.AmdSec.Should().ContainSingle().Which.Id.Should().Be("AMD_DFGVIEWER");
        mets.AmdSec[0].TechMd.Should().BeEmpty();

        // Logical structure: monograph with GROUPID'd MODS dmdSec
        var logRoot = mets.StructMap.Single(sm => sm.Type == "LOGICAL").Div;
        logRoot.Type.Should().Be("monograph");
        logRoot.Dmdid.Should().ContainSingle().Which.Should().Be("DMDLOG_0000");
        logRoot.Div.Should().HaveCount(5);
    }

    [Fact]
    public async Task MetsParser_Cannot_Yet_Extract_Physical_Structure_From_Absolute_Url_FLocats()
    {
        // CURRENT LIMITATION, documented deliberately: the parser derives the directory tree
        // from FLocat hrefs as deposit-relative paths. This METS's hrefs are absolute IIIF /
        // download URLs, so file-to-directory assignment fails and the parse returns a clean
        // failure (not an unhandled exception). Supporting presentation-style METS like this
        // would need a decision about what "physical structure" even means when the content
        // is remote - tracked as future parser work, out of scope for issue #188.
        var goobiMets = new FileInfo("Samples/goobi-2026.xml");

        var result = await parser.GetMetsFileWrapper(new Uri(goobiMets.FullName));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Path_Cache_Reports_The_Dfg_Viewer_Mets_As_Not_Navigable_By_Path()
    {
        // The issue #188 path cache is also the seed of an editability conformance check:
        // a METS is editable-by-path only when every physical div resolves to a unique
        // deposit-relative path. This METS is READABLE (typed model above) but none of its
        // page divs resolve a path - each gets a diagnostic instead. (Third-party METS never
        // reaches MetsManager anyway - the mets:agent gate - but these diagnostics are what
        // a future conformance-based editability check would report.)
        var fullMets = new FullMets
        {
            Mets = LoadTyped(),
            Uri = new Uri("file:///test/goobi-2026.xml")
        };

        var diagnostics = MetsCache.Populate(fullMets);

        fullMets.PhysicalDivsByPath.Should().BeEmpty();
        diagnostics.Should().HaveCount(29)
            .And.OnlyContain(d => d.Contains("unrecognised TYPE 'page'"));
    }
}
