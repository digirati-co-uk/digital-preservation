using System.Xml;
using System.Xml.Linq;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace XmlGen.Tests;

/// <summary>
/// Issue #188 step 3: migrating a document minted before #214 so that every xs:ID in it is a legal
/// NCName, and every reference still names what it named before.
///
/// The property under test throughout is that the migration is a rename and nothing else. The paths
/// are not touched, the content is not touched, the navigation still works, and an ID that was
/// already legal - anyone's, including a client-supplied logical range ID that is public through
/// the IIIF Range URI built from it - comes out the other side identical.
/// </summary>
public class MetsIdNormalisationTests
{
    private readonly MetsManager metsManager;

    /// <summary>
    /// The same manager with <c>NormaliseMetsIdsOnWrite</c> set, which is how the platform is
    /// configured once the flag is on: every write migrates the document it is writing.
    /// </summary>
    private readonly MetsManager migratingOnWrite;

    private readonly MetsParser parser;

    private static readonly XNamespace MetsNs = "http://www.loc.gov/METS/";
    private static readonly XNamespace XLinkNs = "http://www.w3.org/1999/xlink";

    private const string TestDigest = "eb634d64ce8e6be5195174ceaef9ac9e19c37119f3b31618630aa633ccdbf68f";

    public MetsIdNormalisationTests()
    {
        var serviceProvider = new ServiceCollection().AddLogging().BuildServiceProvider();
        var factory = serviceProvider.GetService<ILoggerFactory>();
        var metsLoader = new FileSystemMetsLoader();
        parser = new MetsParser(metsLoader, factory!.CreateLogger<MetsParser>());
        var metsStorage = new FileSystemMetsStorage(parser);
        var metadataManager = new MetadataManager(
            new PremisManager(), new PremisManagerExif(), new PremisEventManagerVirus());
        metsManager = new MetsManager(parser, metsStorage, metadataManager);
        migratingOnWrite = new MetsManager(parser, metsStorage, metadataManager,
            Options.Create(new MetsManagerOptions { NormaliseMetsIdsOnWrite = true }));
    }

    // -----------------------------------------------------------------------
    // How one ID is respelt
    // -----------------------------------------------------------------------

    [Theory]
    // The stem after a known prefix is encoded, so the result is what the minting methods produce.
    [InlineData("PHYS_objects/my file.pdf", "PHYS_objects_x002F_my_x0020_file.pdf")]
    [InlineData("FILE_objects/my file.pdf", "FILE_objects_x002F_my_x0020_file.pdf")]
    [InlineData("ADM_objects/my file.pdf", "ADM_objects_x002F_my_x0020_file.pdf")]
    [InlineData("TECH_objects/my file.pdf", "TECH_objects_x002F_my_x0020_file.pdf")]
    [InlineData("DMD_objects/my file.pdf", "DMD_objects_x002F_my_x0020_file.pdf")]
    // A virus event ID is a prefix in front of another ID, not in front of a path, so the
    // remainder is respelt in its own right rather than encoded as one blob.
    [InlineData("digiprovMD_ClamAV_ADM_objects/a.tif", "digiprovMD_ClamAV_ADM_objects_x002F_a.tif")]
    [InlineData("digiprovMD_ClamAV_2_ADM_objects/a.tif", "digiprovMD_ClamAV_2_ADM_objects_x002F_a.tif")]
    // Nothing we recognise: still made legal, just not split at a prefix.
    [InlineData("someone elses id", "someone_x0020_elses_x0020_id")]
    public void An_Invalid_Id_Is_Respelt_The_Way_The_Platform_Spells_Ids_Now(string legacy, string expected)
    {
        MetsIds.Normalise(legacy).Should().Be(expected);
    }

    [Theory]
    [InlineData("PHYS_ROOT")]
    [InlineData("DMD_PHYS_ROOT")]
    [InlineData("PHYS_objects")]
    [InlineData("LOG_0001")]                                       // a client-supplied range ID
    [InlineData("FILE_objects_x002F_my_x0020_file.pdf")]           // already migrated
    [InlineData("digiprovMD_ingest")]                              // somebody else's
    public void A_Valid_Id_Is_Left_Exactly_As_It_Is(string id)
    {
        MetsIds.Normalise(id).Should().Be(id);
    }

    [Fact]
    public void Respelling_An_Id_Agrees_With_Minting_One()
    {
        // The two must not drift: after a migration, a document's IDs have to be indistinguishable
        // from those of a document written from scratch today.
        foreach (var path in new[]
                 { "objects/my file.pdf", "objects/A&B/AT&T guide.pdf", "objects/2020 files/9 lives.pdf" })
        {
            MetsIds.Normalise(Constants.PhysIdPrefix + path).Should().Be(MetsIds.Phys(path));
            MetsIds.Normalise(Constants.FileIdPrefix + path).Should().Be(MetsIds.File(path));
            MetsIds.Normalise(Constants.AdmIdPrefix + path).Should().Be(MetsIds.Adm(path));
            MetsIds.Normalise(Constants.TechIdPrefix + path).Should().Be(MetsIds.Tech(path));
            MetsIds.Normalise(Constants.DmdIdPrefix + path).Should().Be(MetsIds.Dmd(path));
        }
    }

    // -----------------------------------------------------------------------
    // A whole legacy document
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Normalising_A_Legacy_Document_Makes_Every_Id_Valid_And_Every_Reference_Resolve()
    {
        var (metsUri, report) = await NormaliseFixture("path-fixture-spaces.xml", "normalise-spaces.xml");

        report.Changed.Should().BeTrue();
        var doc = XDocument.Load(metsUri.LocalPath);
        AssertEveryIdIsValidAndEveryReferenceResolves(doc);

        // Spot-check the shape rather than only the invariants: an ID, the IDREFS that names it,
        // and the IDREF from the structMap all have to have moved together.
        var expectedFileId = MetsIds.File("objects/my file.pdf");
        var file = doc.Descendants(MetsNs + "file")
            .Single(f => (string?)f.Attribute("ID") == expectedFileId);
        file.Attribute("ADMID")!.Value.Should().Be(MetsIds.Adm("objects/my file.pdf"));
        doc.Descendants(MetsNs + "fptr").Select(f => (string?)f.Attribute("FILEID"))
            .Should().Contain(expectedFileId);
        doc.Descendants(MetsNs + "div").Select(d => (string?)d.Attribute("ID"))
            .Should().Contain(MetsIds.Phys("objects/my file.pdf"));
    }

    [Fact]
    public async Task Normalising_Leaves_The_Paths_And_The_Content_Alone()
    {
        var (metsUri, _) = await NormaliseFixture("path-fixture-spaces.xml", "normalise-content.xml");
        var doc = XDocument.Load(metsUri.LocalPath);

        // The migration renames identifiers. Everything that says what the object IS - the file
        // locations, the PREMIS names, the digests, the title - has to come out untouched, or the
        // diff this produces is no longer a one-file change.
        doc.Descendants(MetsNs + "FLocat").Select(l => (string?)l.Attribute(XLinkNs + "href"))
            .Should().BeEquivalentTo("objects/my file.pdf", "objects/my great file.pdf",
                "objects/my folder/my document.pdf");
        doc.Descendants(XName.Get("originalName", Constants.PremisNamespace)).Select(n => n.Value)
            .Should().Contain(["objects", "metadata", "objects/my file.pdf", "objects/my folder"]);
        doc.Descendants(XName.Get("messageDigest", Constants.PremisNamespace)).Select(n => n.Value)
            .Should().Contain(TestDigest);
        doc.Descendants(XName.Get("title", "http://www.loc.gov/mods/v3")).Single().Value
            .Should().Be("Spaces Test Fixture");
    }

    [Fact]
    public async Task Normalising_Is_Idempotent()
    {
        var (metsUri, first) = await NormaliseFixture("path-fixture-spaces.xml", "normalise-twice.xml");
        first.Changed.Should().BeTrue();
        var afterFirst = await File.ReadAllTextAsync(metsUri.LocalPath);

        var second = await NormaliseInPlace(metsUri);
        second.Changed.Should().BeFalse("a document that already conforms must not be modified");
        second.IdsRewritten.Should().Be(0);
        (await File.ReadAllTextAsync(metsUri.LocalPath)).Should().Be(afterFirst);
    }

    [Fact]
    public async Task A_Document_Written_By_The_Current_Code_Reports_No_Change()
    {
        // The migration must be able to tell "nothing to do" from "done", because preserving a
        // document that did not change would bump an Archival Group's version for nothing.
        var metsUri = new Uri(new FileInfo("Outputs/normalise-already-conforms.xml").FullName);
        var created = await metsManager.CreateStandardMets(metsUri, "Already Conforms");
        created.Success.Should().BeTrue(created.ErrorMessage ?? "");
        var eTag = created.Value!.ETag!;

        foreach (var path in new[] { "objects/my file.pdf", "objects/report (final), v2.pdf" })
        {
            var add = await metsManager.HandleSingleFileUpload(metsUri, SimpleFile(path), eTag);
            add.Success.Should().BeTrue(add.ErrorMessage ?? "");
            eTag = (await parser.GetMetsFileWrapper(metsUri)).Value!.ETag!;
        }

        var report = await NormaliseInPlace(metsUri);
        report.Changed.Should().BeFalse();
        report.Rewrites.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Normalised_Document_Is_Still_Navigable_By_Path()
    {
        // The point of the whole exercise: paths keep working, and keep working through the cache
        // rather than through any ID convention.
        var (metsUri, _) = await NormaliseFixture("path-fixture-spaces.xml", "normalise-navigable.xml");

        var eTag = (await parser.GetMetsFileWrapper(metsUri)).Value!.ETag!;
        var reloaded = (await metsManager.GetFullMets(metsUri, eTag)).Value!;
        reloaded.PathDiagnostics.Should().BeEmpty();
        reloaded.PhysicalDivsByPath.Keys.Should().Contain(
            ["objects", "metadata", "objects/my file.pdf", "objects/my folder",
                "objects/my folder/my document.pdf"]);

        // And it is still editable: adding a file to the migrated document works, and adds an ID
        // in the same form as the ones now around it.
        var add = await metsManager.HandleSingleFileUpload(
            metsUri, SimpleFile("objects/my folder/late arrival.pdf"), eTag);
        add.Success.Should().BeTrue(add.ErrorMessage ?? "");
        AssertEveryIdIsValidAndEveryReferenceResolves(XDocument.Load(metsUri.LocalPath));
    }

    // -----------------------------------------------------------------------
    // The parts that are easy to get wrong
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_Legacy_Link_Between_Two_Files_Follows_Both_Of_Its_Ends()
    {
        // smLink's ends are IDREFs held in xlink attributes, so they are not found the way ADMID
        // and FILEID are. A link left behind pointing at an ID that no longer exists is the
        // clearest way this migration could silently corrupt a document.
        var metsUri = CopyFixture("path-fixture-spaces.xml", "normalise-links.xml");
        var eTag = (await parser.GetMetsFileWrapper(metsUri)).Value!.ETag!;
        var fullMets = (await metsManager.GetFullMets(metsUri, eTag)).Value!;
        metsManager.LinkFile(fullMets, "objects/my file.pdf", "objects/my great file.pdf",
            new Uri("http://example.org/roles/transcription-of"));
        (await metsManager.WriteMets(fullMets)).Success.Should().BeTrue();

        await NormaliseInPlace(metsUri);

        var doc = XDocument.Load(metsUri.LocalPath);
        var link = doc.Descendants(MetsNs + "smLink").Single();
        link.Attribute(XLinkNs + "from")!.Value.Should().Be(MetsIds.File("objects/my file.pdf"));
        link.Attribute(XLinkNs + "to")!.Value.Should().Be(MetsIds.File("objects/my great file.pdf"));
        AssertEveryIdIsValidAndEveryReferenceResolves(doc);
    }

    [Fact]
    public async Task A_Logical_Range_Keeps_Its_Own_Id_And_Its_Pointers_Follow()
    {
        // A range ID comes from the client and is already a legal NCName. It is also public - the
        // IIIF Range URI is built from it - so renaming one would change a published identifier for
        // no reason. Its fptr, which names a legacy FILE id, does have to move.
        var metsUri = CopyFixture("path-fixture-spaces.xml", "normalise-logical.xml");
        var eTag = (await parser.GetMetsFileWrapper(metsUri)).Value!.ETag!;
        var fullMets = (await metsManager.GetFullMets(metsUri, eTag)).Value!;
        metsManager.SetStructMap(fullMets, new LogicalRange
        {
            Id = "LOG_0000", Type = "Sequence", Name = "A sequence",
            Files = [new FilePointer { LocalPath = "objects/my file.pdf" }]
        }).Success.Should().BeTrue();
        (await metsManager.WriteMets(fullMets)).Success.Should().BeTrue();

        var report = await NormaliseInPlace(metsUri);
        report.Rewrites.Select(r => r.From).Should().NotContain("LOG_0000");

        var doc = XDocument.Load(metsUri.LocalPath);
        var logical = doc.Descendants(MetsNs + "structMap")
            .Single(sm => (string?)sm.Attribute("TYPE") == Constants.Logical);
        logical.Descendants(MetsNs + "div").Select(d => (string?)d.Attribute("ID"))
            .Should().Contain("LOG_0000");
        logical.Descendants(MetsNs + "fptr").Single().Attribute("FILEID")!.Value
            .Should().Be(MetsIds.File("objects/my file.pdf"));
        AssertEveryIdIsValidAndEveryReferenceResolves(doc);
    }

    [Fact]
    public async Task A_Reference_That_Names_Nothing_Is_Still_Made_Legal_And_Reported()
    {
        // The template writes DMDID onto folder divs before the dmdSec that would satisfy it is
        // created, so a dangling DMDID is normal - and a legacy one is not a legal NCName. Leaving
        // it would mean the document still did not conform after a migration that said it did.
        var metsUri = CopyFixture("path-fixture-spaces.xml", "normalise-dangling.xml");
        var xml = await File.ReadAllTextAsync(metsUri.LocalPath);
        xml.Should().Contain("DMDID=\"DMD_metadata\"");
        await File.WriteAllTextAsync(metsUri.LocalPath,
            xml.Replace("DMDID=\"DMD_metadata\"", "DMDID=\"DMD_metadata/ad-hoc\""));

        var report = await NormaliseInPlace(metsUri);

        var doc = XDocument.Load(metsUri.LocalPath);
        var metadataDiv = doc.Descendants(MetsNs + "div")
            .Single(d => (string?)d.Attribute("LABEL") == "metadata");
        metadataDiv.Attribute("DMDID")!.Value.Should().Be("DMD_metadata_x002F_ad-hoc");
        report.Warnings.Should().Contain(w => w.Contains("DMD_metadata/ad-hoc"));

        foreach (var id in AllIds(doc))
        {
            var verify = () => XmlConvert.VerifyNCName(id);
            verify.Should().NotThrow($"'{id}' is written into an ID-typed attribute");
        }
    }

    [Fact]
    public async Task A_Half_Migrated_Document_Comes_Out_Whole()
    {
        // What a deposit taken from an Archival Group preserved before #214 and then added to
        // actually looks like: legacy IDs and encoded ones side by side, referring to each other.
        var metsUri = CopyFixture("path-fixture-spaces.xml", "normalise-mixed.xml");
        var eTag = (await parser.GetMetsFileWrapper(metsUri)).Value!.ETag!;
        var add = await metsManager.HandleSingleFileUpload(
            metsUri, SimpleFile("objects/my folder/new addition.pdf"), eTag);
        add.Success.Should().BeTrue(add.ErrorMessage ?? "");

        var report = await NormaliseInPlace(metsUri);

        // Only the legacy half moved.
        report.Rewrites.Select(r => r.From).Should().Contain("FILE_objects/my file.pdf");
        report.Rewrites.Select(r => r.From).Should()
            .NotContain(MetsIds.File("objects/my folder/new addition.pdf"));

        var doc = XDocument.Load(metsUri.LocalPath);
        AssertEveryIdIsValidAndEveryReferenceResolves(doc);
        AllIds(doc).Should().Contain(MetsIds.File("objects/my folder/new addition.pdf"));
    }

    [Fact]
    public async Task Unicode_And_Ampersands_Survive_The_Respelling()
    {
        var (metsUri, _) = await NormaliseFixture("path-fixture-special.xml", "normalise-special.xml");
        var doc = XDocument.Load(metsUri.LocalPath);

        AssertEveryIdIsValidAndEveryReferenceResolves(doc);
        // An accented letter is legal in an NCName, so it stays a letter; an ampersand is not.
        AllIds(doc).Should().Contain(MetsIds.File("objects/résumé.pdf"));
        AllIds(doc).Should().Contain(MetsIds.File("objects/AT&T guide.pdf"));
        doc.Descendants(MetsNs + "FLocat").Select(l => (string?)l.Attribute(XLinkNs + "href"))
            .Should().Contain("objects/AT&T guide.pdf", "the path itself is not encoded");
    }

    [Theory]
    // Real documents the platform wrote, kept as samples for other reasons entirely. They are here
    // because the fixtures are small and tidy and production is neither: these carry logical
    // structMaps, PREMIS events, several fileGrps and metadata folders, and between them they are
    // the closest thing in the repository to the corpus the migration will actually meet.
    [InlineData("liddle.mets.xml")]
    [InlineData("mets-sample-001.xml")]
    [InlineData("response-book.mets.xml")]
    [InlineData("simple-image.mets.xml")]
    [InlineData("wow.mets.xml")]
    public async Task Real_Preserved_Documents_Come_Out_Conforming(string sampleName)
    {
        var metsUri = CopyFixture(sampleName, $"normalise-{sampleName}");
        // Some of these already carry a dangling reference - liddle and wow both have a div whose
        // ADMID names an amdSec that is not there. Not the migration's business to fix, and the
        // measure of it doing no harm is that it leaves no more than it found.
        var danglingBefore = DanglingReferences(XDocument.Load(metsUri.LocalPath)).Count;

        var report = await NormaliseInPlace(metsUri);
        report.Changed.Should().BeTrue($"{sampleName} was written before the fix");

        var doc = XDocument.Load(metsUri.LocalPath);
        AssertEveryIdIsValidAndUnique(doc);
        DanglingReferences(doc).Should().HaveCountLessThanOrEqualTo(danglingBefore);

        // And running it again is a no-op, on a real document as much as a contrived one.
        (await NormaliseInPlace(metsUri)).Changed.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Migrating on write, so that an edit fixes the document it touches
    // -----------------------------------------------------------------------

    [Fact]
    public async Task With_The_Flag_Off_An_Edit_Leaves_The_Legacy_Ids_Alone()
    {
        // The behaviour every other test in the suite assumes, stated here so the flag's effect can
        // be read against it: adding to a legacy document mints an encoded ID for the new entry and
        // leaves the old ones exactly as they were. MetsIdIntegrityTests pins that this stays
        // navigable; what it costs is a document with two ID generations in it.
        var metsUri = CopyFixture("path-fixture-spaces.xml", "write-flag-off.xml");
        var eTag = (await parser.GetMetsFileWrapper(metsUri)).Value!.ETag!;

        var add = await metsManager.HandleSingleFileUpload(
            metsUri, SimpleFile("objects/added.pdf"), eTag);
        add.Success.Should().BeTrue(add.ErrorMessage ?? "");

        var ids = AllIds(XDocument.Load(metsUri.LocalPath));
        ids.Should().Contain("FILE_objects/my file.pdf", "the legacy IDs are untouched");
        ids.Should().Contain(MetsIds.File("objects/added.pdf"), "and the new one is encoded");
    }

    [Fact]
    public async Task With_The_Flag_On_An_Edit_Migrates_The_Document_It_Touches()
    {
        var metsUri = CopyFixture("path-fixture-spaces.xml", "write-flag-on.xml");
        var eTag = (await parser.GetMetsFileWrapper(metsUri)).Value!.ETag!;

        var add = await migratingOnWrite.HandleSingleFileUpload(
            metsUri, SimpleFile("objects/added.pdf"), eTag);
        add.Success.Should().BeTrue(add.ErrorMessage ?? "");

        var doc = XDocument.Load(metsUri.LocalPath);
        AssertEveryIdIsValidAndEveryReferenceResolves(doc);
        AllIds(doc).Should().NotContain("FILE_objects/my file.pdf",
            "the whole document was migrated, not just the part that was edited");
        AllIds(doc).Should().Contain(MetsIds.File("objects/my file.pdf"));
        AllIds(doc).Should().Contain(MetsIds.File("objects/added.pdf"));
    }

    [Fact]
    public async Task Migrating_On_Write_Applies_To_Every_Kind_Of_Edit()
    {
        // WriteMets is the single point every mutation passes through, which is the whole reason
        // the migration hangs off it. Prove that for a path that does not go through
        // HandleSingleChange at all.
        var metsUri = CopyFixture("path-fixture-spaces.xml", "write-flag-on-structmap.xml");
        var eTag = (await parser.GetMetsFileWrapper(metsUri)).Value!.ETag!;
        var fullMets = (await migratingOnWrite.GetFullMets(metsUri, eTag)).Value!;

        migratingOnWrite.SetStructMap(fullMets, new LogicalRange
        {
            Id = "LOG_0000", Type = "Sequence", Name = "A sequence",
            Files = [new FilePointer { LocalPath = "objects/my file.pdf" }]
        }).Success.Should().BeTrue();
        (await migratingOnWrite.WriteMets(fullMets)).Success.Should().BeTrue();

        var doc = XDocument.Load(metsUri.LocalPath);
        AssertEveryIdIsValidAndEveryReferenceResolves(doc);
        AllIds(doc).Should().Contain("LOG_0000", "a range ID was already legal and is not renamed");
    }

    [Fact]
    public async Task Migrating_On_Write_Does_Nothing_To_A_Document_Written_Today()
    {
        // The flag must be free for the overwhelming majority of writes, which are to documents
        // that already conform.
        var metsUri = new Uri(new FileInfo("Outputs/write-flag-on-fresh.xml").FullName);
        var created = await migratingOnWrite.CreateStandardMets(metsUri, "Fresh");
        created.Success.Should().BeTrue(created.ErrorMessage ?? "");

        var eTag = created.Value!.ETag!;
        var add = await migratingOnWrite.HandleSingleFileUpload(
            metsUri, SimpleFile("objects/report (final), v2.pdf"), eTag);
        add.Success.Should().BeTrue(add.ErrorMessage ?? "");

        var doc = XDocument.Load(metsUri.LocalPath);
        AssertEveryIdIsValidAndEveryReferenceResolves(doc);
        (await NormaliseInPlace(metsUri)).Changed.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Every value written into an ID-typed attribute must be a valid NCName and unique, and every
    /// reference must name one of them. The same contract as <see cref="MetsIdIntegrityTests"/>,
    /// checked here on the far side of a migration rather than after a write.
    /// </summary>
    private static void AssertEveryIdIsValidAndEveryReferenceResolves(XDocument doc)
    {
        AssertEveryIdIsValidAndUnique(doc);
        DanglingReferences(doc).Should().BeEmpty();
    }

    private static void AssertEveryIdIsValidAndUnique(XDocument doc)
    {
        var ids = AllIds(doc);
        ids.Should().OnlyHaveUniqueItems();
        foreach (var id in ids)
        {
            var verify = () => XmlConvert.VerifyNCName(id);
            verify.Should().NotThrow($"'{id}' is written into an xs:ID attribute");
        }
    }

    /// <summary>
    /// Every reference that names no element in the document, as "attribute -> value".
    /// </summary>
    /// <remarks>
    /// DMDID is excluded for the reason given in <see cref="MetsIdIntegrityTests"/>: the template
    /// writes it onto folder divs before their dmdSec exists, so it dangles by design. ADMID is not
    /// excluded, but real preserved documents turn out to carry the occasional dangling one too -
    /// so this is counted rather than forbidden, and the migration is held to leaving it no worse.
    /// A dangling reference may name nothing; it may not be illegal XML, and that part is absolute.
    /// </remarks>
    private static List<string> DanglingReferences(XDocument doc)
    {
        var declared = AllIds(doc).ToHashSet();
        var dangling = new List<string>();

        foreach (var element in doc.Descendants())
        {
            foreach (var name in new[] { "ADMID", "STRUCTID" })
            {
                var value = (string?)element.Attribute(name);
                if (value is null || declared.Contains(value)) continue;
                foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!declared.Contains(token))
                    {
                        dangling.Add($"{element.Name.LocalName}/@{name} -> {token}");
                    }
                }
            }

            var fileId = (string?)element.Attribute("FILEID");
            if (fileId is not null && !declared.Contains(fileId))
            {
                dangling.Add($"{element.Name.LocalName}/@FILEID -> {fileId}");
            }
        }

        foreach (var link in doc.Descendants(MetsNs + "smLink"))
        {
            foreach (var end in new[] { "from", "to" })
            {
                var value = (string?)link.Attribute(XLinkNs + end);
                if (value is not null && !declared.Contains(value))
                {
                    dangling.Add($"smLink/@xlink:{end} -> {value}");
                }
            }
        }

        return dangling;
    }

    /// <summary>Every xs:ID the document declares.</summary>
    private static List<string> AllIds(XDocument doc) =>
        doc.Descendants()
            .Select(e => (string?)e.Attribute("ID"))
            .Where(id => id is not null)
            .Select(id => id!)
            .ToList();

    private async Task<(Uri metsUri, MetsIdNormalisationReport report)>
        NormaliseFixture(string fixtureName, string outputName)
    {
        var metsUri = CopyFixture(fixtureName, outputName);
        return (metsUri, await NormaliseInPlace(metsUri));
    }

    private async Task<MetsIdNormalisationReport> NormaliseInPlace(Uri metsUri)
    {
        var eTag = (await parser.GetMetsFileWrapper(metsUri)).Value!.ETag!;
        var fullMets = (await metsManager.GetFullMets(metsUri, eTag)).Value!;
        var result = metsManager.NormaliseIds(fullMets);
        result.Success.Should().BeTrue(result.ErrorMessage ?? "");
        if (result.Value!.Changed)
        {
            (await metsManager.WriteMets(fullMets)).Success.Should().BeTrue();
        }
        return result.Value!;
    }

    private static WorkingFile SimpleFile(string localPath) =>
        new()
        {
            LocalPath = localPath,
            Name = localPath.Split('/')[^1],
            ContentType = "application/pdf",
            Digest = TestDigest,
            Size = 54321,
            Modified = DateTime.UtcNow
        };

    // -----------------------------------------------------------------------
    // The ways a document can be worse than "some IDs need renaming"
    // -----------------------------------------------------------------------

    [Fact]
    public async Task An_Area_That_Points_At_An_Id_Follows_It()
    {
        // BEGIN holds an IDREF when BETYPE says so. This platform never writes that, but a document
        // it did not write can arrive at the normalise endpoint - nothing checks the creating agent
        // - and a BEGIN left naming a renamed ID is a dangling reference like any other.
        var metsUri = EditedFixture("normalise-area-idref.xml", doc =>
        {
            var fptr = doc.Descendants(MetsNs + "fptr")
                .Single(f => f.Attribute("FILEID")!.Value == "FILE_objects/my file.pdf");
            fptr.Add(new XElement(MetsNs + "area",
                new XAttribute("FILEID", "FILE_objects/my great file.pdf"),
                new XAttribute("BETYPE", "IDREF"),
                new XAttribute("BEGIN", "FILE_objects/my great file.pdf")));
        });

        var report = await NormaliseInPlace(metsUri);

        report.Changed.Should().BeTrue();
        var area = XDocument.Load(metsUri.LocalPath).Descendants(MetsNs + "area").Single();
        area.Attribute("BEGIN")!.Value.Should()
            .Be(MetsIds.File("objects/my great file.pdf"), "BEGIN named an ID that has been renamed");
        AssertEveryIdIsValidAndEveryReferenceResolves(XDocument.Load(metsUri.LocalPath));
    }

    [Fact]
    public async Task An_Area_Whose_Begin_Is_Not_An_Id_Is_Left_Alone()
    {
        // The same attribute holds a byte offset, a time code or an ordinal for every other BETYPE.
        // Normalising one of those would corrupt the area while looking like the same job.
        var metsUri = EditedFixture("normalise-area-byte.xml", doc =>
        {
            var fptr = doc.Descendants(MetsNs + "fptr")
                .Single(f => f.Attribute("FILEID")!.Value == "FILE_objects/my file.pdf");
            fptr.Add(new XElement(MetsNs + "area",
                new XAttribute("FILEID", "FILE_objects/my file.pdf"),
                new XAttribute("BETYPE", "BYTE"),
                new XAttribute("BEGIN", "0"),
                new XAttribute("END", "1024")));
        });

        await NormaliseInPlace(metsUri);

        var area = XDocument.Load(metsUri.LocalPath).Descendants(MetsNs + "area").Single();
        area.Attribute("BEGIN")!.Value.Should().Be("0");
        area.Attribute("END")!.Value.Should().Be("1024");
    }

    [Fact]
    public async Task A_Legacy_Id_Spelt_With_Two_Spaces_Is_Still_Followed()
    {
        // An IDREFS attribute reaches the normaliser already split on whitespace, so an ID spelt
        // with two spaces comes back with one and cannot be found by its real name. Both halves are
        // legal NCNames on their own, so nothing downstream would notice the reference going stale.
        const string legacy = "ADM_objects/my  file.pdf";
        var metsUri = EditedFixture("normalise-double-space.xml", doc =>
        {
            doc.Descendants(MetsNs + "amdSec")
                .Single(a => a.Attribute("ID")!.Value == "ADM_objects/my file.pdf")
                .SetAttributeValue("ID", legacy);
            doc.Descendants(MetsNs + "file")
                .Single(f => f.Attribute("ID")!.Value == "FILE_objects/my file.pdf")
                .SetAttributeValue("ADMID", legacy);
        });

        var report = await NormaliseInPlace(metsUri);

        var doc2 = XDocument.Load(metsUri.LocalPath);
        var expected = MetsIds.Normalise(legacy);
        doc2.Descendants(MetsNs + "amdSec").Select(a => a.Attribute("ID")!.Value)
            .Should().Contain(expected);
        doc2.Descendants(MetsNs + "file")
            .Single(f => f.Attribute("ID")!.Value == MetsIds.File("objects/my file.pdf"))
            .Attribute("ADMID")!.Value.Should().Be(expected);
        report.Warnings.Should().NotContain(w => w.Contains("names no element"),
            "the reference does name an element, just one whose spelling the round trip lost");
        AssertEveryIdIsValidAndEveryReferenceResolves(doc2);
    }

    [Fact]
    public async Task A_Genuine_List_Is_Not_Mistaken_For_The_Collapse_Of_A_Spaced_Id()
    {
        // The pathological cousin of the two-space case. The document declares a legacy ID whose
        // collapsed spelling happens to equal a genuine list of two other declared IDs. The
        // platform's resolver reads that list per token - every token is declared - so the
        // normaliser must not re-read it as the legacy ID, even though the collapsed lookup matches.
        var metsUri = EditedFixture("normalise-collapse-collision.xml", doc =>
        {
            var mets = doc.Root!;
            var firstAmdSec = doc.Descendants(MetsNs + "amdSec").First();
            firstAmdSec.AddBeforeSelf(
                new XElement(MetsNs + "amdSec", new XAttribute("ID", "ADM_ledger  notes")),
                new XElement(MetsNs + "amdSec", new XAttribute("ID", "ADM_ledger")),
                new XElement(MetsNs + "amdSec", new XAttribute("ID", "notes")));
            doc.Descendants(MetsNs + "div")
                .Single(d => d.Attribute("ID")!.Value == "PHYS_objects/my great file.pdf")
                .SetAttributeValue("ADMID", "ADM_ledger notes");
        });

        await NormaliseInPlace(metsUri);

        var doc2 = XDocument.Load(metsUri.LocalPath);
        doc2.Descendants(MetsNs + "div")
            .Single(d => (string?)d.Attribute("ID") == MetsIds.Phys("objects/my great file.pdf"))
            .Attribute("ADMID")!.Value.Should().Be("ADM_ledger notes",
                "every token is a declared ID, so this is a list and must stay one");
        doc2.Descendants(MetsNs + "amdSec").Select(a => (string?)a.Attribute("ID"))
            .Should().Contain(MetsIds.Normalise("ADM_ledger  notes"),
                "the spaced ID itself is still normalised");
    }

    [Fact]
    public async Task A_Document_With_The_Same_Id_On_Two_Elements_Is_Refused()
    {
        // Already invalid, and a rewrite maps an ID rather than an element - so both would take the
        // same new legal ID and the duplication would stop being visible. Nothing is touched.
        var metsUri = EditedFixture("normalise-duplicate-ids.xml", doc =>
            doc.Descendants(MetsNs + "amdSec")
                .Single(a => a.Attribute("ID")!.Value == "ADM_objects/my great file.pdf")
                .SetAttributeValue("ID", "ADM_objects/my file.pdf"));
        var before = await File.ReadAllTextAsync(metsUri.LocalPath);

        // No ETag, because there is no getting one: MetsParser indexes IDs into a dictionary and
        // throws on the second copy. GetFullMets does not, which is the whole point - the document
        // reaches the normaliser even though the parser cannot make sense of it.
        var fullMets = await metsManager.GetFullMets(metsUri, null);
        fullMets.Success.Should().BeTrue("this is the path a normalise request actually takes");

        var result = metsManager.NormaliseIds(fullMets.Value!);

        result.Success.Should().BeFalse("the document carries one ID on two elements");
        result.ErrorMessage.Should().Contain("ADM_objects/my file.pdf");
        (await File.ReadAllTextAsync(metsUri.LocalPath)).Should().Be(before);
    }

    [Fact]
    public async Task Writing_A_Document_With_Duplicate_Ids_Writes_It_Unnormalised()
    {
        // The duplicate-ID refusal happens before the in-memory document is touched, so the write
        // path can pass the document through unchanged rather than refusing - a duplicate ID is a
        // per-document problem (a failed-and-retried add can mint one, issue #216), and refusing
        // here would make EVERY edit to the deposit fail until the service-wide flag was turned
        // off for everyone. The failure that matters - the document is still invalid - is exactly
        // as visible as it was before the write.
        var metsUri = EditedFixture("normalise-duplicate-on-write.xml", doc =>
            doc.Descendants(MetsNs + "amdSec")
                .Single(a => a.Attribute("ID")!.Value == "ADM_objects/my great file.pdf")
                .SetAttributeValue("ID", "ADM_objects/my file.pdf"));

        var fullMets = (await migratingOnWrite.GetFullMets(metsUri, null)).Value!;

        var write = await migratingOnWrite.WriteMets(fullMets);

        write.Success.Should().BeTrue("a broken document must not brick the whole deposit");
        var doc2 = XDocument.Load(metsUri.LocalPath);
        doc2.Descendants(MetsNs + "amdSec")
            .Count(a => (string?)a.Attribute("ID") == "ADM_objects/my file.pdf")
            .Should().Be(2, "the document was written exactly as it was, duplication intact");
    }

    [Fact]
    public async Task A_Reference_To_A_Dropped_Rewrite_Is_Left_Pointing_At_Its_Element()
    {
        // X has a legacy ID whose normalised spelling Y already carries. X's rewrite is dropped
        // (rewriting it would collide), so X keeps its old ID - and the reference to X must then
        // keep its old spelling too. Normalising the reference anyway would silently retarget it
        // onto Y: a fptr pointing at the wrong file in a preserved document.
        var metsUri = EditedFixture("normalise-collision-reference.xml", doc =>
            doc.Descendants(MetsNs + "amdSec").First().AddBeforeSelf(
                new XElement(MetsNs + "amdSec",
                    new XAttribute("ID", MetsIds.Adm("objects/my file.pdf")))));
        // The fixture's file element already references ADM_objects/my file.pdf (the legacy X).

        var report = await NormaliseInPlace(metsUri);

        var doc2 = XDocument.Load(metsUri.LocalPath);
        doc2.Descendants(MetsNs + "file")
            .Select(f => (string?)f.Attribute("ADMID"))
            .Should().Contain("ADM_objects/my file.pdf",
                "the reference still names X, whose rewrite was declined");
        report.Warnings.Should().Contain(w => w.Contains("already used by another element"));
        report.Warnings.Should().NotContain(w => w.Contains("names no element"),
            "the reference names a real element; calling it dangling would be wrong twice over");
    }

    [Fact]
    public async Task A_Broken_Token_Does_Not_Take_Its_Valid_Siblings_With_It()
    {
        // An IDREFS list with one broken entry beside a declared, valid one. The broken entry is
        // normalised alone; collapsing the whole list into one pseudo-ID would sever the div's
        // real link to its amdSec.
        var metsUri = EditedFixture("normalise-mixed-list.xml", doc =>
            doc.Descendants(MetsNs + "div")
                .Single(d => d.Attribute("ID")!.Value == "PHYS_objects/my great file.pdf")
                .SetAttributeValue("ADMID", "bad/token ADM_objects"));

        await NormaliseInPlace(metsUri);

        var doc2 = XDocument.Load(metsUri.LocalPath);
        doc2.Descendants(MetsNs + "div")
            .Single(d => (string?)d.Attribute("ID") == MetsIds.Phys("objects/my great file.pdf"))
            .Attribute("ADMID")!.Value
            .Should().Be("bad_x002F_token ADM_objects",
                "the broken token is fixed alone and the declared sibling survives");
    }

    /// <summary>
    /// A copy of the standard legacy fixture with one thing about it changed - the defect under
    /// test - so that everything around it is a document the parser is known to accept.
    /// </summary>
    private static Uri EditedFixture(string outputName, Action<XDocument> edit)
    {
        var doc = XDocument.Load(new FileInfo("Samples/path-fixture-spaces.xml").FullName);
        edit(doc);
        var dest = new FileInfo($"Outputs/{outputName}");
        doc.Save(dest.FullName);
        return new Uri(dest.FullName);
    }

    private static Uri CopyFixture(string fixtureName, string outputName)
    {
        var source = new FileInfo($"Samples/{fixtureName}");
        var dest = new FileInfo($"Outputs/{outputName}");
        File.Copy(source.FullName, dest.FullName, overwrite: true);
        return new Uri(dest.FullName);
    }
}
