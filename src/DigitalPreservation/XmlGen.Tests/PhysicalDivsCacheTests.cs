using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;
using DigitalPreservation.XmlGen.Mets;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XmlGen.Tests;

/// <summary>
/// Tests for FullMets.PhysicalDivsByPath - the path→div cache that decouples MetsManager
/// navigation from the format of METS ID attributes (issue #188). The cache is populated from
/// premis:originalName (directories) and FLocat href (files), so these tests deliberately make
/// no assertions on ID attribute values: they must keep passing unchanged when the ID minting
/// scheme changes.
/// </summary>
public class PhysicalDivsCacheTests
{
    private readonly MetsManager metsManager;
    private readonly FileSystemMetsStorage metsStorage;

    private const string TestDigest = "eb634d64ce8e6be5195174ceaef9ac9e19c37119f3b31618630aa633ccdbf68f";

    public PhysicalDivsCacheTests()
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

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<FullMets> CreateAndLoadStandardMets(string outputFileName)
    {
        var uri = OutputUri(outputFileName);
        var createResult = await metsManager.CreateStandardMets(uri, "Cache Test METS");
        createResult.Success.Should().BeTrue(createResult.ErrorMessage ?? "");
        var loadResult = await metsManager.GetFullMets(uri, createResult.Value!.ETag);
        loadResult.Success.Should().BeTrue(loadResult.ErrorMessage ?? "");
        return loadResult.Value!;
    }

    private static Uri OutputUri(string outputFileName)
    {
        var fi = new FileInfo($"Outputs/{outputFileName}");
        return new Uri(fi.FullName);
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

    private static WorkingDirectory SimpleDirectory(string localPath, string name) =>
        new()
        {
            LocalPath = localPath,
            Name = name,
            Modified = DateTime.UtcNow
        };

    /// <summary>
    /// The maintained cache must always equal a fresh rebuild from the same Mets object -
    /// same keys, same div instances, no diagnostics.
    /// </summary>
    private static void AssertCacheMatchesRebuild(FullMets fullMets)
    {
        var rebuilt = new FullMets { Mets = fullMets.Mets, Uri = fullMets.Uri };
        var diagnostics = MetsCache.Populate(rebuilt);
        diagnostics.Should().BeEmpty();
        fullMets.PhysicalDivsByPath.Keys.Should().BeEquivalentTo(rebuilt.PhysicalDivsByPath.Keys);
        foreach (var (key, div) in fullMets.PhysicalDivsByPath)
        {
            rebuilt.PhysicalDivsByPath[key].Should().BeSameAs(div, $"cache entry '{key}' should be the same div instance a rebuild finds");
        }
    }

    private static System.Xml.XmlElement GetPremisOriginalNameElement(
        DigitalPreservation.XmlGen.Mets.Mets mets, string directoryPath)
    {
        var amdSec = mets.AmdSec.Single(a => a.Id == $"{Constants.AdmIdPrefix}{directoryPath}");
        var premisXml = amdSec.TechMd[0].MdWrap.XmlData.Any[0];
        var elements = premisXml.GetElementsByTagName("originalName", "http://www.loc.gov/premis/v3");
        return (System.Xml.XmlElement)elements[0]!;
    }

    // -----------------------------------------------------------------------
    // Population on load
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Cache_Is_Populated_When_Mets_Is_Loaded_From_Storage()
    {
        var fullMets = await CreateAndLoadStandardMets("cache-populate-on-load.xml");

        fullMets.PhysicalDivsByPath.Keys.Should().BeEquivalentTo(
            FolderNames.Objects, FolderNames.Metadata, FolderNames.MetadataAdHoc);

        // Cached divs are the actual structMap div instances, matched by label
        var physRoot = fullMets.Mets.StructMap.Single(sm => sm.Type == Constants.Physical).Div!;
        var objectsDiv = physRoot.Div.Single(d => d.Label == FolderNames.Objects);
        var metadataDiv = physRoot.Div.Single(d => d.Label == FolderNames.Metadata);
        var adHocDiv = metadataDiv.Div.Single();

        fullMets.PhysicalDivsByPath[FolderNames.Objects].Should().BeSameAs(objectsDiv);
        fullMets.PhysicalDivsByPath[FolderNames.Metadata].Should().BeSameAs(metadataDiv);
        fullMets.PhysicalDivsByPath[FolderNames.MetadataAdHoc].Should().BeSameAs(adHocDiv);
    }

    [Fact]
    public async Task Cache_Contains_Files_And_Nested_Directories_After_Reload()
    {
        var uri = OutputUri("cache-full-structure.xml");
        var createResult = await metsManager.CreateStandardMets(uri, "Cache Structure METS");
        createResult.Success.Should().BeTrue();
        var eTag = createResult.Value!.ETag;

        // Build: objects/root file.pdf, objects/sub/, objects/sub/nested.pdf
        var addFileResult = await metsManager.HandleSingleFileUpload(
            uri, SimpleFile("objects/root file.pdf", "root file.pdf"), eTag!);
        addFileResult.Success.Should().BeTrue(addFileResult.ErrorMessage ?? "");

        var loadForETag1 = await metsManager.GetFullMets(uri, null);
        var addDirResult = await metsManager.HandleCreateFolder(
            uri, SimpleDirectory("objects/sub", "sub"), loadForETag1.Value!.ETag!);
        addDirResult.Success.Should().BeTrue(addDirResult.ErrorMessage ?? "");

        var loadForETag2 = await metsManager.GetFullMets(uri, null);
        var addNestedResult = await metsManager.HandleSingleFileUpload(
            uri, SimpleFile("objects/sub/nested.pdf", "nested.pdf"), loadForETag2.Value!.ETag!);
        addNestedResult.Success.Should().BeTrue(addNestedResult.ErrorMessage ?? "");

        var fullMets = (await metsManager.GetFullMets(uri, null)).Value!;

        fullMets.PhysicalDivsByPath.Keys.Should().BeEquivalentTo(
            FolderNames.Objects, FolderNames.Metadata, FolderNames.MetadataAdHoc,
            "objects/root file.pdf", "objects/sub", "objects/sub/nested.pdf");

        // A cached file entry points at the div whose fptr resolves, through the fileSec,
        // to a FLocat href equal to the cache key
        var nestedDiv = fullMets.PhysicalDivsByPath["objects/sub/nested.pdf"];
        nestedDiv.Type.Should().Be(Constants.ItemType);
        var fileGroup = fullMets.Mets.FileSec.FileGrp.Single(fg => fg.Use == "OBJECTS");
        var nestedFile = fileGroup.File.Single(f => f.Id == nestedDiv.Fptr[0].Fileid);
        nestedFile.FLocat[0].Href.Should().Be("objects/sub/nested.pdf");

        // And the cached directory div is the parent of the nested file's div
        fullMets.PhysicalDivsByPath["objects/sub"].Div.Should().Contain(nestedDiv);
    }

    // -----------------------------------------------------------------------
    // Maintenance through mutations
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Cache_Is_Maintained_Through_Add_And_Delete_Mutations()
    {
        var fullMets = await CreateAndLoadStandardMets("cache-mutations.xml");

        var addDir = metsManager.AddToMets(fullMets, SimpleDirectory("objects/sub", "sub"));
        addDir.Success.Should().BeTrue(addDir.ErrorMessage ?? "");
        fullMets.PhysicalDivsByPath.Should().ContainKey("objects/sub");
        AssertCacheMatchesRebuild(fullMets);

        var addFile = metsManager.AddToMets(fullMets, SimpleFile("objects/sub/file one.pdf", "file one.pdf"));
        addFile.Success.Should().BeTrue(addFile.ErrorMessage ?? "");
        fullMets.PhysicalDivsByPath.Should().ContainKey("objects/sub/file one.pdf");
        AssertCacheMatchesRebuild(fullMets);

        var deleteFile = metsManager.DeleteFromMets(fullMets, "objects/sub/file one.pdf");
        deleteFile.Success.Should().BeTrue(deleteFile.ErrorMessage ?? "");
        fullMets.PhysicalDivsByPath.Should().NotContainKey("objects/sub/file one.pdf");
        AssertCacheMatchesRebuild(fullMets);

        var deleteDir = metsManager.DeleteFromMets(fullMets, "objects/sub");
        deleteDir.Success.Should().BeTrue(deleteDir.ErrorMessage ?? "");
        fullMets.PhysicalDivsByPath.Should().NotContainKey("objects/sub");
        AssertCacheMatchesRebuild(fullMets);
    }

    [Fact]
    public async Task Updating_An_Existing_File_Leaves_The_Cache_Consistent()
    {
        var fullMets = await CreateAndLoadStandardMets("cache-update-existing.xml");

        var addFile = metsManager.AddToMets(fullMets, SimpleFile("objects/doc.pdf", "doc.pdf"));
        addFile.Success.Should().BeTrue(addFile.ErrorMessage ?? "");
        var divBeforeUpdate = fullMets.PhysicalDivsByPath["objects/doc.pdf"];

        // Same path again = update, not add; div identity must not change
        var updateFile = metsManager.AddToMets(fullMets, SimpleFile("objects/doc.pdf", "doc renamed.pdf"));
        updateFile.Success.Should().BeTrue(updateFile.ErrorMessage ?? "");

        fullMets.PhysicalDivsByPath["objects/doc.pdf"].Should().BeSameAs(divBeforeUpdate);
        AssertCacheMatchesRebuild(fullMets);
    }

    // -----------------------------------------------------------------------
    // Lazy population for directly constructed FullMets
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Navigation_Works_On_A_Directly_Constructed_FullMets()
    {
        // A FullMets built around an in-memory Mets (not loaded through IMetsStorage) starts
        // with an empty cache; the first navigation populates it.
        var uri = OutputUri("cache-lazy-populate.xml");
        var (_, mets) = await metsManager.GetStandardMets(uri, "Lazy Cache METS");
        var fullMets = new FullMets { Mets = mets, Uri = uri };
        fullMets.PhysicalDivsByPath.Should().BeEmpty();

        var addFile = metsManager.AddToMets(fullMets, SimpleFile("objects/lazy.pdf", "lazy.pdf"));

        addFile.Success.Should().BeTrue(addFile.ErrorMessage ?? "");
        fullMets.PhysicalDivsByPath.Should().ContainKey("objects/lazy.pdf");
        AssertCacheMatchesRebuild(fullMets);
    }

    // -----------------------------------------------------------------------
    // Path normalisation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task OriginalName_Variants_Are_Normalised_To_Deposit_Relative_Paths()
    {
        // premis:originalName values from legacy or BagIt sources may carry a data/ prefix,
        // a leading ./ or a trailing slash; the cache keys are always the plain
        // deposit-relative path.
        var uri = OutputUri("cache-normalisation.xml");
        var (_, mets) = await metsManager.GetStandardMets(uri, "Normalisation METS");

        GetPremisOriginalNameElement(mets, FolderNames.Objects).InnerText = $"./data/{FolderNames.Objects}";
        GetPremisOriginalNameElement(mets, FolderNames.Metadata).InnerText = $"./{FolderNames.Metadata}";
        GetPremisOriginalNameElement(mets, FolderNames.MetadataAdHoc).InnerText = $"data/{FolderNames.MetadataAdHoc}/";

        var fullMets = new FullMets { Mets = mets, Uri = uri };
        var diagnostics = MetsCache.Populate(fullMets);

        diagnostics.Should().BeEmpty();
        fullMets.PhysicalDivsByPath.Keys.Should().BeEquivalentTo(
            FolderNames.Objects, FolderNames.Metadata, FolderNames.MetadataAdHoc);
    }

    // -----------------------------------------------------------------------
    // Malformed METS: diagnostics and defensive navigation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Two_Divs_Resolving_To_The_Same_Path_Are_Reported_And_The_Real_Div_Still_Wins()
    {
        var uri = OutputUri("cache-duplicate-path.xml");
        var (_, mets) = await metsManager.GetStandardMets(uri, "Duplicate Path METS");

        // An impostor div, nested under metadata/, whose ADMID points at the objects
        // amdSec - so it resolves to the same path as the real objects div (and, being
        // walked first, wins the cache slot).
        var physRoot = mets.StructMap.Single(sm => sm.Type == Constants.Physical).Div!;
        var metadataDiv = physRoot.Div.Single(d => d.Label == FolderNames.Metadata);
        metadataDiv.Div.Add(new DivType
        {
            Id = "PHYS_impostor",
            Type = Constants.DirectoryType,
            Label = "impostor",
            Admid = { $"{Constants.AdmIdPrefix}{FolderNames.Objects}" }
        });

        var fullMets = new FullMets { Mets = mets, Uri = uri };
        var diagnostics = MetsCache.Populate(fullMets);

        diagnostics.Should().ContainSingle(d => d.Contains($"both resolve to path '{FolderNames.Objects}'"));

        // Navigation must not be captured by the impostor: the fallback resolves the REAL
        // objects div (a direct child of the current div) and the edit lands there.
        var addFile = metsManager.AddToMets(fullMets, SimpleFile("objects/f.txt", "f.txt"));
        addFile.Success.Should().BeTrue(addFile.ErrorMessage ?? "");

        var realObjectsDiv = physRoot.Div.Single(d => d.Label == FolderNames.Objects);
        realObjectsDiv.Div.Should().ContainSingle(d => d.Label == "f.txt");
        metadataDiv.Div.Single(d => d.Label == "impostor").Div.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Second_Physical_StructMap_Is_A_Diagnostic_Not_An_Exception()
    {
        var uri = OutputUri("cache-two-physical.xml");
        var (_, mets) = await metsManager.GetStandardMets(uri, "Two Physical METS");
        mets.StructMap.Add(new StructMapType
        {
            Type = Constants.Physical,
            Div = new DivType { Id = "PHYS_ROOT_2", Label = "second", Type = Constants.DirectoryType }
        });

        var fullMets = new FullMets { Mets = mets, Uri = uri };
        var diagnostics = MetsCache.Populate(fullMets);

        diagnostics.Should().ContainSingle(d => d.Contains("2 PHYSICAL structMaps"));

        // The first structMap is still fully navigable
        var addFile = metsManager.AddToMets(fullMets, SimpleFile("objects/first.pdf", "first.pdf"));
        addFile.Success.Should().BeTrue(addFile.ErrorMessage ?? "");
    }

    [Fact]
    public async Task Failed_File_Add_Leaves_Mets_And_Cache_Untouched_And_Retry_Succeeds()
    {
        var fullMets = await CreateAndLoadStandardMets("cache-failed-add.xml");
        var physRoot = fullMets.Mets.StructMap.Single(sm => sm.Type == Constants.Physical).Div!;
        var objectsDiv = physRoot.Div.Single(d => d.Label == FolderNames.Objects);
        var dmdSecCountBefore = fullMets.Mets.DmdSec.Count;
        var amdSecCountBefore = fullMets.Mets.AmdSec.Count;

        // Conflicting digest metadata makes ProcessAllFileMetadata fail
        var badFile = SimpleFile("objects/new.tif", "new.tif");
        badFile.Metadata.Add(new DigestMetadata { Digest = "aaa", Source = "test-a" });
        badFile.Metadata.Add(new DigestMetadata { Digest = "bbb", Source = "test-b" });

        var failedAdd = metsManager.AddToMets(fullMets, badFile);

        failedAdd.Failure.Should().BeTrue();
        objectsDiv.Div.Should().BeEmpty("a failed add must not leave a half-added div behind");
        fullMets.Mets.DmdSec.Count.Should().Be(dmdSecCountBefore);
        fullMets.Mets.AmdSec.Count.Should().Be(amdSecCountBefore);
        AssertCacheMatchesRebuild(fullMets);

        // The retry with clean metadata succeeds and produces exactly one div
        var retry = metsManager.AddToMets(fullMets, SimpleFile("objects/new.tif", "new.tif"));
        retry.Success.Should().BeTrue(retry.ErrorMessage ?? "");
        objectsDiv.Div.Should().ContainSingle(d => d.Label == "new.tif");
        AssertCacheMatchesRebuild(fullMets);
    }

    [Fact]
    public async Task Setting_Metadata_By_An_Unresolvable_Path_Does_Not_Write_To_An_Ancestor()
    {
        var fullMets = await CreateAndLoadStandardMets("cache-setbypath-miss.xml");
        var dmdSecCountBefore = fullMets.Mets.DmdSec.Count;

        metsManager.SetAccessRestrictionsByPath(fullMets, "objects/missing/file.txt", ["Closed"]);

        // No dmdSec was created for (and no accessCondition written to) any ancestor div
        fullMets.Mets.DmdSec.Count.Should().Be(dmdSecCountBefore);
    }

    [Fact]
    public async Task File_With_Data_Prefixed_FLocat_Href_Is_Navigable_And_Deletable()
    {
        var fullMets = await CreateAndLoadStandardMets("cache-data-href.xml");
        var addFile = metsManager.AddToMets(fullMets, SimpleFile("objects/x.pdf", "x.pdf"));
        addFile.Success.Should().BeTrue(addFile.ErrorMessage ?? "");

        // Simulate a legacy variant: the FLocat href carries the BagIt data/ prefix
        var fileGroup = fullMets.Mets.FileSec.FileGrp.Single(fg => fg.Use == "OBJECTS");
        fileGroup.File.Single().FLocat[0].Href = "data/objects/x.pdf";
        MetsCache.Populate(fullMets);
        fullMets.PhysicalDivsByPath.Should().ContainKey("objects/x.pdf");

        var deleteFile = metsManager.DeleteFromMets(fullMets, "objects/x.pdf");

        deleteFile.Success.Should().BeTrue(deleteFile.ErrorMessage ?? "");
        AssertCacheMatchesRebuild(fullMets);
    }

    [Fact]
    public async Task Navigation_Failure_Reports_Path_Diagnostics_In_The_Error()
    {
        var uri = OutputUri("cache-error-diagnostics.xml");
        var (_, mets) = await metsManager.GetStandardMets(uri, "Error Diagnostics METS");

        // Break the objects div beyond all fallbacks: no resolvable path metadata AND a
        // non-convention ID, so navigation below it cannot succeed.
        var physRoot = mets.StructMap.Single(sm => sm.Type == Constants.Physical).Div!;
        var objectsDiv = physRoot.Div.Single(d => d.Label == FolderNames.Objects);
        objectsDiv.Id = "PHYS_broken";
        objectsDiv.Admid.Clear();

        var fullMets = new FullMets { Mets = mets, Uri = uri };
        var addFile = metsManager.AddToMets(fullMets, SimpleFile("objects/f.txt", "f.txt"));

        addFile.Failure.Should().BeTrue();
        addFile.ErrorMessage.Should().Contain("METS path diagnostics")
            .And.Contain("PHYS_broken");
    }

    [Fact]
    public async Task Unresolvable_Divs_Are_Reported_As_Diagnostics_And_Left_Out_Of_The_Cache()
    {
        var uri = OutputUri("cache-diagnostics.xml");
        var (_, mets) = await metsManager.GetStandardMets(uri, "Diagnostics METS");
        var physRoot = mets.StructMap.Single(sm => sm.Type == Constants.Physical).Div!;

        physRoot.Div.Add(new DivType { Id = "PHYS_noadm", Type = Constants.DirectoryType, Label = "noadm" });
        physRoot.Div.Add(new DivType { Id = "PHYS_nofptr", Type = Constants.ItemType, Label = "nofptr" });
        physRoot.Div.Add(new DivType
        {
            Id = "PHYS_dangling",
            Type = Constants.ItemType,
            Label = "dangling",
            Fptr = { new DivTypeFptr { Fileid = "FILE_missing" } }
        });
        physRoot.Div.Add(new DivType { Id = "PHYS_odd", Type = "banana", Label = "odd" });

        var fullMets = new FullMets { Mets = mets, Uri = uri };
        var diagnostics = MetsCache.Populate(fullMets);

        diagnostics.Should().HaveCount(4);
        diagnostics.Should().ContainSingle(d => d.Contains("PHYS_noadm") && d.Contains("no ADMID"));
        diagnostics.Should().ContainSingle(d => d.Contains("PHYS_nofptr") && d.Contains("no fptr"));
        diagnostics.Should().ContainSingle(d => d.Contains("PHYS_dangling") && d.Contains("FILE_missing"));
        diagnostics.Should().ContainSingle(d => d.Contains("PHYS_odd") && d.Contains("banana"));

        fullMets.PhysicalDivsByPath.Keys.Should().BeEquivalentTo(
            FolderNames.Objects, FolderNames.Metadata, FolderNames.MetadataAdHoc);
    }

    // -----------------------------------------------------------------------
    // Legacy METS: IDs with spaces (frozen fixture)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Legacy_Mets_With_Spaces_In_Ids_Is_Cached_And_Editable_By_Path()
    {
        // The frozen fixture's ID attributes contain spaces, so its ADMID values were split
        // into multiple IDREFS tokens on deserialization; the cache builder must rejoin them
        // to find each directory's premis:originalName.
        var metsUri = CopyFixture("path-fixture-spaces.xml", "cache-legacy-spaces.xml");
        var loadResult = await metsStorage.GetFullMets(metsUri, null);
        loadResult.Success.Should().BeTrue(loadResult.ErrorMessage ?? "");
        var fullMets = loadResult.Value!;

        fullMets.PhysicalDivsByPath.Keys.Should().Contain(
        [
            "objects/my file.pdf",
            "objects/my great file.pdf",
            "objects/my folder",
            "objects/my folder/my document.pdf"
        ]);

        var deleteFile = metsManager.DeleteFromMets(fullMets, "objects/my folder/my document.pdf");
        deleteFile.Success.Should().BeTrue(deleteFile.ErrorMessage ?? "");

        var deleteDir = metsManager.DeleteFromMets(fullMets, "objects/my folder");
        deleteDir.Success.Should().BeTrue(deleteDir.ErrorMessage ?? "");

        fullMets.PhysicalDivsByPath.Keys.Should().NotContain(
            ["objects/my folder", "objects/my folder/my document.pdf"]);
        AssertCacheMatchesRebuild(fullMets);
    }
}
