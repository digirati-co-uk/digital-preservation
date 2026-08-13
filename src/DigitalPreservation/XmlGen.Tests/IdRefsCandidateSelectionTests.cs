using System.Xml.Linq;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;
using DigitalPreservation.XmlGen.Mets;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XmlGen.Tests;

/// <summary>
/// Issue #215. ADMID is IDREFS — a LIST whose order carries no meaning — so a file or directory
/// may legitimately name a shared <c>rightsMD</c> before the section that actually describes it.
/// Archivematica does exactly that. Resolution therefore has to accept the first candidate that
/// is USABLE by the caller, not the first that merely exists; taking the rights section instead
/// loses a file's checksum, crashes an update, or leaves a directory with no path at all.
///
/// Each test here puts the unusable section FIRST in the ADMID, which is the whole point — with
/// the useful section first they all pass regardless.
/// </summary>
public class IdRefsCandidateSelectionTests
{
    private readonly MetsManager metsManager;
    private readonly MetsParser parser;
    private readonly FileSystemMetsStorage metsStorage;

    private const string TestDigest = "eb634d64ce8e6be5195174ceaef9ac9e19c37119f3b31618630aa633ccdbf68f";

    public IdRefsCandidateSelectionTests()
    {
        var sp = new ServiceCollection().AddLogging().BuildServiceProvider();
        var parserLogger = sp.GetService<ILoggerFactory>()!.CreateLogger<MetsParser>();
        parser = new MetsParser(new FileSystemMetsLoader(), parserLogger);
        metsStorage = new FileSystemMetsStorage(parser);
        var metadataManager = new MetadataManager(new PremisManager(), new PremisManagerExif(), new PremisEventManagerVirus());
        metsManager = new MetsManager(parser, metsStorage, metadataManager);
    }

    private async Task<(Uri uri, FullMets fullMets)> CreateWithOneFile(string outputName, string localPath)
    {
        var uri = new Uri(new FileInfo($"Outputs/{outputName}").FullName);
        var create = await metsManager.CreateStandardMets(uri, "Candidate Selection");
        create.Success.Should().BeTrue(create.ErrorMessage ?? "");

        var fullMets = (await metsManager.GetFullMets(uri, create.Value!.ETag)).Value!;
        var add = metsManager.AddToMets(fullMets, new WorkingFile
        {
            LocalPath = localPath,
            Name = localPath.Split('/')[^1],
            ContentType = "application/pdf",
            Digest = TestDigest,
            Size = 54321,
            Modified = DateTime.UtcNow
        });
        add.Success.Should().BeTrue(add.ErrorMessage ?? "");
        return (uri, fullMets);
    }

    /// <summary>A rights-only amdSec: no techMD, so nothing a metadata reader can use.</summary>
    private static AmdSecType RightsOnlyAmdSec(string id) =>
        new() { Id = id, RightsMd = { new MdSecType { Id = id + "_RIGHTS" } } };

    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_File_Whose_Admid_Names_A_Rights_Section_First_Keeps_Its_Checksum()
    {
        // The parser used to stop on the rights section, find no fixity, and hand the binary
        // on with Digest = null - which GetDiffImportJob requires and would reject.
        const string path = "objects/report.pdf";
        var (uri, fullMets) = await CreateWithOneFile("candidate-parser-digest.xml", path);

        var file = fullMets.Mets.FileSec.FileGrp.SelectMany(fg => fg.File).Single(f => f.Admid.Count > 0);
        fullMets.Mets.AmdSec.Add(RightsOnlyAmdSec("ADM_rights_shared"));
        file.Admid.Insert(0, "ADM_rights_shared");
        (await metsManager.WriteMets(fullMets)).Success.Should().BeTrue();

        var parsed = await parser.GetMetsFileWrapper(uri);
        parsed.Success.Should().BeTrue(parsed.ErrorMessage ?? "");
        var workingFile = parsed.Value!.Files.Single(f => f.LocalPath == path);
        workingFile.Digest.Should().Be(TestDigest, "the techMD holding the fixity is later in the ADMID");
        workingFile.Size.Should().Be(54321);
    }

    [Fact]
    public async Task Updating_A_File_Whose_Admid_Names_A_Rights_Section_First_Succeeds()
    {
        // This used to throw ArgumentOutOfRangeException out of SetAmdSec (TechMd[0] on a
        // section that has none) - an unhandled 500 where every other failure here is a
        // Result.Fail.
        const string path = "objects/report.pdf";
        var (uri, fullMets) = await CreateWithOneFile("candidate-update-crash.xml", path);

        var file = fullMets.Mets.FileSec.FileGrp.SelectMany(fg => fg.File).Single(f => f.Admid.Count > 0);
        var realAdmId = file.Admid[0];
        fullMets.Mets.AmdSec.Add(RightsOnlyAmdSec("ADM_rights_shared"));
        file.Admid.Insert(0, "ADM_rights_shared");

        var update = metsManager.AddToMets(fullMets, new WorkingFile
        {
            LocalPath = path, Name = "report.pdf", ContentType = "application/pdf",
            Digest = TestDigest, Size = 99999, Modified = DateTime.UtcNow
        });

        update.Success.Should().BeTrue(update.ErrorMessage ?? "");
        // The PREMIS was written into the file's OWN amdSec, not the shared rights section.
        var rights = fullMets.Mets.AmdSec.Single(a => a.Id == "ADM_rights_shared");
        rights.TechMd.Should().BeEmpty("a shared rights section must not acquire this file's PREMIS");
        fullMets.Mets.AmdSec.Single(a => a.Id == realAdmId).TechMd.Should().ContainSingle();
        _ = uri;
    }

    [Fact]
    public async Task A_Directory_Whose_Admid_Names_A_Rights_Section_First_Still_Has_A_Path()
    {
        // Resolution used to stop on the rights section, find no premis:originalName, and
        // report the div as pathless - which takes every edit beneath it down too.
        var uri = new Uri(new FileInfo("Outputs/candidate-directory-path.xml").FullName);
        var create = await metsManager.CreateStandardMets(uri, "Candidate Selection Dir");
        create.Success.Should().BeTrue(create.ErrorMessage ?? "");
        var fullMets = (await metsManager.GetFullMets(uri, create.Value!.ETag)).Value!;

        var addDir = metsManager.AddToMets(fullMets, new WorkingDirectory
        {
            LocalPath = "objects/sub", Name = "sub", Modified = DateTime.UtcNow
        });
        addDir.Success.Should().BeTrue(addDir.ErrorMessage ?? "");

        var subDiv = fullMets.PhysicalDivsByPath["objects/sub"];
        fullMets.Mets.AmdSec.Add(RightsOnlyAmdSec("ADM_rights_shared"));
        subDiv.Admid.Insert(0, "ADM_rights_shared");

        MetsCache.Populate(fullMets).Should().BeEmpty("the directory still resolves via its own amdSec");
        fullMets.PhysicalDivsByPath.Should().ContainKey("objects/sub");

        // ...and the path below it is still editable.
        var addFile = metsManager.AddToMets(fullMets, new WorkingFile
        {
            LocalPath = "objects/sub/page.tif", Name = "page.tif", ContentType = "image/tiff",
            Digest = TestDigest, Size = 10, Modified = DateTime.UtcNow
        });
        addFile.Success.Should().BeTrue(addFile.ErrorMessage ?? "");
    }

    [Fact]
    public async Task A_File_Whose_Admid_Resolves_Nothing_Usable_Fails_Cleanly()
    {
        // The other half: when NO candidate carries technical metadata the operation must
        // report a BadRequest, not resolve something unusable and carry on.
        const string path = "objects/report.pdf";
        var (_, fullMets) = await CreateWithOneFile("candidate-none-usable.xml", path);

        var file = fullMets.Mets.FileSec.FileGrp.SelectMany(fg => fg.File).Single(f => f.Admid.Count > 0);
        var realAdmId = file.Admid[0];
        fullMets.Mets.AmdSec.Remove(fullMets.Mets.AmdSec.Single(a => a.Id == realAdmId));
        fullMets.Mets.AmdSec.Add(RightsOnlyAmdSec(realAdmId));

        var update = metsManager.AddToMets(fullMets, new WorkingFile
        {
            LocalPath = path, Name = "report.pdf", ContentType = "application/pdf",
            Digest = TestDigest, Size = 1, Modified = DateTime.UtcNow
        });

        update.Failure.Should().BeTrue();
        update.ErrorCode.Should().Be(DigitalPreservation.Common.Model.ErrorCodes.BadRequest);
        update.ErrorMessage.Should().Contain("technical metadata");
    }

    [Fact]
    public async Task A_Genuine_Multi_Admid_Whose_First_Section_Is_Usable_Is_Unaffected()
    {
        // Guards against over-correcting: when the first candidate IS usable it must still win.
        const string path = "objects/report.pdf";
        var (uri, fullMets) = await CreateWithOneFile("candidate-first-usable.xml", path);

        var file = fullMets.Mets.FileSec.FileGrp.SelectMany(fg => fg.File).Single(f => f.Admid.Count > 0);
        fullMets.Mets.AmdSec.Add(RightsOnlyAmdSec("ADM_rights_shared"));
        file.Admid.Add("ADM_rights_shared");
        (await metsManager.WriteMets(fullMets)).Success.Should().BeTrue();

        var parsed = await parser.GetMetsFileWrapper(uri);
        parsed.Value!.Files.Single(f => f.LocalPath == path).Digest.Should().Be(TestDigest);
        XDocument.Load(uri.LocalPath).Descendants().Should().NotBeEmpty();
    }
}
