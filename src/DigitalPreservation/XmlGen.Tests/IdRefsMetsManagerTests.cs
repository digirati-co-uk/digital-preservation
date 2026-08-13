using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;
using DigitalPreservation.XmlGen.Mets;
using DigitalPreservation.XmlGen.Mods.V3;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XmlGen.Tests;

/// <summary>
/// MetsManager-side IDREFS handling (IdRefs): legacy space-containing IDs arrive from the
/// XmlSerializer as several tokens and must resolve via the rejoined form, while genuine
/// multi-reference IDREFS must resolve per token. Deletion is tolerant of references that
/// don't resolve. See docs/issues/188/issue-188-idrefs-plan.md.
/// </summary>
public class IdRefsMetsManagerTests
{
    private readonly MetsManager metsManager;
    private readonly FileSystemMetsStorage metsStorage;

    private const string TestDigest = "eb634d64ce8e6be5195174ceaef9ac9e19c37119f3b31618630aa633ccdbf68f";

    public IdRefsMetsManagerTests()
    {
        var serviceProvider = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

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
        var fi = new FileInfo($"Outputs/{outputFileName}");
        var uri = new Uri(fi.FullName);
        var createResult = await metsManager.CreateStandardMets(uri, "IdRefs Test METS");
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

    private static WorkingFile SimpleFile(string localPath, string name) =>
        new()
        {
            LocalPath = localPath,
            Name = name,
            ContentType = "application/pdf",
            Digest = TestDigest,
            Size = 54321,
            Modified = DateTime.UtcNow
        };

    [Fact]
    public async Task Directory_With_Genuine_Multi_Token_Admid_Resolves_Via_A_Later_Token()
    {
        var fullMets = await CreateAndLoadStandardMets("idrefs-multi-admid.xml");
        var physRoot = fullMets.Mets.StructMap.Single(sm => sm.Type == Constants.Physical).Div!;
        var objectsDiv = physRoot.Div.Single(d => d.Label == FolderNames.Objects);

        // A genuine two-reference ADMID: the first token resolves nothing, the second is the
        // real objects amdSec (carrying premis:originalName).
        objectsDiv.Admid.Insert(0, "ADM_missing");

        var diagnostics = MetsCache.Populate(fullMets);

        diagnostics.Should().BeEmpty();
        fullMets.PhysicalDivsByPath[FolderNames.Objects].Should().BeSameAs(objectsDiv);

        // And navigation-dependent editing still works
        var addFile = metsManager.AddToMets(fullMets, SimpleFile("objects/multi.pdf", "multi.pdf"));
        addFile.Success.Should().BeTrue(addFile.ErrorMessage ?? "");
    }

    [Fact]
    public async Task Deleting_A_Div_With_A_Dangling_Dmdid_Succeeds()
    {
        var fullMets = await CreateAndLoadStandardMets("idrefs-dangling-dmdid.xml");
        var addDir = metsManager.AddToMets(fullMets, new WorkingDirectory
        {
            LocalPath = "objects/sub",
            Name = "sub",
            Modified = DateTime.UtcNow
        });
        addDir.Success.Should().BeTrue(addDir.ErrorMessage ?? "");

        // DMDID references dangle by design until metadata is first set (lazy dmdSec
        // creation) - deleting such a div must not fail on the unresolvable reference.
        fullMets.PhysicalDivsByPath["objects/sub"].Dmdid.Add("DMD_dangling");

        var deleteDir = metsManager.DeleteFromMets(fullMets, "objects/sub");

        deleteDir.Success.Should().BeTrue(deleteDir.ErrorMessage ?? "");
        fullMets.PhysicalDivsByPath.Should().NotContainKey("objects/sub");
    }

    [Fact]
    public async Task Deleting_A_File_Whose_AmdSec_Is_Missing_Succeeds()
    {
        var fullMets = await CreateAndLoadStandardMets("idrefs-missing-amdsec.xml");
        var addFile = metsManager.AddToMets(fullMets, SimpleFile("objects/orphan.pdf", "orphan.pdf"));
        addFile.Success.Should().BeTrue(addFile.ErrorMessage ?? "");

        // Break the METS: remove the file's amdSec, leaving its ADMID dangling. Deletion is
        // how broken content gets cleaned up, so it must still succeed.
        var div = fullMets.PhysicalDivsByPath["objects/orphan.pdf"];
        var fileGroup = fullMets.Mets.FileSec.FileGrp.Single(fg => fg.Use == "OBJECTS");
        var file = fileGroup.File.Single(f => f.Id == div.Fptr[0].Fileid);
        var amdSec = IdRefs.ResolveSingle(file.Admid, id => fullMets.Mets.AmdSec.FirstOrDefault(a => a.Id == id));
        fullMets.Mets.AmdSec.Remove(amdSec!);

        var deleteFile = metsManager.DeleteFromMets(fullMets, "objects/orphan.pdf");

        deleteFile.Success.Should().BeTrue(deleteFile.ErrorMessage ?? "");
        fileGroup.File.Should().BeEmpty();
        fullMets.PhysicalDivsByPath.Should().NotContainKey("objects/orphan.pdf");
    }

    [Fact]
    public async Task Deleting_A_File_With_Genuine_Multi_Admid_Removes_Its_Sections_But_Keeps_Shared_Ones()
    {
        var fullMets = await CreateAndLoadStandardMets("idrefs-multi-admid-delete.xml");
        metsManager.AddToMets(fullMets, SimpleFile("objects/a.pdf", "a.pdf")).Success.Should().BeTrue();
        metsManager.AddToMets(fullMets, SimpleFile("objects/b.pdf", "b.pdf")).Success.Should().BeTrue();

        var fileGroup = fullMets.Mets.FileSec.FileGrp.Single(fg => fg.Use == "OBJECTS");
        var fileA = fileGroup.File.Single(
            f => f.Id == fullMets.PhysicalDivsByPath["objects/a.pdf"].Fptr[0].Fileid);
        var fileB = fileGroup.File.Single(
            f => f.Id == fullMets.PhysicalDivsByPath["objects/b.pdf"].Fptr[0].Fileid);
        var ownAdmId = fileA.Admid.Single();

        // Give a.pdf a genuine multi-token ADMID: its own amdSec, an extra section of its
        // own, and a section shared with b.pdf (the Archivematica shared-rightsMD pattern).
        fullMets.Mets.AmdSec.Add(new AmdSecType { Id = "ADM_rights_a" });
        fullMets.Mets.AmdSec.Add(new AmdSecType { Id = "ADM_rights_shared" });
        fileA.Admid.Add("ADM_rights_a");
        fileA.Admid.Add("ADM_rights_shared");
        fileB.Admid.Add("ADM_rights_shared");

        var delete = metsManager.DeleteFromMets(fullMets, "objects/a.pdf");

        delete.Success.Should().BeTrue(delete.ErrorMessage ?? "");
        fullMets.Mets.AmdSec.Should().NotContain(a => a.Id == ownAdmId);
        fullMets.Mets.AmdSec.Should().NotContain(a => a.Id == "ADM_rights_a");
        fullMets.Mets.AmdSec.Should().Contain(a => a.Id == "ADM_rights_shared");
    }

    [Fact]
    public async Task Replacing_A_Logical_StructMap_Removes_A_Legacy_Spaced_DmdSec()
    {
        var fullMets = await CreateAndLoadStandardMets("idrefs-logical-replace.xml");
        metsManager.SetStructMap(fullMets, new LogicalRange { Id = "RANGE_1", Type = "Sequence" });

        // Simulate a legacy structMap div carrying a space-containing DMDID: the
        // XmlSerializer delivers it as two tokens, but the dmdSec ID is the whole string.
        var logical = fullMets.Mets.StructMap.Single(sm => sm.Type == Constants.Logical);
        fullMets.Mets.DmdSec.Add(new MdSecType { Id = "DMD_my range" });
        logical.Div.Dmdid.Add("DMD_my");
        logical.Div.Dmdid.Add("range");

        metsManager.SetStructMap(fullMets, new LogicalRange { Id = "RANGE_1", Type = "Sequence" });

        fullMets.Mets.DmdSec.Should().NotContain(d => d.Id == "DMD_my range");
        fullMets.Mets.StructMap.Count(sm => sm.Type == Constants.Logical).Should().Be(1);
    }

    [Fact]
    public async Task Clearing_Mods_On_A_Div_With_Genuine_Multi_Dmdid_Keeps_The_Other_DmdSec()
    {
        var fullMets = await CreateAndLoadStandardMets("idrefs-multi-dmdid-mods.xml");
        var physRoot = fullMets.Mets.StructMap.Single(sm => sm.Type == Constants.Physical).Div!;
        var objectsDiv = physRoot.Div.Single(d => d.Label == FolderNames.Objects);

        // Give the div MODS content (lazily creating its own dmdSec)...
        var mods = ModsManager.GetModsForDiv(fullMets.Mets, objectsDiv, createDmd: true)!;
        mods.AddAccessCondition("Restricted", Constants.RestrictionOnAccess);
        ModsManager.SetModsForDiv(fullMets.Mets, objectsDiv, mods);
        var ownDmdId = objectsDiv.Dmdid.Single();

        // ...then a second, genuine reference to another real dmdSec.
        fullMets.Mets.DmdSec.Add(new MdSecType { Id = "DMD_other" });
        objectsDiv.Dmdid.Add("DMD_other");

        // Clearing the MODS must prune only the div's own (now empty) dmdSec and its
        // reference - not the other genuinely-referenced dmdSec.
        ModsManager.SetModsForDiv(fullMets.Mets, objectsDiv, new ModsDefinition());

        fullMets.Mets.DmdSec.Should().NotContain(d => d.Id == ownDmdId);
        fullMets.Mets.DmdSec.Should().Contain(d => d.Id == "DMD_other");
        objectsDiv.Dmdid.Should().Equal("DMD_other");
    }

    [Fact]
    public async Task Clearing_Mods_Sweeps_Dangling_Sibling_Tokens_But_Keeps_Live_Ones()
    {
        var fullMets = await CreateAndLoadStandardMets("idrefs-dangling-sibling-sweep.xml");
        var physRoot = fullMets.Mets.StructMap.Single(sm => sm.Type == Constants.Physical).Div!;
        var objectsDiv = physRoot.Div.Single(d => d.Label == FolderNames.Objects);

        var mods = ModsManager.GetModsForDiv(fullMets.Mets, objectsDiv, createDmd: true)!;
        mods.AddAccessCondition("Restricted", Constants.RestrictionOnAccess);
        ModsManager.SetModsForDiv(fullMets.Mets, objectsDiv, mods);
        var ownDmdId = objectsDiv.Dmdid.Single();

        // A three-token DMDID: the div's own dmdSec, a dangling token, and a live reference.
        fullMets.Mets.DmdSec.Add(new MdSecType { Id = "DMD_other" });
        objectsDiv.Dmdid.Add("DMD_dangling");
        objectsDiv.Dmdid.Add("DMD_other");

        ModsManager.SetModsForDiv(fullMets.Mets, objectsDiv, new ModsDefinition());

        // Own dmdSec pruned, dangling sibling swept, live reference (and its dmdSec) kept.
        fullMets.Mets.DmdSec.Should().NotContain(d => d.Id == ownDmdId);
        fullMets.Mets.DmdSec.Should().Contain(d => d.Id == "DMD_other");
        objectsDiv.Dmdid.Should().Equal("DMD_other");
    }

    [Fact]
    public async Task Clearing_Mods_Keeps_A_Live_Legacy_Reference_That_Arrives_As_Several_Tokens()
    {
        // Issue #217. The sweep used to compare each DMDID token verbatim against dmdSec.Id -
        // but a legacy space-containing ID arrives as SEVERAL tokens, none of which equals any
        // dmdSec ID on its own. So a LIVE reference read as several dangling ones was stripped,
        // costing the div its descriptive record and orphaning the section.
        var fullMets = await CreateAndLoadStandardMets("idrefs-legacy-token-sweep.xml");
        var physRoot = fullMets.Mets.StructMap.Single(sm => sm.Type == Constants.Physical).Div!;
        var objectsDiv = physRoot.Div.Single(d => d.Label == FolderNames.Objects);

        var mods = ModsManager.GetModsForDiv(fullMets.Mets, objectsDiv, createDmd: true)!;
        mods.AddAccessCondition("Restricted", Constants.RestrictionOnAccess);
        ModsManager.SetModsForDiv(fullMets.Mets, objectsDiv, mods);
        var ownDmdId = objectsDiv.Dmdid.Single();

        // A legacy dmdSec whose ID contains a space, referenced the way the XmlSerializer
        // presents it: two tokens that mean one ID.
        const string legacyDmdId = "DMD_objects/my file.pdf";
        fullMets.Mets.DmdSec.Add(new MdSecType { Id = legacyDmdId });
        objectsDiv.Dmdid.Clear();
        objectsDiv.Dmdid.Add("DMD_objects/my");
        objectsDiv.Dmdid.Add("file.pdf");
        // ...and re-attach the div's own dmdSec so there is something to prune.
        objectsDiv.Dmdid.Insert(0, ownDmdId);

        ModsManager.SetModsForDiv(fullMets.Mets, objectsDiv, new ModsDefinition());

        // The div's own dmdSec goes; the legacy reference and its section survive intact.
        fullMets.Mets.DmdSec.Should().NotContain(d => d.Id == ownDmdId);
        fullMets.Mets.DmdSec.Should().Contain(d => d.Id == legacyDmdId);
        objectsDiv.Dmdid.Should().Equal("DMD_objects/my", "file.pdf");
        IdRefs.Joined(objectsDiv.Dmdid).Should().Be(legacyDmdId);
    }

    [Fact]
    public async Task Setting_Mods_On_A_Div_With_Genuine_Multi_Dmdid_Never_Mutates_The_Other_DmdSec()
    {
        var fullMets = await CreateAndLoadStandardMets("idrefs-multi-dmdid-set.xml");
        var physRoot = fullMets.Mets.StructMap.Single(sm => sm.Type == Constants.Physical).Div!;
        var objectsDiv = physRoot.Div.Single(d => d.Label == FolderNames.Objects);

        var mods = ModsManager.GetModsForDiv(fullMets.Mets, objectsDiv, createDmd: true)!;
        var ownDmdId = objectsDiv.Dmdid.Single();
        var otherDmd = new MdSecType { Id = "DMD_other" };
        fullMets.Mets.DmdSec.Add(otherDmd);
        objectsDiv.Dmdid.Add("DMD_other");

        // The first-resolving dmdSec is the div's editable MODS record; the second referenced
        // dmdSec is third-party content and must come through a write completely untouched.
        mods.AddAccessCondition("Restricted", Constants.RestrictionOnAccess);
        ModsManager.SetModsForDiv(fullMets.Mets, objectsDiv, mods);

        ModsManager.GetModsForDiv(fullMets.Mets, objectsDiv)!
            .GetAccessConditions(Constants.RestrictionOnAccess).Should().Equal("Restricted");
        otherDmd.MdWrap.Should().BeNull();
        objectsDiv.Dmdid.Should().Equal(ownDmdId, "DMD_other");
    }

    [Fact]
    public async Task Clearing_Mods_On_One_Referrer_Of_A_Shared_DmdSec_Keeps_The_Section()
    {
        var fullMets = await CreateAndLoadStandardMets("idrefs-shared-dmdid-clear.xml");
        var physRoot = fullMets.Mets.StructMap.Single(sm => sm.Type == Constants.Physical).Div!;
        var objectsDiv = physRoot.Div.Single(d => d.Label == FolderNames.Objects);
        var metadataDiv = physRoot.Div.Single(d => d.Label == FolderNames.Metadata);

        var mods = ModsManager.GetModsForDiv(fullMets.Mets, objectsDiv, createDmd: true)!;
        mods.AddAccessCondition("Restricted", Constants.RestrictionOnAccess);
        ModsManager.SetModsForDiv(fullMets.Mets, objectsDiv, mods);
        var sharedDmdId = objectsDiv.Dmdid.Single();

        // A second div genuinely references the same dmdSec (Goobi-style sharing).
        metadataDiv.Dmdid.Add(sharedDmdId);

        // Clearing the FIRST referrer's MODS drops only its reference - the shared section
        // and the other referrer are untouched (parity with the deletion sites' guard).
        ModsManager.SetModsForDiv(fullMets.Mets, objectsDiv, new ModsDefinition());

        fullMets.Mets.DmdSec.Should().Contain(d => d.Id == sharedDmdId);
        objectsDiv.Dmdid.Should().BeEmpty();
        metadataDiv.Dmdid.Should().Contain(sharedDmdId);
    }

    [Fact]
    public async Task Legacy_Spaced_Div_Mods_Can_Be_Set_Read_Back_And_Cleared()
    {
        // The frozen fixture's div IDs contain spaces, so the derived DMDID
        // ("DMD_objects/my file.pdf") splits into tokens on serialization - setting,
        // re-reading and clearing MODS must all resolve it via the rejoined form.
        var metsUri = CopyFixture("path-fixture-spaces.xml", "idrefs-legacy-mods.xml");
        var loadResult = await metsStorage.GetFullMets(metsUri, null);
        loadResult.Success.Should().BeTrue(loadResult.ErrorMessage ?? "");
        var fullMets = loadResult.Value!;
        var spacedDiv = fullMets.PhysicalDivsByPath["objects/my file.pdf"];

        var mods = ModsManager.GetModsForDiv(fullMets.Mets, spacedDiv, createDmd: true)!;
        mods.AddAccessCondition("Restricted", Constants.RestrictionOnAccess);
        ModsManager.SetModsForDiv(fullMets.Mets, spacedDiv, mods);

        ModsManager.GetModsForDiv(fullMets.Mets, spacedDiv)!
            .GetAccessConditions(Constants.RestrictionOnAccess).Should().Equal("Restricted");
        fullMets.Mets.DmdSec.Should().Contain(d => d.Id == "DMD_objects/my file.pdf");

        ModsManager.SetModsForDiv(fullMets.Mets, spacedDiv, new ModsDefinition());

        fullMets.Mets.DmdSec.Should().NotContain(d => d.Id == "DMD_objects/my file.pdf");
        spacedDiv.Dmdid.Should().BeEmpty();
    }

    [Fact]
    public async Task Legacy_Spaced_SmLinks_Reference_File_Ids_That_Exist_In_The_FileSec()
    {
        // Pins the invariant issue #188 step 2 must preserve: a link minted against a LEGACY
        // document must reference the FILE element's ACTUAL (raw, space-containing) ID.
        // If step 2 changes LinkFile to mint encoded IDs instead of resolving real ones,
        // this test fails - see the step 2 checklist in issue-188-plan.md.
        var metsUri = CopyFixture("path-fixture-spaces.xml", "idrefs-legacy-smlink.xml");
        var loadResult = await metsStorage.GetFullMets(metsUri, null);
        loadResult.Success.Should().BeTrue(loadResult.ErrorMessage ?? "");
        var fullMets = loadResult.Value!;

        metsManager.LinkFile(fullMets, "objects/my file.pdf", "objects/my file.pdf",
            new Uri("http://example.org/roles/transcription-of"));

        var link = fullMets.Mets.StructLink!.SmLink.OfType<StructLinkTypeSmLink>().Single();
        var fileIds = fullMets.Mets.FileSec.FileGrp.SelectMany(fg => fg.File).Select(f => f.Id).ToList();
        fileIds.Should().Contain(link.From);
        fileIds.Should().Contain(link.To);
    }

    [Fact]
    public async Task Updating_A_Legacy_Spaced_File_Resolves_Its_AmdSec_Via_The_Rejoined_Tokens()
    {
        // The frozen fixture's file IDs contain spaces, so ADMID arrives as multiple tokens;
        // updating the file exercises MetadataManager's amdSec resolution through IdRefs.
        var metsUri = CopyFixture("path-fixture-spaces.xml", "idrefs-legacy-update.xml");
        var loadResult = await metsStorage.GetFullMets(metsUri, null);
        loadResult.Success.Should().BeTrue(loadResult.ErrorMessage ?? "");
        var fullMets = loadResult.Value!;

        var update = metsManager.AddToMets(fullMets, SimpleFile("objects/my file.pdf", "my file.pdf"));

        update.Success.Should().BeTrue(update.ErrorMessage ?? "");
    }
}
