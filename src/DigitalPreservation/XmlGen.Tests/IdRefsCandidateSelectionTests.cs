using System.Xml.Linq;
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

    /// <summary>An amdSec carrying a PREMIS object — a genuinely usable candidate.</summary>
    private static AmdSecType PremisBearingAmdSec(string id) =>
        new()
        {
            Id = id,
            TechMd =
            {
                new MdSecType
                {
                    Id = id + "_TECH",
                    MdWrap = new MdSecTypeMdWrap
                    {
                        Mdtype = MdSecTypeMdWrapMdtype.PremisObject,
                        XmlData = new MdSecTypeMdWrapXmlData
                        {
                            Any = { new System.Xml.XmlDocument().CreateElement("object", "http://www.loc.gov/premis/v3") }
                        }
                    }
                }
            }
        };

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
    public async Task A_Section_With_A_Non_PREMIS_TechMd_Is_Not_Treated_As_Usable()
    {
        // "Has a techMD" is not the same as "has the PREMIS we are about to read and patch".
        // Archivematica-shaped input can name a section whose techMD holds only FITS or EXIF
        // output; stopping there hands non-PREMIS XML to GetPremisComplexType().
        const string path = "objects/report.pdf";
        var (_, fullMets) = await CreateWithOneFile("candidate-non-premis-techmd.xml", path);

        var file = fullMets.Mets.FileSec.FileGrp.SelectMany(fg => fg.File).Single(f => f.Admid.Count > 0);
        var fitsOnly = new AmdSecType
        {
            Id = "ADM_fits_only",
            TechMd =
            {
                new MdSecType
                {
                    Id = "TECH_fits_only",
                    MdWrap = new MdSecTypeMdWrap
                    {
                        Mdtype = MdSecTypeMdWrapMdtype.Other,
                        XmlData = new MdSecTypeMdWrapXmlData
                        {
                            Any = { new System.Xml.XmlDocument().CreateElement("fits", "http://hul.harvard.edu/ois/xml/ns/fits/fits_output") }
                        }
                    }
                }
            }
        };
        fullMets.Mets.AmdSec.Add(fitsOnly);
        file.Admid.Insert(0, "ADM_fits_only");

        var update = metsManager.AddToMets(fullMets, new WorkingFile
        {
            LocalPath = path, Name = "report.pdf", ContentType = "application/pdf",
            Digest = TestDigest, Size = 4242, Modified = DateTime.UtcNow
        });

        update.Success.Should().BeTrue(update.ErrorMessage ?? "");
        fitsOnly.TechMd.Should().ContainSingle()
            .Which.MdWrap.XmlData!.Any![0].LocalName.Should().Be("fits",
                "the FITS section must not be overwritten with this file's PREMIS");
    }

    [Fact]
    public async Task A_Legacy_Reference_To_An_Unusable_Section_Fails_Loudly_Rather_Than_Guessing()
    {
        // A legacy space-containing ADMID is an IDENTITY match once rejoined, so if the section
        // it names is unusable the answer is "this document is broken", not "try each half of
        // the ID against whatever else is lying around" - which can resolve a fragment against
        // an unrelated section and answer with confident nonsense.
        const string path = "objects/report.pdf";
        var (_, fullMets) = await CreateWithOneFile("candidate-legacy-unusable.xml", path);

        var file = fullMets.Mets.FileSec.FileGrp.SelectMany(fg => fg.File).Single(f => f.Admid.Count > 0);
        // The file names ONE legacy section, split into two tokens by the serialiser...
        fullMets.Mets.AmdSec.Remove(fullMets.Mets.AmdSec.Single(a => a.Id == file.Admid[0]));
        fullMets.Mets.AmdSec.Add(RightsOnlyAmdSec("ADM_objects/my file.pdf"));
        file.Admid.Clear();
        file.Admid.Add("ADM_objects/my");
        file.Admid.Add("file.pdf");
        // ...and a decoy that one of those fragments matches, carrying PREMIS so it is a
        // perfectly USABLE candidate. Walking on would resolve to it and write this file's
        // technical metadata into an unrelated section.
        fullMets.Mets.AmdSec.Add(PremisBearingAmdSec("file.pdf"));

        var update = metsManager.AddToMets(fullMets, new WorkingFile
        {
            LocalPath = path, Name = "report.pdf", ContentType = "application/pdf",
            Digest = TestDigest, Size = 1, Modified = DateTime.UtcNow
        });

        update.Failure.Should().BeTrue("the section the file names has no PREMIS");
        update.ErrorMessage.Should().Contain("PREMIS");
    }

    [Fact]
    public async Task The_Parser_Does_Not_Treat_An_Unusable_Identity_Match_As_The_File_S_Metadata()
    {
        // The other two call sites recheck what the identity tier hands back - MetadataManager
        // explicitly, MetsCache implicitly (its path comes back null). The parser must too.
        // A SINGLE-token ADMID naming a rights-only section is an identity match, so it is
        // returned unusable by design; treating it as resolved scopes the virus-scan lookup to
        // that section, and a shared rights section carries someone else's scan.
        const string path = "objects/report.pdf";
        var (uri, fullMets) = await CreateWithOneFile("candidate-parser-identity-recheck.xml", path);

        var file = fullMets.Mets.FileSec.FileGrp.SelectMany(fg => fg.File).Single(f => f.Admid.Count > 0);
        var shared = RightsOnlyAmdSec("ADM_shared_rights");
        // Another FILE's scan, parked in the shared section - note the ID names that other
        // file, not this one, so it is discoverable only by scoping the search to this section.
        shared.DigiprovMd.Add(new MdSecType
        {
            Id = Constants.VirusProvEventPrefix + MetsIds.Adm("objects/some-other-file.pdf"),
            MdWrap = new MdSecTypeMdWrap
            {
                Mdtype = MdSecTypeMdWrapMdtype.PremisEvent,
                XmlData = new MdSecTypeMdWrapXmlData
                {
                    Any = { PremisEventManagerVirus.GetXmlElement(
                        new PremisEventManagerVirus().Create(new VirusScanMetadata
                        {
                            Source = "ClamAV", HasVirus = true,
                            VirusFound = "Someone-Elses-Virus", VirusDefinition = "defs-x"
                        })) }
                }
            }
        });
        fullMets.Mets.AmdSec.Add(shared);
        // The file names ONLY that section - a single token, so the identity tier returns it.
        file.Admid.Clear();
        file.Admid.Add("ADM_shared_rights");
        (await metsManager.WriteMets(fullMets)).Success.Should().BeTrue();

        var parsed = await parser.GetMetsFileWrapper(uri);
        parsed.Success.Should().BeTrue(parsed.ErrorMessage ?? "");
        var workingFile = parsed.Value!.Files.Single(f => f.LocalPath == path);

        workingFile.Metadata.OfType<VirusScanMetadata>().Should().BeEmpty(
            "a shared rights section's scan is not this file's");
        workingFile.Digest.Should().BeNull("the section it names carries no PREMIS");
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
