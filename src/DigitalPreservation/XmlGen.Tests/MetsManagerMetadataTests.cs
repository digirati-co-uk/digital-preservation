using System.Xml.Linq;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XmlGen.Tests;

/// <summary>
/// Focused tests for the metadata operations on MetsManager that were not covered
/// by the existing test suite:
///
/// - <see cref="IMetsManager.SetRecordInfoByPath"/> and
///   <see cref="IMetsManager.SetRecordInfoByDivId"/> — write mods:recordInfo into
///   the dmdSec of a physical or logical div.
///
/// - <see cref="IMetsManager.SetAccessRestrictionsByDivId"/> and
///   <see cref="IMetsManager.SetRightsStatementByDivId"/> — write MODS
///   mods:accessCondition into the dmdSec addressed by div ID rather than
///   physical path. Includes both logical and physical div IDs.
/// </summary>
public class MetsManagerMetadataTests
{
    private readonly MetsManager metsManager;
    private readonly MetsParser parser;

    private static readonly XNamespace MetsNs = "http://www.loc.gov/METS/";
    private static readonly XNamespace ModsNs = "http://www.loc.gov/mods/v3";

    public MetsManagerMetadataTests()
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

    private async Task<(Uri metsUri, FullMets fullMets)> CreateMetsWithFile(
        string outputFile, string filePath = "objects/file.tif")
    {
        var metsUri = new Uri(new FileInfo(outputFile).FullName);
        var createResult = await metsManager.CreateStandardMets(metsUri, "Metadata Test");
        var fullMets = (await metsManager.GetFullMets(metsUri, createResult.Value!.ETag!)).Value!;
        metsManager.AddToMets(fullMets, new WorkingFile
        {
            LocalPath = filePath,
            Name = "file.tif",
            ContentType = "image/tiff",
            Digest = "abcd1234",
            Size = 1000,
            Modified = DateTime.UtcNow
        });
        return (metsUri, fullMets);
    }

    private async Task<(Uri metsUri, FullMets fullMets)> CreateMetsWithLogicalRange(
        string outputFile, string rootId = "LOG_0000")
    {
        var (metsUri, fullMets) = await CreateMetsWithFile(outputFile);
        var logSm = new LogicalRange
        {
            Id = rootId,
            Type = "Collection",
            Name = "Root",
            Ranges =
            [
                new LogicalRange
                {
                    Id = rootId + "_ITEM",
                    Type = "Item",
                    Name = "Item",
                    Files = [new FilePointer { LocalPath = "objects/file.tif" }]
                }
            ]
        };
        metsManager.SetStructMap(fullMets, logSm);
        return (metsUri, fullMets);
    }

    // -----------------------------------------------------------------------
    // SetRecordInfoByPath
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SetRecordInfoByPath_Round_Trips_Via_Parser()
    {
        // After SetRecordInfoByPath + WriteMets the parser must return a WorkingDirectory
        // whose RecordInfo carries the expected RecordIdentifiers.

        var (metsUri, fullMets) = await CreateMetsWithFile("Outputs/meta-recordinfo-bypath-roundtrip.xml");

        metsManager.SetRecordInfoByPath(fullMets, "objects", new RecordInfo
        {
            RecordIdentifiers =
            [
                new RecordIdentifier { Source = "identity-service", Value = "abc123" },
                new RecordIdentifier { Source = "EMu",              Value = "COLL/1" }
            ]
        });
        await metsManager.WriteMets(fullMets);

        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();

        var objectsDir = parseResult.Value!.PhysicalStructure!.Directories
            .Single(d => d.Name == FolderNames.Objects);
        objectsDir.RecordInfo.Should().NotBeNull();
        objectsDir.RecordInfo!.RecordIdentifiers.Should().HaveCount(2);
        objectsDir.RecordInfo.RecordIdentifiers[0].Source.Should().Be("identity-service");
        objectsDir.RecordInfo.RecordIdentifiers[0].Value.Should().Be("abc123");
        objectsDir.RecordInfo.RecordIdentifiers[1].Source.Should().Be("EMu");
        objectsDir.RecordInfo.RecordIdentifiers[1].Value.Should().Be("COLL/1");
    }

    [Fact]
    public async Task SetRecordInfoByPath_Raw_Xml_Has_RecordIdentifier_Elements()
    {
        // The dmdSec for the targeted div must contain mods:recordInfo with
        // a mods:recordIdentifier per identifier supplied.

        var (metsUri, fullMets) = await CreateMetsWithFile("Outputs/meta-recordinfo-bypath-rawxml.xml");

        metsManager.SetRecordInfoByPath(fullMets, "objects", new RecordInfo
        {
            RecordIdentifiers = [new RecordIdentifier { Source = "EMu", Value = "REF/42" }]
        });
        await metsManager.WriteMets(fullMets);

        var doc = XDocument.Load(metsUri.LocalPath);
        var objectsDmd = doc.Descendants(MetsNs + "dmdSec")
            .Single(d => (string?)d.Attribute("ID") == "DMD_objects");

        var recordIdentifiers = objectsDmd.Descendants(ModsNs + "recordIdentifier").ToList();
        recordIdentifiers.Should().HaveCount(1);
        recordIdentifiers[0].Attribute("source")!.Value.Should().Be("EMu");
        recordIdentifiers[0].Value.Should().Be("REF/42");
    }

    [Fact]
    public async Task SetRecordInfoByPath_On_File_Path_Round_Trips()
    {
        // SetRecordInfoByPath must work on individual file paths, not just directories.

        var (metsUri, fullMets) = await CreateMetsWithFile("Outputs/meta-recordinfo-bypath-file.xml");

        metsManager.SetRecordInfoByPath(fullMets, "objects/file.tif", new RecordInfo
        {
            RecordIdentifiers = [new RecordIdentifier { Source = "cat", Value = "FILE/1" }]
        });
        await metsManager.WriteMets(fullMets);

        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();
        var objectsDir = parseResult.Value!.PhysicalStructure!.Directories
            .Single(d => d.Name == FolderNames.Objects);
        var file = objectsDir.Files.Single();
        file.RecordInfo.Should().NotBeNull();
        file.RecordInfo!.RecordIdentifiers[0].Value.Should().Be("FILE/1");
    }

    // -----------------------------------------------------------------------
    // SetRecordInfoByDivId
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SetRecordInfoByDivId_On_Physical_Div_Id_Round_Trips_Via_Parser()
    {
        // SetRecordInfoByDivId with a PHYS_* ID is equivalent to SetRecordInfoByPath.

        var (metsUri, fullMets) = await CreateMetsWithFile("Outputs/meta-recordinfo-bydivid-phys.xml");

        metsManager.SetRecordInfoByDivId(fullMets, "PHYS_objects", new RecordInfo
        {
            RecordIdentifiers = [new RecordIdentifier { Source = "EMu", Value = "PHYS/99" }]
        });
        await metsManager.WriteMets(fullMets);

        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        var objectsDir = parseResult.Value!.PhysicalStructure!.Directories
            .Single(d => d.Name == FolderNames.Objects);
        objectsDir.RecordInfo!.RecordIdentifiers[0].Value.Should().Be("PHYS/99");
    }

    [Fact]
    public async Task SetRecordInfoByDivId_On_Logical_Div_Id_Round_Trips_Via_Parser()
    {
        // SetRecordInfoByDivId must work on logical div IDs (e.g. "LOG_0000_ITEM"),
        // writing mods:recordInfo into the logical dmdSec.

        var (metsUri, fullMets) = await CreateMetsWithLogicalRange("Outputs/meta-recordinfo-bydivid-logical.xml");

        metsManager.SetRecordInfoByDivId(fullMets, "LOG_0000_ITEM", new RecordInfo
        {
            RecordIdentifiers = [new RecordIdentifier { Source = "EMu", Value = "LOG/1" }]
        });
        await metsManager.WriteMets(fullMets);

        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();
        var logicalRoot = parseResult.Value!.LogicalStructures.Single();
        var item = logicalRoot.Ranges.Single();
        item.RecordInfo.Should().NotBeNull();
        item.RecordInfo!.RecordIdentifiers[0].Value.Should().Be("LOG/1");
    }

    // -----------------------------------------------------------------------
    // SetAccessRestrictionsByDivId
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SetAccessRestrictionsByDivId_On_Physical_Div_Id_Round_Trips()
    {
        // SetAccessRestrictionsByDivId with a PHYS_* ID must write the access
        // restriction into the physical dmdSec and be readable by the parser.

        var (metsUri, fullMets) = await CreateMetsWithFile("Outputs/meta-access-bydivid-phys.xml");

        metsManager.SetAccessRestrictionsByDivId(fullMets, "PHYS_objects", ["Level1"]);
        await metsManager.WriteMets(fullMets);

        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        var objectsDir = parseResult.Value!.PhysicalStructure!.Directories
            .Single(d => d.Name == FolderNames.Objects);
        objectsDir.AccessRestrictions.Should().ContainSingle().Which.Should().Be("Level1");
    }

    [Fact]
    public async Task SetAccessRestrictionsByDivId_On_Logical_Div_Round_Trips()
    {
        // SetAccessRestrictionsByDivId on a logical div ID must write the access
        // restriction into the logical dmdSec.

        var (metsUri, fullMets) = await CreateMetsWithLogicalRange("Outputs/meta-access-bydivid-logical.xml");

        metsManager.SetAccessRestrictionsByDivId(fullMets, "LOG_0000_ITEM", ["Closed"]);
        await metsManager.WriteMets(fullMets);

        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        var item = parseResult.Value!.LogicalStructures.Single().Ranges.Single();
        item.AccessRestrictions.Should().ContainSingle().Which.Should().Be("Closed");
    }

    [Fact]
    public async Task SetAccessRestrictionsByDivId_Clears_Previous_Restrictions()
    {
        // Calling SetAccessRestrictionsByDivId a second time must replace (not
        // accumulate) the previous access restrictions.

        var (metsUri, fullMets) = await CreateMetsWithFile("Outputs/meta-access-bydivid-clear.xml");

        metsManager.SetAccessRestrictionsByDivId(fullMets, "PHYS_objects", ["Level1", "Level2"]);
        metsManager.SetAccessRestrictionsByDivId(fullMets, "PHYS_objects", ["Closed"]);
        await metsManager.WriteMets(fullMets);

        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        var objectsDir = parseResult.Value!.PhysicalStructure!.Directories
            .Single(d => d.Name == FolderNames.Objects);
        objectsDir.AccessRestrictions.Should().ContainSingle().Which.Should().Be("Closed");
    }

    [Fact]
    public async Task SetAccessRestrictionsByDivId_Empty_List_Clears_All_Restrictions()
    {
        // Passing an empty list must remove all access restrictions from the dmdSec.

        var (metsUri, fullMets) = await CreateMetsWithFile("Outputs/meta-access-bydivid-empty.xml");

        metsManager.SetAccessRestrictionsByDivId(fullMets, "PHYS_objects", ["Level1"]);
        metsManager.SetAccessRestrictionsByDivId(fullMets, "PHYS_objects", []);
        await metsManager.WriteMets(fullMets);

        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        var objectsDir = parseResult.Value!.PhysicalStructure!.Directories
            .Single(d => d.Name == FolderNames.Objects);
        // Either null or an empty list — no access restriction must survive.
        (objectsDir.AccessRestrictions == null || objectsDir.AccessRestrictions.Count == 0)
            .Should().BeTrue("clearing restrictions should leave none");
    }

    // -----------------------------------------------------------------------
    // SetRightsStatementByDivId
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SetRightsStatementByDivId_On_Physical_Div_Id_Round_Trips()
    {
        // SetRightsStatementByDivId with a PHYS_* ID must write the rights statement
        // into the physical dmdSec and be readable by the parser.

        var (metsUri, fullMets) = await CreateMetsWithFile("Outputs/meta-rights-bydivid-phys.xml");
        var rights = new Uri("http://rightsstatements.org/vocab/InC/1.0/");

        metsManager.SetRightsStatementByDivId(fullMets, "PHYS_objects", rights);
        await metsManager.WriteMets(fullMets);

        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        var objectsDir = parseResult.Value!.PhysicalStructure!.Directories
            .Single(d => d.Name == FolderNames.Objects);
        objectsDir.RightsStatement.Should().Be(rights.ToString());
    }

    [Fact]
    public async Task SetRightsStatementByDivId_On_Logical_Div_Round_Trips()
    {
        // SetRightsStatementByDivId on a logical div ID must write the rights statement
        // into the logical dmdSec.

        var (metsUri, fullMets) = await CreateMetsWithLogicalRange("Outputs/meta-rights-bydivid-logical.xml");
        var rights = new Uri("http://rightsstatements.org/vocab/InC/1.0/");

        metsManager.SetRightsStatementByDivId(fullMets, "LOG_0000_ITEM", rights);
        await metsManager.WriteMets(fullMets);

        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        var item = parseResult.Value!.LogicalStructures.Single().Ranges.Single();
        item.RightsStatement.Should().Be(rights.ToString());
    }

    [Fact]
    public async Task SetRightsStatementByDivId_Null_Clears_Existing_Statement()
    {
        // Passing null must remove the rights statement from the dmdSec.

        var (metsUri, fullMets) = await CreateMetsWithFile("Outputs/meta-rights-bydivid-clear.xml");
        var rights = new Uri("http://rightsstatements.org/vocab/InC/1.0/");

        metsManager.SetRightsStatementByDivId(fullMets, "PHYS_objects", rights);
        metsManager.SetRightsStatementByDivId(fullMets, "PHYS_objects", null);
        await metsManager.WriteMets(fullMets);

        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        var objectsDir = parseResult.Value!.PhysicalStructure!.Directories
            .Single(d => d.Name == FolderNames.Objects);
        objectsDir.RightsStatement.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // Pruning redundant dmdSec / mods when all metadata is cleared
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Clearing_All_Metadata_Removes_Redundant_DmdSec_And_Mods()
    {
        // After setting then clearing every piece of MODS metadata, the dmdSec must
        // not survive as an empty <mods/> shell — it should be removed entirely,
        // along with the div's now-dangling DMDID reference.

        var (metsUri, fullMets) = await CreateMetsWithFile("Outputs/meta-prune-file.xml");

        // Add some metadata...
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects/file.tif", ["Closed"]);
        metsManager.SetRightsStatementByPath(fullMets, "objects/file.tif",
            new Uri("http://rightsstatements.org/vocab/InC/1.0/"));
        metsManager.SetRecordInfoByPath(fullMets, "objects/file.tif", new RecordInfo
        {
            RecordIdentifiers = [new RecordIdentifier { Source = "EMu", Value = "REF/1" }]
        });

        // ...then clear it all again.
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects/file.tif", []);
        metsManager.SetRightsStatementByPath(fullMets, "objects/file.tif", null);
        metsManager.SetRecordInfoByPath(fullMets, "objects/file.tif", new RecordInfo());
        await metsManager.WriteMets(fullMets);

        var doc = XDocument.Load(metsUri.LocalPath);

        // No dmdSec for the file, and no empty mods left anywhere.
        doc.Descendants(MetsNs + "dmdSec")
            .Any(d => (string?)d.Attribute("ID") == MetsIds.Dmd("objects/file.tif"))
            .Should().BeFalse("the dmdSec should be removed once it carries no MODS information");

        // The file div must no longer reference the removed dmdSec.
        var fileDiv = doc.Descendants(MetsNs + "div")
            .Single(d => (string?)d.Attribute("ID") == MetsIds.Phys("objects/file.tif"));
        fileDiv.Attribute("DMDID").Should().BeNull("the dangling DMDID reference should be dropped");
    }

    [Fact]
    public async Task Clearing_One_Field_Keeps_DmdSec_When_Other_Metadata_Remains()
    {
        // Clearing only the access restriction must NOT remove the dmdSec while a
        // record identifier is still present — only genuinely empty MODS is pruned.

        var (metsUri, fullMets) = await CreateMetsWithFile("Outputs/meta-prune-keep.xml");

        metsManager.SetAccessRestrictionsByPath(fullMets, "objects/file.tif", ["Closed"]);
        metsManager.SetRecordInfoByPath(fullMets, "objects/file.tif", new RecordInfo
        {
            RecordIdentifiers = [new RecordIdentifier { Source = "EMu", Value = "REF/1" }]
        });

        // Clear only the access restriction; record info must survive.
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects/file.tif", []);
        await metsManager.WriteMets(fullMets);

        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        var objectsDir = parseResult.Value!.PhysicalStructure!.Directories
            .Single(d => d.Name == FolderNames.Objects);
        var file = objectsDir.Files.Single();
        (file.AccessRestrictions == null || file.AccessRestrictions.Count == 0)
            .Should().BeTrue("the access restriction was cleared");
        file.RecordInfo.Should().NotBeNull("record info must survive clearing of an unrelated field");
        file.RecordInfo!.RecordIdentifiers[0].Value.Should().Be("REF/1");
    }
}
