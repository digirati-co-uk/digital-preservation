using System.Xml.Linq;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XmlGen.Tests;

/// <summary>
/// Issue #508: a duration (or width and height) a person enters for a file the pipeline couldn't
/// measure. Written to the file's MODS as supplied="yes" extents, read back as the file's
/// EnteredExtent, never as measured metadata; a measured value always wins.
/// </summary>
public class EnteredExtentTests
{
    private static readonly XNamespace ModsNs = "http://www.loc.gov/mods/v3";

    private readonly MetsParser parser;
    private readonly MetsManager metsManager;

    public EnteredExtentTests()
    {
        var factory = new ServiceCollection().AddLogging().BuildServiceProvider().GetRequiredService<ILoggerFactory>();
        parser = new MetsParser(new FileSystemMetsLoader(), factory.CreateLogger<MetsParser>());
        var metadataManager = new MetadataManager(new PremisManager(), new PremisManagerExif(), new PremisEventManagerVirus());
        metsManager = new MetsManager(parser, new FileSystemMetsStorage(parser), metadataManager);
    }

    private async Task<(Uri MetsUri, FullMets FullMets)> CreateMets(string name, ExtentMetadata? measured = null)
    {
        var metsUri = new Uri(new FileInfo($"Outputs/{name}.xml").FullName);
        var created = await metsManager.CreateStandardMets(metsUri, "Entered extent test");
        var fullMets = (await metsManager.GetFullMets(metsUri, created.Value!.ETag!)).Value!;
        var tape = new WorkingFile
        {
            LocalPath = "objects/tape.mp3", Name = "tape.mp3", ContentType = "audio/mpeg",
            Digest = "aaaa", Size = 1, Modified = DateTime.UtcNow
        };
        if (measured != null) tape.Metadata.Add(measured);
        metsManager.AddToMets(fullMets, tape);
        return (metsUri, fullMets);
    }

    private async Task<WorkingFile> Write(Uri metsUri, FullMets fullMets)
    {
        (await metsManager.WriteMets(fullMets)).Success.Should().BeTrue();
        var parsed = (await parser.GetMetsFileWrapper(metsUri)).Value!;
        return parsed.Files.Single(f => f.LocalPath == "objects/tape.mp3");
    }

    [Fact]
    public async Task An_Entered_Duration_Is_A_Supplied_Mods_Extent_And_Reads_Back_As_Entered()
    {
        var (metsUri, fullMets) = await CreateMets("entered-extent");

        metsManager.SetEnteredExtentByPath(fullMets, "objects/tape.mp3", new EnteredExtent { Duration = 2708.5 })
            .Success.Should().BeTrue();
        var tape = await Write(metsUri, fullMets);

        var extent = XDocument.Load(metsUri.LocalPath).Descendants(ModsNs + "extent").Should().ContainSingle().Subject;
        ((string?)extent.Attribute("unit")).Should().Be("seconds");
        ((string?)extent.Attribute("supplied")).Should().Be("yes");
        extent.Value.Should().Be("2708.5");

        tape.EnteredExtent.Should().Be(new EnteredExtent { Duration = 2708.5 });
        tape.Metadata.OfType<ExtentMetadata>().Should().BeEmpty("an entered value is never a measured one");
        tape.GetEffectiveExtent().Should().Be(new EffectiveExtent(2708.5, null, null, true, false, false));
    }

    [Fact]
    public async Task A_Measured_Duration_Wins_Over_An_Entered_One()
    {
        var (metsUri, fullMets) = await CreateMets("entered-extent-measured",
            new ExtentMetadata { Source = "Exif", Duration = 2641 });

        metsManager.SetEnteredExtentByPath(fullMets, "objects/tape.mp3", new EnteredExtent { Duration = 2700 })
            .Success.Should().BeTrue("a person can say it; the measurement still wins");
        var tape = await Write(metsUri, fullMets);

        tape.EnteredExtent!.Duration.Should().Be(2700);
        tape.GetEffectiveExtent().Should().Be(new EffectiveExtent(2641, null, null, false, false, false));
    }

    [Fact]
    public async Task Entering_Nothing_Clears_What_Was_Entered()
    {
        var (metsUri, fullMets) = await CreateMets("entered-extent-cleared");
        metsManager.SetEnteredExtentByPath(fullMets, "objects/tape.mp3", new EnteredExtent { Duration = 60, PixelWidth = 640, PixelHeight = 480 });
        await Write(metsUri, fullMets);
        fullMets = (await metsManager.GetFullMets(metsUri, (await parser.GetMetsFileWrapper(metsUri)).Value!.ETag!)).Value!;

        metsManager.SetEnteredExtentByPath(fullMets, "objects/tape.mp3", null).Success.Should().BeTrue();
        var tape = await Write(metsUri, fullMets);

        tape.EnteredExtent.Should().BeNull();
        XDocument.Load(metsUri.LocalPath).Descendants(ModsNs + "physicalDescription").Should().BeEmpty(
            "an emptied physicalDescription is removed");
    }

    [Fact]
    public async Task Width_And_Height_Have_Their_Own_Units()
    {
        var (metsUri, fullMets) = await CreateMets("entered-extent-size");

        metsManager.SetEnteredExtentByPath(fullMets, "objects/tape.mp3", new EnteredExtent { PixelWidth = 1920, PixelHeight = 1080 });
        var tape = await Write(metsUri, fullMets);

        XDocument.Load(metsUri.LocalPath).Descendants(ModsNs + "extent").Select(e => (string?)e.Attribute("unit"))
            .Should().BeEquivalentTo("pixels wide", "pixels high");
        tape.EnteredExtent.Should().Be(new EnteredExtent { PixelWidth = 1920, PixelHeight = 1080 });
    }

    [Fact]
    public async Task A_Folder_Has_No_Extent_To_Enter()
    {
        var (_, fullMets) = await CreateMets("entered-extent-folder");

        var result = metsManager.SetEnteredExtentByPath(fullMets, "objects", new EnteredExtent { Duration = 60 });

        result.Failure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BadRequest);
    }
}
