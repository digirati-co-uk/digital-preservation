using System.Xml;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;
using DigitalPreservation.XmlGen.Mets;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XmlGen.Tests;

/// <summary>
/// Behaviour introduced by issue #188 step 2, beyond "IDs are now encoded" (which
/// <see cref="MetsIdIntegrityTests"/> and <see cref="MetsIdEncodingTests"/> cover):
/// the boundary validation of client-supplied logical IDs, the derivation of a dmdSec ID from
/// a path rather than from the div's ID, and the duplicate-div guard's awareness that a legacy
/// document names the same path with a different ID.
/// </summary>
public class EncodedMetsIdTests
{
    private readonly MetsManager metsManager;
    private readonly FileSystemMetsStorage metsStorage;

    public EncodedMetsIdTests()
    {
        var serviceProvider = new ServiceCollection().AddLogging().BuildServiceProvider();
        var factory = serviceProvider.GetService<ILoggerFactory>();
        var parserLogger = factory!.CreateLogger<MetsParser>();
        var metsLoader = new FileSystemMetsLoader();
        var parser = new MetsParser(metsLoader, parserLogger);
        metsStorage = new FileSystemMetsStorage(parser);
        var premisManager = new PremisManager();
        var premisManagerExif = new PremisManagerExif();
        var premisEventManager = new PremisEventManagerVirus();
        var metadataManager = new MetadataManager(premisManager, premisManagerExif, premisEventManager);
        metsManager = new MetsManager(parser, metsStorage, metadataManager);
    }

    private async Task<FullMets> CreateAndLoadStandardMets(string outputFileName)
    {
        var uri = new Uri(new FileInfo($"Outputs/{outputFileName}").FullName);
        var createResult = await metsManager.CreateStandardMets(uri, "Encoded ID Test METS");
        createResult.Success.Should().BeTrue(createResult.ErrorMessage ?? "");
        var loadResult = await metsManager.GetFullMets(uri, createResult.Value!.ETag);
        loadResult.Success.Should().BeTrue(loadResult.ErrorMessage ?? "");
        return loadResult.Value!;
    }

    private static Uri CopyFixture(string fixtureName, string outputName)
    {
        var source = new FileInfo($"Samples/{fixtureName}");
        var dest = new FileInfo($"Outputs/{outputName}");
        File.Copy(source.FullName, dest.FullName, overwrite: true);
        return new Uri(dest.FullName);
    }

    private static void AssertIsValidId(string? id)
    {
        id.Should().NotBeNull();
        var verify = () => XmlConvert.VerifyNCName(id!);
        verify.Should().NotThrow($"'{id}' is written into an xs:ID attribute");
    }

    // -----------------------------------------------------------------------
    // Client-supplied logical structMap IDs are validated, not encoded
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("has space")]
    [InlineData("has/slash")]
    [InlineData("1leading-digit")]
    [InlineData("has:colon")]
    [InlineData("has&ampersand")]
    public async Task SetStructMap_Rejects_A_Range_Id_That_Cannot_Be_An_Xml_Id(string badId)
    {
        var fullMets = await CreateAndLoadStandardMets($"encoded-badid-{badId.GetHashCode()}.xml");

        var result = metsManager.SetStructMap(fullMets, new LogicalRange { Id = badId, Type = "Sequence" });

        result.Failure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BadRequest);
        fullMets.Mets.StructMap.Should().NotContain(sm => sm.Type == Constants.Logical,
            "a rejected structMap must not be written at all");
    }

    [Fact]
    public async Task SetStructMap_Rejects_An_Invalid_Id_On_A_NESTED_Range()
    {
        // The whole tree is the client's, so validation cannot stop at the root.
        var fullMets = await CreateAndLoadStandardMets("encoded-badid-nested.xml");

        var result = metsManager.SetStructMap(fullMets, new LogicalRange
        {
            Id = "LOG_0000",
            Type = "Sequence",
            Ranges = [new LogicalRange { Id = "bad id", Type = "Item" }]
        });

        result.Failure.Should().BeTrue();
        result.ErrorMessage.Should().Contain("bad id");
        fullMets.Mets.StructMap.Should().NotContain(sm => sm.Type == Constants.Logical);
    }

    [Fact]
    public async Task A_Rejected_SetStructMap_Leaves_An_Existing_StructMap_Intact()
    {
        // Validation runs before anything is removed, so a bad replacement cannot destroy the
        // structMap (and its dmdSecs) that is already there.
        var fullMets = await CreateAndLoadStandardMets("encoded-badid-nondestructive.xml");
        metsManager.SetStructMap(fullMets, new LogicalRange { Id = "LOG_0000", Type = "Sequence", Name = "Keep me" })
            .Success.Should().BeTrue();

        var result = metsManager.SetStructMap(fullMets, new LogicalRange { Id = "LOG_0000", Type = "Sequence",
            Ranges = [new LogicalRange { Id = "still bad", Type = "Item" }] });

        result.Failure.Should().BeTrue();
        var logical = fullMets.Mets.StructMap.Should().ContainSingle(sm => sm.Type == Constants.Logical).Subject;
        logical.Div!.Label.Should().Be("Keep me");
        fullMets.Mets.DmdSec.Should().Contain(d => d.Id == $"{Constants.DmdIdPrefix}LOG_0000");
    }

    [Fact]
    public async Task A_Range_With_No_Id_Writes_No_Id_Attribute_And_Is_Still_Replaceable()
    {
        // Third-party logical divs often have no ID; the parser reports that as an empty
        // string. Writing it back as ID="" would be an invalid xs:ID, so nothing is written -
        // and the structMap must still be identifiable for replacement.
        var fullMets = await CreateAndLoadStandardMets("encoded-empty-logical-id.xml");

        metsManager.SetStructMap(fullMets, new LogicalRange { Id = "", Type = "Sequence", Name = "First" })
            .Success.Should().BeTrue();
        fullMets.Mets.StructMap.Single(sm => sm.Type == Constants.Logical).Div!.Id.Should().BeNull();

        metsManager.SetStructMap(fullMets, new LogicalRange { Id = "", Type = "Sequence", Name = "Second" })
            .Success.Should().BeTrue();

        var logical = fullMets.Mets.StructMap.Should().ContainSingle(sm => sm.Type == Constants.Logical).Subject;
        logical.Div!.Label.Should().Be("Second", "the ID-less structMap must be replaced, not duplicated");

        metsManager.RemoveStructMap(fullMets, "");
        fullMets.Mets.StructMap.Should().NotContain(sm => sm.Type == Constants.Logical);
    }

    [Fact]
    public async Task SetStructMap_Accepts_The_Ids_The_Ui_Generates()
    {
        // The UI mints LOG_<epoch-ms>; these must keep working unchanged.
        var fullMets = await CreateAndLoadStandardMets("encoded-goodid.xml");

        var result = metsManager.SetStructMap(fullMets, new LogicalRange
        {
            Id = "LOG_1755091200000",
            Type = "Sequence",
            Ranges = [new LogicalRange { Id = "LOG_1755091200001", Type = "Item" }]
        });

        result.Success.Should().BeTrue(result.ErrorMessage ?? "");
        fullMets.Mets.StructMap.Should().Contain(sm => sm.Type == Constants.Logical);
    }

    [Fact]
    public async Task A_Logical_Div_Still_Takes_Its_Dmd_Id_From_Its_Own_Id()
    {
        // Logical divs have no path, so the DMD_ id is derived from the (validated) div ID -
        // the one case where deriving an ID from an ID is still correct.
        var fullMets = await CreateAndLoadStandardMets("encoded-logical-dmd.xml");

        var result = metsManager.SetStructMap(fullMets, new LogicalRange
        {
            Id = "LOG_0000", Type = "Sequence", Name = "Titled, so it needs MODS"
        });

        result.Success.Should().BeTrue(result.ErrorMessage ?? "");
        var logicalDiv = fullMets.Mets.StructMap.Single(sm => sm.Type == Constants.Logical).Div!;
        logicalDiv.Dmdid.Should().ContainSingle().Which.Should().Be($"{Constants.DmdIdPrefix}LOG_0000");
        fullMets.Mets.DmdSec.Should().Contain(d => d.Id == $"{Constants.DmdIdPrefix}LOG_0000");
    }

    // -----------------------------------------------------------------------
    // dmdSec IDs come from the path, so a legacy div gets a VALID new ID
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Setting_Metadata_On_A_Legacy_Div_By_Path_Mints_An_Encoded_Dmd_Id()
    {
        // The frozen fixture's file divs carry raw IDs and no DMDID. Deriving the new dmdSec ID
        // from the div's ID would mint "DMD_objects/my file.pdf" - a second invalid ID, in a
        // document we are supposed to be repairing rather than adding to.
        var metsUri = CopyFixture("path-fixture-spaces.xml", "encoded-legacy-dmd-by-path.xml");
        var fullMets = (await metsStorage.GetFullMets(metsUri, null)).Value!;

        var result = metsManager.SetAccessRestrictionsByPath(fullMets, "objects/my file.pdf", ["Closed"]);

        result.Success.Should().BeTrue(result.ErrorMessage ?? "");
        var div = fullMets.PhysicalDivsByPath["objects/my file.pdf"];
        div.Id.Should().Be("PHYS_objects/my file.pdf", "the legacy div's own ID is never rewritten");
        var dmdId = div.Dmdid.Should().ContainSingle().Subject;
        dmdId.Should().Be(MetsIds.Dmd("objects/my file.pdf"));
        AssertIsValidId(dmdId);
        fullMets.Mets.DmdSec.Should().Contain(d => d.Id == dmdId);
    }

    [Fact]
    public async Task Setting_Metadata_On_A_Legacy_Div_By_Div_Id_Also_Mints_An_Encoded_Dmd_Id()
    {
        // The ByDivId entry point is handed an ID rather than a path, so it recovers the path
        // from the cache before minting.
        var metsUri = CopyFixture("path-fixture-spaces.xml", "encoded-legacy-dmd-by-divid.xml");
        var fullMets = (await metsStorage.GetFullMets(metsUri, null)).Value!;

        var result = metsManager.SetAccessRestrictionsByDivId(fullMets, "PHYS_objects/my file.pdf", ["Closed"]);

        result.Success.Should().BeTrue(result.ErrorMessage ?? "");
        var div = fullMets.PhysicalDivsByPath["objects/my file.pdf"];
        var dmdId = div.Dmdid.Should().ContainSingle().Subject;
        dmdId.Should().Be(MetsIds.Dmd("objects/my file.pdf"));
        AssertIsValidId(dmdId);
    }

    [Fact]
    public async Task An_Existing_Legacy_Dmd_Reference_Is_Reused_Not_Replaced()
    {
        // The other half of the promise: where a legacy dmdSec is already referenced, editing
        // its metadata must go on writing to THAT section, not mint an encoded sibling.
        var metsUri = CopyFixture("path-fixture-spaces.xml", "encoded-legacy-dmd-reuse.xml");
        var fullMets = (await metsStorage.GetFullMets(metsUri, null)).Value!;
        var objectsDiv = fullMets.PhysicalDivsByPath[FolderNames.Objects];
        objectsDiv.Dmdid.Should().ContainSingle().Which.Should().Be($"{Constants.DmdIdPrefix}{FolderNames.Objects}");

        var result = metsManager.SetAccessRestrictionsByPath(fullMets, FolderNames.Objects, ["Closed"]);

        result.Success.Should().BeTrue(result.ErrorMessage ?? "");
        objectsDiv.Dmdid.Should().ContainSingle().Which.Should().Be($"{Constants.DmdIdPrefix}{FolderNames.Objects}");
    }

    // -----------------------------------------------------------------------
    // The duplicate-div guard knows both ID forms
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Adding_A_Path_Whose_Legacy_Div_Exists_But_Is_Unreachable_Is_Refused()
    {
        // Two sibling divs resolve to the same path, so navigation refuses to guess between
        // them and the edit arrives as an ADD. One of them carries the legacy raw ID for that
        // path: minting an encoded ID would not collide with it, so without a check for the
        // legacy form too the add would quietly create a THIRD div for one path.
        var uri = new Uri(new FileInfo("Outputs/encoded-legacy-duplicate-guard.xml").FullName);
        var (_, mets) = await metsManager.GetStandardMets(uri, "Legacy Duplicate METS");

        var physRoot = mets.StructMap.Single(sm => sm.Type == Constants.Physical).Div!;
        var objectsDiv = physRoot.Div.Single(d => d.Label == FolderNames.Objects);
        var objectsAmdSec = mets.AmdSec.Single(a => a.Id == MetsIds.Adm(FolderNames.Objects));
        var twinAdmId = $"{Constants.AdmIdPrefix}objects/twin";
        mets.AmdSec.Add(new AmdSecType { Id = twinAdmId, TechMd = { objectsAmdSec.TechMd[0] } });
        // Repoint the shared premis originalName so both new divs resolve to objects/twin.
        var originalName = objectsAmdSec.TechMd[0].MdWrap.XmlData.Any![0]
            .GetElementsByTagName("originalName", "http://www.loc.gov/premis/v3")[0]!;
        originalName.InnerText = "objects/twin";
        objectsDiv.Admid.Clear();
        objectsDiv.Div.Add(new DivType
        {
            Id = "PHYS_objects/twin", Type = Constants.DirectoryType, Label = "twin a", Admid = { twinAdmId }
        });
        objectsDiv.Div.Add(new DivType
        {
            Id = "PHYS_twin_b", Type = Constants.DirectoryType, Label = "twin b", Admid = { twinAdmId }
        });

        var fullMets = new FullMets { Mets = mets, Uri = uri };
        MetsCache.Populate(fullMets).Should().Contain(d => d.Contains("both resolve to path"));
        var divCountBefore = objectsDiv.Div.Count;

        var result = metsManager.AddToMets(fullMets, new WorkingDirectory
        {
            LocalPath = "objects/twin", Name = "twin", Modified = DateTime.UtcNow
        });

        result.Failure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BadRequest);
        result.ErrorMessage.Should().Contain("PHYS_objects/twin");
        objectsDiv.Div.Count.Should().Be(divCountBefore, "nothing may be added when the path is already claimed");
    }
}
