using System.Xml.Linq;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;
using FluentAssertions;
using FluentAssertions.Equivalency;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XmlGen.Tests;

/// <summary>
/// Focused tests for the logical structMap operations on MetsManager:
///
/// - <see cref="IMetsManager.SetStructMap"/> — creates or replaces a LOGICAL structMap
///   from a <see cref="LogicalRange"/> tree, generating dmdSec entries and fptr elements
///   (whole-file, time-based, region-based).
///
/// - <see cref="IMetsManager.RemoveStructMap"/> — removes a LOGICAL structMap by its
///   root div ID, cleaning up the associated dmdSec entries.
///
/// - <see cref="IMetsManager.SetStructMapOrder"/> — reorders multiple LOGICAL structMaps
///   in the StructMap collection.
///
/// - <see cref="IMetsManager.LinkFile"/> and <see cref="IMetsManager.UnLinkFile"/> —
///   add and remove mets:smLink elements in mets:structLink.
/// </summary>
public class MetsManagerLogicalStructTests
{
    private readonly MetsManager metsManager;
    private readonly MetsParser parser;

    private static readonly XNamespace MetsNs = "http://www.loc.gov/METS/";
    private static readonly XNamespace XLinkNs = "http://www.w3.org/1999/xlink";

    private static readonly Uri Supplementing = FileLinkRoles.FromIiifProvides("transcript");

    public MetsManagerLogicalStructTests()
    {
        var serviceProvider = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        var factory = serviceProvider.GetService<ILoggerFactory>();
        var parserLogger = factory!.CreateLogger<MetsParser>();
        var metsLoader = new FileSystemMetsLoader();
        parser = new MetsParser(metsLoader, parserLogger);
        var metsStorage = new FileSystemMetsStorage(parser);
        var premisManager = new PremisManager();
        var premisManagerExif = new PremisManagerExif();
        var premisEventManager = new PremisEventManagerVirus();
        var metadataManager = new MetadataManager(premisManager, premisManagerExif, premisEventManager);
        metsManager = new MetsManager(parser, metsStorage, metadataManager);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<(Uri metsUri, FullMets fullMets)> CreateMetsWithFile(string outputFile, string filePath, string fileName)
    {
        var metsUri = new Uri(new FileInfo(outputFile).FullName);
        var createResult = await metsManager.CreateStandardMets(metsUri, "Test METS");
        var fullMets = (await metsManager.GetFullMets(metsUri, createResult.Value!.ETag!)).Value!;
        metsManager.AddToMets(fullMets, SimpleFile(filePath, fileName));
        await metsManager.WriteMets(fullMets);
        return (metsUri, fullMets);
    }

    private static WorkingFile SimpleFile(string localPath, string name) =>
        new()
        {
            LocalPath = localPath,
            Name = name,
            ContentType = "image/tiff",
            Digest = "abcd1234",
            Size = 1000,
            Modified = DateTime.UtcNow
        };

    private static LogicalRange SimpleLogicalRange(string rootId, string rootName, string filePath) =>
        new()
        {
            Id = rootId,
            Type = "Collection",
            Name = rootName,
            Ranges =
            [
                new LogicalRange
                {
                    Id = rootId + "_ITEM",
                    Type = "Item",
                    Name = "Item One",
                    RecordInfo = new RecordInfo
                    {
                        RecordIdentifiers = [new RecordIdentifier { Source = "test", Value = "item-001" }]
                    },
                    Files = [new FilePointer { LocalPath = filePath }]
                }
            ]
        };

    // -----------------------------------------------------------------------
    // SetStructMap — raw XML structure
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SetStructMap_Creates_Logical_StructMap_In_Raw_Xml()
    {
        // SetStructMap must produce a mets:structMap TYPE="LOGICAL" element containing
        // the correct root div and child divs matching the LogicalRange tree.

        var metsUri = new Uri(new FileInfo("Outputs/logical-rawxml.xml").FullName);
        var createResult = await metsManager.CreateStandardMets(metsUri, "Logical Raw XML Test");
        var fullMets = (await metsManager.GetFullMets(metsUri, createResult.Value!.ETag!)).Value!;
        metsManager.AddToMets(fullMets, SimpleFile("objects/page.tif", "page.tif"));

        var logSm = new LogicalRange
        {
            Id = "LOG_0000",
            Type = "Collection",
            Name = "My Collection",
            Ranges =
            [
                new LogicalRange
                {
                    Id = "LOG_0001",
                    Type = "Item",
                    Name = "Item One",
                    Files = [new FilePointer { LocalPath = "objects/page.tif" }]
                }
            ]
        };

        metsManager.SetStructMap(fullMets, logSm);
        await metsManager.WriteMets(fullMets);

        var doc = XDocument.Load(metsUri.LocalPath);
        var structMaps = doc.Descendants(MetsNs + "structMap").ToList();

        // One PHYSICAL and one LOGICAL.
        structMaps.Should().HaveCount(2);
        var logicalSm = structMaps.Single(sm => (string?)sm.Attribute("TYPE") == "LOGICAL");

        // Root div.
        var rootDiv = logicalSm.Elements(MetsNs + "div").Single();
        rootDiv.Attribute("ID")!.Value.Should().Be("LOG_0000");
        rootDiv.Attribute("TYPE")!.Value.Should().Be("Collection");
        rootDiv.Attribute("LABEL")!.Value.Should().Be("My Collection");

        // Child div with fptr pointing to the file.
        var childDiv = rootDiv.Elements(MetsNs + "div").Single();
        childDiv.Attribute("ID")!.Value.Should().Be("LOG_0001");
        childDiv.Attribute("TYPE")!.Value.Should().Be("Item");
        var fptr = childDiv.Elements(MetsNs + "fptr").Single();
        fptr.Attribute("FILEID")!.Value.Should().Be("FILE_objects/page.tif");
    }

    [Fact]
    public async Task SetStructMap_Creates_DmdSec_For_Each_Range_With_Name()
    {
        // A dmdSec with ID "DMD_" + range.Id must exist for each LogicalRange
        // that has a Name or RecordInfo, and the div must reference it via DMDID.

        var metsUri = new Uri(new FileInfo("Outputs/logical-dmdsec.xml").FullName);
        var createResult = await metsManager.CreateStandardMets(metsUri, "DmdSec Test");
        var fullMets = (await metsManager.GetFullMets(metsUri, createResult.Value!.ETag!)).Value!;
        metsManager.AddToMets(fullMets, SimpleFile("objects/a.tif", "a.tif"));

        var logSm = new LogicalRange
        {
            Id = "LOG_0000",
            Type = "Collection",
            Name = "Root",
            Ranges =
            [
                new LogicalRange
                {
                    Id = "LOG_0001",
                    Type = "Item",
                    Name = "Child Item",
                    Files = [new FilePointer { LocalPath = "objects/a.tif" }]
                }
            ]
        };

        metsManager.SetStructMap(fullMets, logSm);
        await metsManager.WriteMets(fullMets);

        var doc = XDocument.Load(metsUri.LocalPath);

        // dmdSec for root and child must exist.
        doc.Descendants(MetsNs + "dmdSec")
            .Should().Contain(d => (string?)d.Attribute("ID") == "DMD_LOG_0000");
        doc.Descendants(MetsNs + "dmdSec")
            .Should().Contain(d => (string?)d.Attribute("ID") == "DMD_LOG_0001");

        // Root div references its dmdSec.
        var rootDiv = doc.Descendants(MetsNs + "div")
            .Single(d => (string?)d.Attribute("ID") == "LOG_0000");
        rootDiv.Attribute("DMDID")!.Value.Should().Be("DMD_LOG_0000");

        // Child div references its dmdSec.
        var childDiv = doc.Descendants(MetsNs + "div")
            .Single(d => (string?)d.Attribute("ID") == "LOG_0001");
        childDiv.Attribute("DMDID")!.Value.Should().Be("DMD_LOG_0001");
    }

    [Fact]
    public async Task SetStructMap_Time_Based_FilePointer_Produces_Area_Element_With_Betype_Time()
    {
        // A FilePointer with BeginTime / EndTime must produce a mets:fptr containing a
        // mets:area element with BETYPE="TIME" and BEGIN / END as HH:MM:SS time codes.

        var metsUri = new Uri(new FileInfo("Outputs/logical-time-area.xml").FullName);
        var createResult = await metsManager.CreateStandardMets(metsUri, "Time Area Test");
        var fullMets = (await metsManager.GetFullMets(metsUri, createResult.Value!.ETag!)).Value!;
        metsManager.AddToMets(fullMets, SimpleFile("objects/audio.wav", "audio.wav"));

        var logSm = new LogicalRange
        {
            Id = "LOG_0000",
            Type = "Collection",
            Name = "Tapes",
            Ranges =
            [
                new LogicalRange
                {
                    Id = "LOG_0001",
                    Type = "Item",
                    Name = "Interview",
                    Files =
                    [
                        new FilePointer
                        {
                            LocalPath = "objects/audio.wav",
                            BeginTime = 15.0,    // 00:00:15
                            EndTime   = 2109.5   // 00:35:09.5
                        }
                    ]
                }
            ]
        };

        metsManager.SetStructMap(fullMets, logSm);
        await metsManager.WriteMets(fullMets);

        var doc = XDocument.Load(metsUri.LocalPath);
        var childDiv = doc.Descendants(MetsNs + "div")
            .Single(d => (string?)d.Attribute("ID") == "LOG_0001");
        var fptr = childDiv.Elements(MetsNs + "fptr").Single();

        // Must use area, not direct FILEID.
        fptr.Attribute("FILEID").Should().BeNull();
        var area = fptr.Elements(MetsNs + "area").Single();
        area.Attribute("FILEID")!.Value.Should().Be("FILE_objects/audio.wav");
        area.Attribute("BETYPE")!.Value.Should().Be("TIME");
        area.Attribute("BEGIN")!.Value.Should().Be("00:00:15");
        area.Attribute("END")!.Value.Should().Be("00:35:09.5");
    }

    [Fact]
    public async Task SetStructMap_Region_Based_FilePointer_Produces_Area_Element_With_Shape_Rect()
    {
        // A FilePointer with a Region must produce a mets:area element with SHAPE="RECT"
        // and COORDS="x1,y1,x2,y2".

        var metsUri = new Uri(new FileInfo("Outputs/logical-rect-area.xml").FullName);
        var createResult = await metsManager.CreateStandardMets(metsUri, "Rect Area Test");
        var fullMets = (await metsManager.GetFullMets(metsUri, createResult.Value!.ETag!)).Value!;
        metsManager.AddToMets(fullMets, SimpleFile("objects/page.tif", "page.tif"));

        var logSm = new LogicalRange
        {
            Id = "LOG_0000",
            Type = "Collection",
            Name = "Book",
            Ranges =
            [
                new LogicalRange
                {
                    Id = "LOG_0001",
                    Type = "Part",
                    Name = "Part 1",
                    Files =
                    [
                        new FilePointer
                        {
                            LocalPath = "objects/page.tif",
                            Region = new Rectangle { X1 = 0, Y1 = 0, X2 = 6000, Y2 = 2000 }
                        }
                    ]
                }
            ]
        };

        metsManager.SetStructMap(fullMets, logSm);
        await metsManager.WriteMets(fullMets);

        var doc = XDocument.Load(metsUri.LocalPath);
        var childDiv = doc.Descendants(MetsNs + "div")
            .Single(d => (string?)d.Attribute("ID") == "LOG_0001");
        var fptr = childDiv.Elements(MetsNs + "fptr").Single();

        fptr.Attribute("FILEID").Should().BeNull();
        var area = fptr.Elements(MetsNs + "area").Single();
        area.Attribute("FILEID")!.Value.Should().Be("FILE_objects/page.tif");
        area.Attribute("SHAPE")!.Value.Should().Be("RECT");
        area.Attribute("COORDS")!.Value.Should().Be("0,0,6000,2000");
    }

    // -----------------------------------------------------------------------
    // SetStructMap — parser round-trip
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SetStructMap_Whole_File_Round_Trips_Via_Parser()
    {
        // After SetStructMap + WriteMets the parser must reconstruct the same
        // LogicalRange tree (excluding Effective* fields which are parser-only).

        var metsUri = new Uri(new FileInfo("Outputs/logical-roundtrip.xml").FullName);
        var createResult = await metsManager.CreateStandardMets(metsUri, "Round-trip Test");
        var fullMets = (await metsManager.GetFullMets(metsUri, createResult.Value!.ETag!)).Value!;
        metsManager.AddToMets(fullMets, SimpleFile("objects/img.tif", "img.tif"));

        var logSm = new LogicalRange
        {
            Id = "LOG_0000",
            Type = "Collection",
            Name = "My Collection",
            Ranges =
            [
                new LogicalRange
                {
                    Id = "LOG_0001",
                    Type = "Item",
                    Name = "Item One",
                    RecordInfo = new RecordInfo
                    {
                        RecordIdentifiers =
                        [
                            new RecordIdentifier { Source = "cat", Value = "CAT/1" }
                        ]
                    },
                    Files = [new FilePointer { LocalPath = "objects/img.tif" }]
                }
            ]
        };

        metsManager.SetStructMap(fullMets, logSm);
        await metsManager.WriteMets(fullMets);

        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();
        var parsed = parseResult.Value!.LogicalStructures.Single();

        parsed.Should().BeEquivalentTo(logSm,
            opts => opts.Excluding((IMemberInfo m) => m.Name.StartsWith("Effective")));
    }

    [Fact]
    public async Task SetStructMap_Replacing_Existing_Removes_Old_DmdSecs_And_StructMap()
    {
        // Calling SetStructMap a second time with the same root ID must remove the old
        // LOGICAL structMap and all its dmdSecs before writing the new one.

        var metsUri = new Uri(new FileInfo("Outputs/logical-replace.xml").FullName);
        var createResult = await metsManager.CreateStandardMets(metsUri, "Replace Test");
        var fullMets = (await metsManager.GetFullMets(metsUri, createResult.Value!.ETag!)).Value!;
        metsManager.AddToMets(fullMets, SimpleFile("objects/a.tif", "a.tif"));
        metsManager.AddToMets(fullMets, SimpleFile("objects/b.tif", "b.tif"));

        // First SetStructMap.
        var originalLogSm = new LogicalRange
        {
            Id = "LOG_0000",
            Type = "Collection",
            Name = "Original",
            Ranges = [new LogicalRange { Id = "LOG_0001", Type = "Item", Name = "Original Item", Files = [new FilePointer { LocalPath = "objects/a.tif" }] }]
        };
        metsManager.SetStructMap(fullMets, originalLogSm);

        // Replace with a different tree under the same root ID.
        var replacementLogSm = new LogicalRange
        {
            Id = "LOG_0000",
            Type = "Collection",
            Name = "Replacement",
            Ranges = [new LogicalRange { Id = "LOG_0099", Type = "Item", Name = "Replacement Item", Files = [new FilePointer { LocalPath = "objects/b.tif" }] }]
        };
        metsManager.SetStructMap(fullMets, replacementLogSm);
        await metsManager.WriteMets(fullMets);

        var doc = XDocument.Load(metsUri.LocalPath);

        // Exactly one LOGICAL structMap.
        doc.Descendants(MetsNs + "structMap")
            .Count(sm => (string?)sm.Attribute("TYPE") == "LOGICAL")
            .Should().Be(1);

        // Old child div and its dmdSec are gone.
        doc.Descendants(MetsNs + "div")
            .Should().NotContain(d => (string?)d.Attribute("ID") == "LOG_0001");
        doc.Descendants(MetsNs + "dmdSec")
            .Should().NotContain(d => (string?)d.Attribute("ID") == "DMD_LOG_0001");

        // New content is present.
        doc.Descendants(MetsNs + "div")
            .Should().Contain(d => (string?)d.Attribute("ID") == "LOG_0099");
        doc.Descendants(MetsNs + "dmdSec")
            .Should().Contain(d => (string?)d.Attribute("ID") == "DMD_LOG_0099");
    }

    // -----------------------------------------------------------------------
    // RemoveStructMap
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RemoveStructMap_Removes_Logical_StructMap_And_Its_DmdSecs()
    {
        // RemoveStructMap must remove the LOGICAL structMap element and all dmdSec
        // entries that were created for its divs.

        var metsUri = new Uri(new FileInfo("Outputs/logical-remove.xml").FullName);
        var createResult = await metsManager.CreateStandardMets(metsUri, "Remove Test");
        var fullMets = (await metsManager.GetFullMets(metsUri, createResult.Value!.ETag!)).Value!;
        metsManager.AddToMets(fullMets, SimpleFile("objects/x.tif", "x.tif"));

        var logSm = SimpleLogicalRange("LOG_0000", "Root", "objects/x.tif");
        metsManager.SetStructMap(fullMets, logSm);

        metsManager.RemoveStructMap(fullMets, "LOG_0000");
        await metsManager.WriteMets(fullMets);

        var doc = XDocument.Load(metsUri.LocalPath);

        // No LOGICAL structMap.
        doc.Descendants(MetsNs + "structMap")
            .Should().NotContain(sm => (string?)sm.Attribute("TYPE") == "LOGICAL");

        // All logical dmdSecs gone.
        doc.Descendants(MetsNs + "dmdSec")
            .Should().NotContain(d => (string?)d.Attribute("ID") == "DMD_LOG_0000");
        doc.Descendants(MetsNs + "dmdSec")
            .Should().NotContain(d => (string?)d.Attribute("ID") == "DMD_LOG_0000_ITEM");

        // Physical structMap is untouched.
        doc.Descendants(MetsNs + "structMap")
            .Should().Contain(sm => (string?)sm.Attribute("TYPE") == "PHYSICAL");
    }

    [Fact]
    public async Task RemoveStructMap_On_NonExistent_Id_Is_No_Op()
    {
        // RemoveStructMap must not throw when the given ID does not exist; the METS
        // must be left unmodified.

        var metsUri = new Uri(new FileInfo("Outputs/logical-remove-noop.xml").FullName);
        var createResult = await metsManager.CreateStandardMets(metsUri, "Remove No-Op Test");
        var fullMets = (await metsManager.GetFullMets(metsUri, createResult.Value!.ETag!)).Value!;

        var act = () => metsManager.RemoveStructMap(fullMets, "LOG_DOES_NOT_EXIST");
        act.Should().NotThrow();

        await metsManager.WriteMets(fullMets);
        var doc = XDocument.Load(metsUri.LocalPath);
        doc.Descendants(MetsNs + "structMap")
            .Should().HaveCount(1, "only the PHYSICAL structMap should be present");
    }

    // -----------------------------------------------------------------------
    // SetStructMapOrder
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SetStructMapOrder_Reorders_Multiple_Logical_StructMaps()
    {
        // When two LOGICAL structMaps exist, SetStructMapOrder reorders them in the
        // StructMap collection without losing either one.

        var metsUri = new Uri(new FileInfo("Outputs/logical-order.xml").FullName);
        var createResult = await metsManager.CreateStandardMets(metsUri, "Order Test");
        var fullMets = (await metsManager.GetFullMets(metsUri, createResult.Value!.ETag!)).Value!;
        metsManager.AddToMets(fullMets, SimpleFile("objects/a.tif", "a.tif"));
        metsManager.AddToMets(fullMets, SimpleFile("objects/b.tif", "b.tif"));

        var smA = new LogicalRange { Id = "LOG_A", Type = "Collection", Name = "A", Ranges = [new LogicalRange { Id = "LOG_A1", Type = "Item", Name = "A1", Files = [new FilePointer { LocalPath = "objects/a.tif" }] }] };
        var smB = new LogicalRange { Id = "LOG_B", Type = "Collection", Name = "B", Ranges = [new LogicalRange { Id = "LOG_B1", Type = "Item", Name = "B1", Files = [new FilePointer { LocalPath = "objects/b.tif" }] }] };

        metsManager.SetStructMap(fullMets, smA);
        metsManager.SetStructMap(fullMets, smB);

        // Reorder: B before A.
        metsManager.SetStructMapOrder(fullMets, ["LOG_B", "LOG_A"]);
        await metsManager.WriteMets(fullMets);

        var doc = XDocument.Load(metsUri.LocalPath);
        var logicalStructMaps = doc.Descendants(MetsNs + "structMap")
            .Where(sm => (string?)sm.Attribute("TYPE") == "LOGICAL")
            .ToList();

        logicalStructMaps.Should().HaveCount(2);
        logicalStructMaps[0].Elements(MetsNs + "div").Single().Attribute("ID")!.Value.Should().Be("LOG_B");
        logicalStructMaps[1].Elements(MetsNs + "div").Single().Attribute("ID")!.Value.Should().Be("LOG_A");
    }

    // -----------------------------------------------------------------------
    // LinkFile
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LinkFile_Creates_SmLink_With_Correct_Attributes_In_Raw_Xml()
    {
        // LinkFile must create a mets:structLink containing a mets:smLink with the
        // correct xlink:from, xlink:to and xlink:arcrole attributes.

        var metsUri = new Uri(new FileInfo("Outputs/logical-link.xml").FullName);
        var createResult = await metsManager.CreateStandardMets(metsUri, "Link Test");
        var fullMets = (await metsManager.GetFullMets(metsUri, createResult.Value!.ETag!)).Value!;
        metsManager.AddToMets(fullMets, SimpleFile("objects/audio.m4a", "audio.m4a"));
        metsManager.AddToMets(fullMets, SimpleFile("objects/transcript.docx", "transcript.docx"));

        metsManager.LinkFile(fullMets, "objects/audio.m4a", "objects/transcript.docx", Supplementing);
        await metsManager.WriteMets(fullMets);

        var doc = XDocument.Load(metsUri.LocalPath);
        var structLink = doc.Descendants(MetsNs + "structLink").Single();
        var smLink = structLink.Elements(MetsNs + "smLink").Single();

        smLink.Attribute(XLinkNs + "from")!.Value.Should().Be("FILE_objects/audio.m4a");
        smLink.Attribute(XLinkNs + "to")!.Value.Should().Be("FILE_objects/transcript.docx");
        smLink.Attribute(XLinkNs + "arcrole")!.Value.Should().Be(Supplementing.ToString());
    }

    [Fact]
    public async Task LinkFile_Multiple_Links_Are_All_Present()
    {
        // Calling LinkFile multiple times must accumulate all smLink elements.

        var metsUri = new Uri(new FileInfo("Outputs/logical-link-multi.xml").FullName);
        var createResult = await metsManager.CreateStandardMets(metsUri, "Multi Link Test");
        var fullMets = (await metsManager.GetFullMets(metsUri, createResult.Value!.ETag!)).Value!;
        metsManager.AddToMets(fullMets, SimpleFile("objects/a.m4a", "a.m4a"));
        metsManager.AddToMets(fullMets, SimpleFile("objects/a.docx", "a.docx"));
        metsManager.AddToMets(fullMets, SimpleFile("objects/b.m4a", "b.m4a"));
        metsManager.AddToMets(fullMets, SimpleFile("objects/b.docx", "b.docx"));

        metsManager.LinkFile(fullMets, "objects/a.m4a", "objects/a.docx", Supplementing);
        metsManager.LinkFile(fullMets, "objects/b.m4a", "objects/b.docx", Supplementing);
        await metsManager.WriteMets(fullMets);

        var doc = XDocument.Load(metsUri.LocalPath);
        var smLinks = doc.Descendants(MetsNs + "smLink").ToList();

        smLinks.Should().HaveCount(2);
        smLinks.Should().Contain(sl => sl.Attribute(XLinkNs + "from")!.Value == "FILE_objects/a.m4a");
        smLinks.Should().Contain(sl => sl.Attribute(XLinkNs + "from")!.Value == "FILE_objects/b.m4a");
    }

    // -----------------------------------------------------------------------
    // UnLinkFile
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UnLinkFile_Removes_Matching_SmLink()
    {
        // After UnLinkFile the matching smLink element must be absent from the XML
        // while any other smLinks are preserved.

        var metsUri = new Uri(new FileInfo("Outputs/logical-unlink.xml").FullName);
        var createResult = await metsManager.CreateStandardMets(metsUri, "Unlink Test");
        var fullMets = (await metsManager.GetFullMets(metsUri, createResult.Value!.ETag!)).Value!;
        metsManager.AddToMets(fullMets, SimpleFile("objects/a.m4a", "a.m4a"));
        metsManager.AddToMets(fullMets, SimpleFile("objects/a.docx", "a.docx"));
        metsManager.AddToMets(fullMets, SimpleFile("objects/b.m4a", "b.m4a"));
        metsManager.AddToMets(fullMets, SimpleFile("objects/b.docx", "b.docx"));

        metsManager.LinkFile(fullMets, "objects/a.m4a", "objects/a.docx", Supplementing);
        metsManager.LinkFile(fullMets, "objects/b.m4a", "objects/b.docx", Supplementing);

        // Remove only the first link.
        metsManager.UnLinkFile(fullMets, "objects/a.m4a", "objects/a.docx", Supplementing);
        await metsManager.WriteMets(fullMets);

        var doc = XDocument.Load(metsUri.LocalPath);
        var smLinks = doc.Descendants(MetsNs + "smLink").ToList();

        smLinks.Should().HaveCount(1);
        smLinks[0].Attribute(XLinkNs + "from")!.Value.Should().Be("FILE_objects/b.m4a");
    }

    [Fact]
    public async Task UnLinkFile_On_NonExistent_Link_Is_No_Op()
    {
        // UnLinkFile must not throw when the specified link does not exist.

        var metsUri = new Uri(new FileInfo("Outputs/logical-unlink-noop.xml").FullName);
        var createResult = await metsManager.CreateStandardMets(metsUri, "Unlink No-Op Test");
        var fullMets = (await metsManager.GetFullMets(metsUri, createResult.Value!.ETag!)).Value!;
        metsManager.AddToMets(fullMets, SimpleFile("objects/a.m4a", "a.m4a"));
        metsManager.AddToMets(fullMets, SimpleFile("objects/b.docx", "b.docx"));

        var act = () => metsManager.UnLinkFile(fullMets, "objects/a.m4a", "objects/b.docx", Supplementing);
        act.Should().NotThrow();
    }
}
