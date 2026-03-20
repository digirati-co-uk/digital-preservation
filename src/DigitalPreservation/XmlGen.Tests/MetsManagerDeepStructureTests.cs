using System.Xml.Linq;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XmlGen.Tests;

/// <summary>
/// Builds the same complex, multi-level directory structure three different ways and
/// asserts that all three approaches produce identical results.
///
/// The structure under objects/ is:
///
///   series-a/                   ← no direct files; only subdirectories
///     sub-a1/                   ← 2 pdf files + 1 subdir
///       doc-a1-001.pdf
///       doc-a1-002.pdf
///       sub-a1-deep/            ← 3 pdf files + 1 subdir (3 levels)
///         deep-001.pdf
///         deep-002.pdf
///         deep-003.pdf
///         sub-a1-deeper/        ← 1 pdf file; leaf at 4 levels
///           deepest-001.pdf
///     sub-a2/                   ← 3 pdf files; leaf dir
///       doc-a2-001.pdf
///       doc-a2-002.pdf
///       doc-a2-003.pdf
///   series-b/                   ← 1 direct pdf + 2 subdirs
///     cover-b.pdf
///     sub-b1/                   ← 5 tif files; leaf dir (many files)
///       page-001.tif
///       page-002.tif
///       page-003.tif
///       page-004.tif
///       page-005.tif
///     sub-b2/                   ← no files; only 1 subdir
///       appendix/               ← 2 pdf files; leaf at 3 levels
///         appendix-001.pdf
///         appendix-002.pdf
///   series-c/                   ← 8 tif files; no subdirs (many files)
///     scan-001.tif … scan-008.tif
///
/// Test 1 — Incremental: every directory and file is added via the async
///   HandleCreateFolder / HandleSingleFileUpload methods, fetching a fresh ETag
///   after each operation.
///
/// Test 2 — Declarative: the entire tree is described as an ordered sequence of
///   WorkingBase objects (parents before children). A single GetFullMets +
///   loop of AddToMets calls + one WriteMets persists the whole structure at once.
///
/// Test 3 — Combined: series-a is built with the incremental Handle* methods;
///   then GetFullMets loads the current state into memory, and series-b / series-c
///   are appended via AddToMets before a single WriteMets writes everything out.
///   This verifies that the two approaches can be freely interleaved.
///
/// All three tests run through the same assertion method which checks:
///   • MetsParser round-trip: directory counts, file counts, LocalPaths, and Names
///     at every level of the tree.
///   • Raw XML: a selection of ID attributes (PHYS_*, ADM_*, FILE_*, TECH_*) are
///     present and cross-reference each other correctly.
/// </summary>
public class MetsManagerDeepStructureTests
{
    private readonly MetsManager metsManager;
    private readonly MetsParser parser;

    private static readonly XNamespace MetsNs = "http://www.loc.gov/METS/";
    private static readonly XNamespace XLinkNs = "http://www.w3.org/1999/xlink";

    private const string PdfDigest = "eb634d64ce8e6be5195174ceaef9ac9e19c37119f3b31618630aa633ccdbf68f";
    private const string TifDigest  = "801d4a031510adb61ae11412c1554fbaa769a6b4428225ad87a489f92889f105";

    public MetsManagerDeepStructureTests()
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
    // Data helpers
    // -----------------------------------------------------------------------

    private static WorkingFile Pdf(string localPath) =>
        new()
        {
            LocalPath = localPath,
            Name = localPath.Split('/').Last(),
            ContentType = "application/pdf",
            Digest = PdfDigest,
            Size = 102400,
            Modified = DateTime.UtcNow
        };

    private static WorkingFile Tif(string localPath) =>
        new()
        {
            LocalPath = localPath,
            Name = localPath.Split('/').Last(),
            ContentType = "image/tiff",
            Digest = TifDigest,
            Size = 4096000,
            Modified = DateTime.UtcNow
        };

    private static WorkingDirectory Dir(string localPath, string label) =>
        new()
        {
            LocalPath = localPath,
            Name = label,
            Modified = DateTime.UtcNow
        };

    // -----------------------------------------------------------------------
    // Incremental helpers (Handle* — each call reads a fresh ETag)
    // -----------------------------------------------------------------------

    private async Task<string> CreateAndGetETag(Uri metsUri, string label)
    {
        var result = await metsManager.CreateStandardMets(metsUri, label);
        result.Success.Should().BeTrue();
        return result.Value!.ETag!;
    }

    private async Task<string> AddFolderGetETag(Uri metsUri, string localPath, string label, string eTag)
    {
        var result = await metsManager.HandleCreateFolder(metsUri, Dir(localPath, label), eTag);
        result.Success.Should().BeTrue(result.ErrorMessage ?? $"HandleCreateFolder failed for {localPath}");
        return (await parser.GetMetsFileWrapper(metsUri)).Value!.ETag!;
    }

    private async Task<string> AddFileGetETag(Uri metsUri, WorkingFile file, string eTag)
    {
        var result = await metsManager.HandleSingleFileUpload(metsUri, file, eTag);
        result.Success.Should().BeTrue(result.ErrorMessage ?? $"HandleSingleFileUpload failed for {file.LocalPath}");
        return (await parser.GetMetsFileWrapper(metsUri)).Value!.ETag!;
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Incremental_Approach_Builds_Deep_Structure()
    {
        // Every directory and file is created with the async Handle* methods.
        // A fresh ETag is fetched from the parser after each successful call so
        // that the concurrency guard is satisfied for the next operation.

        var metsFi = new FileInfo("Outputs/deep-incremental.xml");
        var metsUri = new Uri(metsFi.FullName);
        var eTag = await CreateAndGetETag(metsUri, "Deep Structure (Incremental)");

        // series-a: directory-only at the top level
        eTag = await AddFolderGetETag(metsUri, "objects/series-a", "Series A", eTag);

        eTag = await AddFolderGetETag(metsUri, "objects/series-a/sub-a1", "Sub A1", eTag);
        eTag = await AddFileGetETag(metsUri, Pdf("objects/series-a/sub-a1/doc-a1-001.pdf"), eTag);
        eTag = await AddFileGetETag(metsUri, Pdf("objects/series-a/sub-a1/doc-a1-002.pdf"), eTag);

        eTag = await AddFolderGetETag(metsUri, "objects/series-a/sub-a1/sub-a1-deep", "Sub A1 Deep", eTag);
        eTag = await AddFileGetETag(metsUri, Pdf("objects/series-a/sub-a1/sub-a1-deep/deep-001.pdf"), eTag);
        eTag = await AddFileGetETag(metsUri, Pdf("objects/series-a/sub-a1/sub-a1-deep/deep-002.pdf"), eTag);
        eTag = await AddFileGetETag(metsUri, Pdf("objects/series-a/sub-a1/sub-a1-deep/deep-003.pdf"), eTag);

        eTag = await AddFolderGetETag(metsUri, "objects/series-a/sub-a1/sub-a1-deep/sub-a1-deeper", "Sub A1 Deeper", eTag);
        eTag = await AddFileGetETag(metsUri, Pdf("objects/series-a/sub-a1/sub-a1-deep/sub-a1-deeper/deepest-001.pdf"), eTag);

        eTag = await AddFolderGetETag(metsUri, "objects/series-a/sub-a2", "Sub A2", eTag);
        eTag = await AddFileGetETag(metsUri, Pdf("objects/series-a/sub-a2/doc-a2-001.pdf"), eTag);
        eTag = await AddFileGetETag(metsUri, Pdf("objects/series-a/sub-a2/doc-a2-002.pdf"), eTag);
        eTag = await AddFileGetETag(metsUri, Pdf("objects/series-a/sub-a2/doc-a2-003.pdf"), eTag);

        // series-b: one direct file plus nested subdirectories
        eTag = await AddFolderGetETag(metsUri, "objects/series-b", "Series B", eTag);
        eTag = await AddFileGetETag(metsUri, Pdf("objects/series-b/cover-b.pdf"), eTag);

        eTag = await AddFolderGetETag(metsUri, "objects/series-b/sub-b1", "Sub B1", eTag);
        eTag = await AddFileGetETag(metsUri, Tif("objects/series-b/sub-b1/page-001.tif"), eTag);
        eTag = await AddFileGetETag(metsUri, Tif("objects/series-b/sub-b1/page-002.tif"), eTag);
        eTag = await AddFileGetETag(metsUri, Tif("objects/series-b/sub-b1/page-003.tif"), eTag);
        eTag = await AddFileGetETag(metsUri, Tif("objects/series-b/sub-b1/page-004.tif"), eTag);
        eTag = await AddFileGetETag(metsUri, Tif("objects/series-b/sub-b1/page-005.tif"), eTag);

        eTag = await AddFolderGetETag(metsUri, "objects/series-b/sub-b2", "Sub B2", eTag);
        eTag = await AddFolderGetETag(metsUri, "objects/series-b/sub-b2/appendix", "Appendix", eTag);
        eTag = await AddFileGetETag(metsUri, Pdf("objects/series-b/sub-b2/appendix/appendix-001.pdf"), eTag);
        eTag = await AddFileGetETag(metsUri, Pdf("objects/series-b/sub-b2/appendix/appendix-002.pdf"), eTag);

        // series-c: many files, no subdirectories
        eTag = await AddFolderGetETag(metsUri, "objects/series-c", "Series C", eTag);
        eTag = await AddFileGetETag(metsUri, Tif("objects/series-c/scan-001.tif"), eTag);
        eTag = await AddFileGetETag(metsUri, Tif("objects/series-c/scan-002.tif"), eTag);
        eTag = await AddFileGetETag(metsUri, Tif("objects/series-c/scan-003.tif"), eTag);
        eTag = await AddFileGetETag(metsUri, Tif("objects/series-c/scan-004.tif"), eTag);
        eTag = await AddFileGetETag(metsUri, Tif("objects/series-c/scan-005.tif"), eTag);
        eTag = await AddFileGetETag(metsUri, Tif("objects/series-c/scan-006.tif"), eTag);
        eTag = await AddFileGetETag(metsUri, Tif("objects/series-c/scan-007.tif"), eTag);
        await AddFileGetETag(metsUri, Tif("objects/series-c/scan-008.tif"), eTag);

        await AssertDeepStructure(metsUri);
    }

    [Fact]
    public async Task Declarative_Approach_Builds_Deep_Structure()
    {
        // The entire tree is described as an ordered sequence of WorkingBase objects
        // (every parent directory precedes its children). A single GetFullMets + loop
        // of AddToMets calls + one WriteMets persists the whole structure atomically.

        var metsFi = new FileInfo("Outputs/deep-declarative.xml");
        var metsUri = new Uri(metsFi.FullName);
        var createResult = await metsManager.CreateStandardMets(metsUri, "Deep Structure (Declarative)");
        var fullMets = (await metsManager.GetFullMets(metsUri, createResult.Value!.ETag!)).Value!;

        // Declare the entire tree as an ordered sequence.
        // Directories must appear before any of their children.
        WorkingBase[] tree =
        [
            // series-a
            Dir("objects/series-a", "Series A"),
            Dir("objects/series-a/sub-a1", "Sub A1"),
            Pdf("objects/series-a/sub-a1/doc-a1-001.pdf"),
            Pdf("objects/series-a/sub-a1/doc-a1-002.pdf"),
            Dir("objects/series-a/sub-a1/sub-a1-deep", "Sub A1 Deep"),
            Pdf("objects/series-a/sub-a1/sub-a1-deep/deep-001.pdf"),
            Pdf("objects/series-a/sub-a1/sub-a1-deep/deep-002.pdf"),
            Pdf("objects/series-a/sub-a1/sub-a1-deep/deep-003.pdf"),
            Dir("objects/series-a/sub-a1/sub-a1-deep/sub-a1-deeper", "Sub A1 Deeper"),
            Pdf("objects/series-a/sub-a1/sub-a1-deep/sub-a1-deeper/deepest-001.pdf"),
            Dir("objects/series-a/sub-a2", "Sub A2"),
            Pdf("objects/series-a/sub-a2/doc-a2-001.pdf"),
            Pdf("objects/series-a/sub-a2/doc-a2-002.pdf"),
            Pdf("objects/series-a/sub-a2/doc-a2-003.pdf"),

            // series-b
            Dir("objects/series-b", "Series B"),
            Pdf("objects/series-b/cover-b.pdf"),
            Dir("objects/series-b/sub-b1", "Sub B1"),
            Tif("objects/series-b/sub-b1/page-001.tif"),
            Tif("objects/series-b/sub-b1/page-002.tif"),
            Tif("objects/series-b/sub-b1/page-003.tif"),
            Tif("objects/series-b/sub-b1/page-004.tif"),
            Tif("objects/series-b/sub-b1/page-005.tif"),
            Dir("objects/series-b/sub-b2", "Sub B2"),
            Dir("objects/series-b/sub-b2/appendix", "Appendix"),
            Pdf("objects/series-b/sub-b2/appendix/appendix-001.pdf"),
            Pdf("objects/series-b/sub-b2/appendix/appendix-002.pdf"),

            // series-c
            Dir("objects/series-c", "Series C"),
            Tif("objects/series-c/scan-001.tif"),
            Tif("objects/series-c/scan-002.tif"),
            Tif("objects/series-c/scan-003.tif"),
            Tif("objects/series-c/scan-004.tif"),
            Tif("objects/series-c/scan-005.tif"),
            Tif("objects/series-c/scan-006.tif"),
            Tif("objects/series-c/scan-007.tif"),
            Tif("objects/series-c/scan-008.tif"),
        ];

        foreach (var item in tree)
        {
            metsManager.AddToMets(fullMets, item).Success.Should().BeTrue();
        }

        await metsManager.WriteMets(fullMets);

        await AssertDeepStructure(metsUri);
    }

    [Fact]
    public async Task Combined_Approach_Builds_Deep_Structure()
    {
        // series-a is built using the incremental Handle* approach (write after every
        // single operation). Then GetFullMets loads the current on-disk state into a
        // FullMets, and series-b and series-c are appended with AddToMets calls before
        // a single WriteMets persists the remainder. This tests that the two approaches
        // can be freely interleaved on the same METS file.

        var metsFi = new FileInfo("Outputs/deep-combined.xml");
        var metsUri = new Uri(metsFi.FullName);
        var eTag = await CreateAndGetETag(metsUri, "Deep Structure (Combined)");

        // ---- Incremental phase: build series-a ----

        eTag = await AddFolderGetETag(metsUri, "objects/series-a", "Series A", eTag);

        eTag = await AddFolderGetETag(metsUri, "objects/series-a/sub-a1", "Sub A1", eTag);
        eTag = await AddFileGetETag(metsUri, Pdf("objects/series-a/sub-a1/doc-a1-001.pdf"), eTag);
        eTag = await AddFileGetETag(metsUri, Pdf("objects/series-a/sub-a1/doc-a1-002.pdf"), eTag);

        eTag = await AddFolderGetETag(metsUri, "objects/series-a/sub-a1/sub-a1-deep", "Sub A1 Deep", eTag);
        eTag = await AddFileGetETag(metsUri, Pdf("objects/series-a/sub-a1/sub-a1-deep/deep-001.pdf"), eTag);
        eTag = await AddFileGetETag(metsUri, Pdf("objects/series-a/sub-a1/sub-a1-deep/deep-002.pdf"), eTag);
        eTag = await AddFileGetETag(metsUri, Pdf("objects/series-a/sub-a1/sub-a1-deep/deep-003.pdf"), eTag);

        eTag = await AddFolderGetETag(metsUri, "objects/series-a/sub-a1/sub-a1-deep/sub-a1-deeper", "Sub A1 Deeper", eTag);
        eTag = await AddFileGetETag(metsUri, Pdf("objects/series-a/sub-a1/sub-a1-deep/sub-a1-deeper/deepest-001.pdf"), eTag);

        eTag = await AddFolderGetETag(metsUri, "objects/series-a/sub-a2", "Sub A2", eTag);
        eTag = await AddFileGetETag(metsUri, Pdf("objects/series-a/sub-a2/doc-a2-001.pdf"), eTag);
        eTag = await AddFileGetETag(metsUri, Pdf("objects/series-a/sub-a2/doc-a2-002.pdf"), eTag);
        eTag = await AddFileGetETag(metsUri, Pdf("objects/series-a/sub-a2/doc-a2-003.pdf"), eTag);

        // ---- Switch to declarative phase: append series-b and series-c ----
        // Re-read the current on-disk state into a fresh FullMets so that AddToMets
        // sees all the divs already written during the incremental phase above.

        var freshParse = await parser.GetMetsFileWrapper(metsUri);
        var fullMets = (await metsManager.GetFullMets(metsUri, freshParse.Value!.ETag!)).Value!;

        WorkingBase[] remainder =
        [
            Dir("objects/series-b", "Series B"),
            Pdf("objects/series-b/cover-b.pdf"),
            Dir("objects/series-b/sub-b1", "Sub B1"),
            Tif("objects/series-b/sub-b1/page-001.tif"),
            Tif("objects/series-b/sub-b1/page-002.tif"),
            Tif("objects/series-b/sub-b1/page-003.tif"),
            Tif("objects/series-b/sub-b1/page-004.tif"),
            Tif("objects/series-b/sub-b1/page-005.tif"),
            Dir("objects/series-b/sub-b2", "Sub B2"),
            Dir("objects/series-b/sub-b2/appendix", "Appendix"),
            Pdf("objects/series-b/sub-b2/appendix/appendix-001.pdf"),
            Pdf("objects/series-b/sub-b2/appendix/appendix-002.pdf"),

            Dir("objects/series-c", "Series C"),
            Tif("objects/series-c/scan-001.tif"),
            Tif("objects/series-c/scan-002.tif"),
            Tif("objects/series-c/scan-003.tif"),
            Tif("objects/series-c/scan-004.tif"),
            Tif("objects/series-c/scan-005.tif"),
            Tif("objects/series-c/scan-006.tif"),
            Tif("objects/series-c/scan-007.tif"),
            Tif("objects/series-c/scan-008.tif"),
        ];

        foreach (var item in remainder)
        {
            metsManager.AddToMets(fullMets, item).Success.Should().BeTrue();
        }

        await metsManager.WriteMets(fullMets);

        await AssertDeepStructure(metsUri);
    }

    // -----------------------------------------------------------------------
    // Shared assertions
    // -----------------------------------------------------------------------

    /// <summary>
    /// Asserts the expected deep structure at two levels:
    ///   1. MetsParser round-trip — WorkingDirectory/WorkingFile properties.
    ///   2. Raw XML — a selection of ID attributes are present and consistent.
    /// </summary>
    private async Task AssertDeepStructure(Uri metsUri)
    {
        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();
        var wrapper = parseResult.Value!;

        AssertParserRoundTrip(wrapper);
        AssertRawXml(metsUri);
    }

    private static void AssertParserRoundTrip(MetsFileWrapper wrapper)
    {
        var root = wrapper.PhysicalStructure!;

        // Top-level objects directory: 3 series, no direct files
        var objectsDir = root.Directories.Single(d => d.Name == FolderNames.Objects);
        objectsDir.Directories.Should().HaveCount(3);
        objectsDir.Files.Should().BeEmpty();

        var seriesA = objectsDir.Directories.Single(d => d.Name == "Series A");
        var seriesB = objectsDir.Directories.Single(d => d.Name == "Series B");
        var seriesC = objectsDir.Directories.Single(d => d.Name == "Series C");

        // ---- series-a ----
        // No direct files; two subdirectories (Sub A1 sorts before Sub A2)
        seriesA.LocalPath.Should().Be("objects/series-a");
        seriesA.Files.Should().BeEmpty();
        seriesA.Directories.Should().HaveCount(2);

        var subA1 = seriesA.Directories.Single(d => d.Name == "Sub A1");
        var subA2 = seriesA.Directories.Single(d => d.Name == "Sub A2");

        subA1.LocalPath.Should().Be("objects/series-a/sub-a1");
        subA1.Files.Should().HaveCount(2);
        subA1.Files[0].LocalPath.Should().Be("objects/series-a/sub-a1/doc-a1-001.pdf");
        subA1.Files[0].Name.Should().Be("doc-a1-001.pdf");
        subA1.Files[0].ContentType.Should().Be("application/pdf");
        subA1.Files[1].LocalPath.Should().Be("objects/series-a/sub-a1/doc-a1-002.pdf");
        subA1.Directories.Should().HaveCount(1);

        var subA1Deep = subA1.Directories.Single(d => d.Name == "Sub A1 Deep");
        subA1Deep.LocalPath.Should().Be("objects/series-a/sub-a1/sub-a1-deep");
        subA1Deep.Files.Should().HaveCount(3);
        subA1Deep.Files[0].LocalPath.Should().Be("objects/series-a/sub-a1/sub-a1-deep/deep-001.pdf");
        subA1Deep.Files[1].LocalPath.Should().Be("objects/series-a/sub-a1/sub-a1-deep/deep-002.pdf");
        subA1Deep.Files[2].LocalPath.Should().Be("objects/series-a/sub-a1/sub-a1-deep/deep-003.pdf");
        subA1Deep.Directories.Should().HaveCount(1);

        // 4 levels deep
        var subA1Deeper = subA1Deep.Directories.Single(d => d.Name == "Sub A1 Deeper");
        subA1Deeper.LocalPath.Should().Be("objects/series-a/sub-a1/sub-a1-deep/sub-a1-deeper");
        subA1Deeper.Files.Should().HaveCount(1);
        subA1Deeper.Files[0].LocalPath.Should().Be("objects/series-a/sub-a1/sub-a1-deep/sub-a1-deeper/deepest-001.pdf");
        subA1Deeper.Directories.Should().BeEmpty();

        subA2.LocalPath.Should().Be("objects/series-a/sub-a2");
        subA2.Files.Should().HaveCount(3);
        subA2.Files.Select(f => f.LocalPath).Should().BeEquivalentTo(
            "objects/series-a/sub-a2/doc-a2-001.pdf",
            "objects/series-a/sub-a2/doc-a2-002.pdf",
            "objects/series-a/sub-a2/doc-a2-003.pdf");
        subA2.Directories.Should().BeEmpty();

        // ---- series-b ----
        // 1 direct file (cover-b.pdf) + 2 subdirs (Sub B1, Sub B2)
        seriesB.LocalPath.Should().Be("objects/series-b");
        seriesB.Files.Should().HaveCount(1);
        seriesB.Files[0].LocalPath.Should().Be("objects/series-b/cover-b.pdf");
        seriesB.Files[0].ContentType.Should().Be("application/pdf");
        seriesB.Directories.Should().HaveCount(2);

        var subB1 = seriesB.Directories.Single(d => d.Name == "Sub B1");
        var subB2 = seriesB.Directories.Single(d => d.Name == "Sub B2");

        subB1.LocalPath.Should().Be("objects/series-b/sub-b1");
        subB1.Files.Should().HaveCount(5);
        subB1.Files.Select(f => f.LocalPath).Should().BeEquivalentTo(
            "objects/series-b/sub-b1/page-001.tif",
            "objects/series-b/sub-b1/page-002.tif",
            "objects/series-b/sub-b1/page-003.tif",
            "objects/series-b/sub-b1/page-004.tif",
            "objects/series-b/sub-b1/page-005.tif");
        subB1.Files.Should().AllSatisfy(f => f.ContentType.Should().Be("image/tiff"));
        subB1.Directories.Should().BeEmpty();

        subB2.LocalPath.Should().Be("objects/series-b/sub-b2");
        subB2.Files.Should().BeEmpty();
        subB2.Directories.Should().HaveCount(1);

        var appendix = subB2.Directories.Single(d => d.Name == "Appendix");
        appendix.LocalPath.Should().Be("objects/series-b/sub-b2/appendix");
        appendix.Files.Should().HaveCount(2);
        appendix.Files[0].LocalPath.Should().Be("objects/series-b/sub-b2/appendix/appendix-001.pdf");
        appendix.Files[1].LocalPath.Should().Be("objects/series-b/sub-b2/appendix/appendix-002.pdf");
        appendix.Directories.Should().BeEmpty();

        // ---- series-c ----
        // 8 tif files, no subdirectories
        seriesC.LocalPath.Should().Be("objects/series-c");
        seriesC.Directories.Should().BeEmpty();
        seriesC.Files.Should().HaveCount(8);
        seriesC.Files.Select(f => f.LocalPath).Should().BeEquivalentTo(
            "objects/series-c/scan-001.tif",
            "objects/series-c/scan-002.tif",
            "objects/series-c/scan-003.tif",
            "objects/series-c/scan-004.tif",
            "objects/series-c/scan-005.tif",
            "objects/series-c/scan-006.tif",
            "objects/series-c/scan-007.tif",
            "objects/series-c/scan-008.tif");
        seriesC.Files.Should().AllSatisfy(f => f.ContentType.Should().Be("image/tiff"));
    }

    private static void AssertRawXml(Uri metsUri)
    {
        var doc = XDocument.Load(metsUri.LocalPath);

        // --- Directory Divs and their AmdSecs ---

        // Level 1: series-a (directory-only)
        AssertDirectoryDiv(doc, "objects/series-a", "Series A");

        // Level 2: sub-a1 and sub-a2
        AssertDirectoryDiv(doc, "objects/series-a/sub-a1", "Sub A1");
        AssertDirectoryDiv(doc, "objects/series-a/sub-a2", "Sub A2");

        // Level 3: sub-a1-deep and appendix
        AssertDirectoryDiv(doc, "objects/series-a/sub-a1/sub-a1-deep", "Sub A1 Deep");
        AssertDirectoryDiv(doc, "objects/series-b/sub-b2/appendix", "Appendix");

        // Level 4: sub-a1-deeper
        AssertDirectoryDiv(doc, "objects/series-a/sub-a1/sub-a1-deep/sub-a1-deeper", "Sub A1 Deeper");

        // Level 1: series-b (has direct file + subdirs)
        AssertDirectoryDiv(doc, "objects/series-b", "Series B");

        // Level 1: series-c (many files, no subdirs)
        AssertDirectoryDiv(doc, "objects/series-c", "Series C");

        // --- File elements: ID, ADMID, FLocat HREF and AmdSec consistency ---

        // Deepest file (4 levels)
        AssertFileElement(doc, "objects/series-a/sub-a1/sub-a1-deep/sub-a1-deeper/deepest-001.pdf");

        // File directly under a level-1 directory
        AssertFileElement(doc, "objects/series-b/cover-b.pdf");

        // File inside a level-3 leaf directory
        AssertFileElement(doc, "objects/series-b/sub-b2/appendix/appendix-002.pdf");

        // First and last files in the large series-c flat set
        AssertFileElement(doc, "objects/series-c/scan-001.tif");
        AssertFileElement(doc, "objects/series-c/scan-008.tif");

        // --- Total element counts as a sanity check ---

        // 9 user-added directories (3 series + 6 children) plus the built-in
        // objects and metadata divs = 11 Directory divs total, plus one per file.
        var allDivs = doc.Descendants(MetsNs + "div").ToList();
        // 25 files + 13 directories (PHYS_ROOT + metadata + objects + 10 user-added) = 38 divs
        allDivs.Should().HaveCount(38);

        // 25 files in the fileSec
        var fileEls = doc.Descendants(MetsNs + "file").ToList();
        fileEls.Should().HaveCount(25);

        // One amdSec per file (25) + one per directory that has an amdSec:
        //   built-in (objects, metadata) + 10 user-added (series-a, sub-a1, sub-a1-deep,
        //   sub-a1-deeper, sub-a2, series-b, sub-b1, sub-b2, appendix, series-c) = 12
        // PHYS_ROOT has no amdSec.  Total: 25 + 12 = 37.
        var amdSecs = doc.Descendants(MetsNs + "amdSec").ToList();
        amdSecs.Should().HaveCount(37);
    }

    // Verifies that a Directory div exists with correct TYPE, LABEL and ADMID,
    // and that a matching amdSec element is present.
    private static void AssertDirectoryDiv(XDocument doc, string localPath, string expectedLabel)
    {
        var physId = $"PHYS_{localPath}";
        var admId  = $"ADM_{localPath}";

        var divEl = doc.Descendants(MetsNs + "div")
            .FirstOrDefault(d => (string?)d.Attribute("ID") == physId);
        divEl.Should().NotBeNull($"expected Directory div with ID='{physId}'");
        divEl!.Attribute("TYPE")!.Value.Should().Be("Directory");
        divEl.Attribute("LABEL")!.Value.Should().Be(expectedLabel);
        divEl.Attribute("ADMID")!.Value.Should().Be(admId);

        doc.Descendants(MetsNs + "amdSec")
            .Should().Contain(a => (string?)a.Attribute("ID") == admId,
                $"expected amdSec with ID='{admId}'");
    }

    // Verifies that a FileType element, its FLocat HREF, its ADMID, the matching
    // amdSec, and the matching structMap Div (with its fptr) are all consistent.
    private static void AssertFileElement(XDocument doc, string localPath)
    {
        var fileId = $"FILE_{localPath}";
        var admId  = $"ADM_{localPath}";
        var physId = $"PHYS_{localPath}";

        var fileEl = doc.Descendants(MetsNs + "file")
            .FirstOrDefault(f => (string?)f.Attribute("ID") == fileId);
        fileEl.Should().NotBeNull($"expected file element with ID='{fileId}'");
        fileEl!.Attribute("ADMID")!.Value.Should().Be(admId);
        fileEl.Elements(MetsNs + "FLocat").Single()
            .Attribute(XLinkNs + "href")!.Value.Should().Be(localPath);

        doc.Descendants(MetsNs + "amdSec")
            .Should().Contain(a => (string?)a.Attribute("ID") == admId,
                $"expected amdSec with ID='{admId}'");

        var divEl = doc.Descendants(MetsNs + "div")
            .FirstOrDefault(d => (string?)d.Attribute("ID") == physId);
        divEl.Should().NotBeNull($"expected structMap div with ID='{physId}'");
        divEl!.Attribute("TYPE")!.Value.Should().Be("Item");
        divEl.Elements(MetsNs + "fptr").Single()
            .Attribute("FILEID")!.Value.Should().Be(fileId);
    }
}
