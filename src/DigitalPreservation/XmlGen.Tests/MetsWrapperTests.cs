using DigitalPreservation.Mets;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;

namespace XmlGen.Tests;

public class MetsWrapperTests
{
    private ILogger<MetsParser> logger;
    
    public MetsWrapperTests()
    {
        var serviceProvider = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        var factory = serviceProvider.GetService<ILoggerFactory>();
        logger = factory!.CreateLogger<MetsParser>();    
    }
    
    [Fact]
    public async Task Can_Parse_Goobi_METS_For_Wrapper()
    {
        var metsLoader = new FileSystemMetsLoader();
        var parser = new MetsParser(metsLoader, logger);
        var goobiMetsFile = new FileInfo("Samples/goobi-wc-b29356350-2.xml");
        var result = await parser.GetMetsFileWrapper(new Uri(goobiMetsFile.FullName));

        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNull();

        var phys = result.Value!.PhysicalStructure;
        phys!.Files.Should().Contain(f => f.Name == "goobi-wc-b29356350-2.xml");

        phys.Directories.Should().HaveCount(2);
        var objDir = phys.Directories.Single(d => d.Name == FolderNames.Objects);
        objDir.Directories.Should().HaveCount(0);
        objDir.Files.Should().HaveCount(32);
        objDir.LocalPath.Should().Be(FolderNames.Objects);
        var altoDir = phys.Directories.Single(d => d.Name == "alto");
        altoDir.Directories.Should().HaveCount(0);
        altoDir.Files.Should().HaveCount(32);
        altoDir.LocalPath.Should().Be("alto");
        
        objDir.Files[10].LocalPath.Should().Be("objects/b29356350_0011.jp2");
        objDir.Files[10].Name.Should().Be("b29356350_0011.jp2");
        objDir.Files[10].ContentType.Should().Be("image/jp2");
        
        altoDir.Files[10].LocalPath.Should().Be("alto/b29356350_0011.xml");
        altoDir.Files[10].Name.Should().Be("b29356350_0011.xml");
        altoDir.Files[10].ContentType.Should().Be("application/xml");
    }
    
    [Fact]
    public async Task Can_Parse_EPrints_METS()
    {
        var metsLoader = new FileSystemMetsLoader();
        var parser = new MetsParser(metsLoader, logger);
        var eprintsMets = new FileInfo("Samples/EPrints.10315.METS.xml");
        var result = await parser.GetMetsFileWrapper(new Uri(eprintsMets.FullName));

        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Self.Should().NotBeNull();
        result.Value.Self!.Digest.Should().NotBeEmpty();
        var phys = result.Value!.PhysicalStructure;
        phys!.Files.Should().Contain(f => f.Name == "EPrints.10315.METS.xml");

        result.Value.Name.Should().Be("[Example title]");
        phys.Directories.Should().HaveCount(1);
        phys.Directories[0].Files.Should().HaveCount(4);
        
        phys.Directories[0].Name.Should().Be(FolderNames.Objects);
        phys.Directories[0].Files[0].Digest.Should().Be("4675c73e6fd66d2ea9a684ec79e4e6559bb4d44a35e8234794b0691472b0385d");
        phys.Directories[0].Files[3].Digest.Should().Be("315bb3bd2eb2da5ce8b848bb7f09803d8a48e64021c4b6fe074aed9cc591c154");

        phys.Directories[0].Files[3].LocalPath.Should().Be("objects/372705s_004.jpg");
        phys.Directories[0].Files[3].Name.Should().Be("372705s_004.jpg");
    }

    [Fact]
    public async Task Can_Parse_Archivematica_METS()
    {
        var metsLoader = new FileSystemMetsLoader();
        var parser = new MetsParser(metsLoader, logger);
        var archivematicaMets = new FileInfo("Samples/archivematica-wc-METS.299eb16f-1e62-4bf6-b259-c82146153711.xml");
        var result = await parser.GetMetsFileWrapper(new Uri(archivematicaMets.FullName));

        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Self.Should().NotBeNull();
        result.Value.Self!.Digest.Should().NotBeEmpty();
        var phys = result.Value!.PhysicalStructure;
        phys!.Files.Should().Contain(f => f.Name == "archivematica-wc-METS.299eb16f-1e62-4bf6-b259-c82146153711.xml");

        result.Value.Name.Should().BeNull(); // No name in Archivematica METS
        phys.Directories.Should().HaveCount(1);
        
        result.Value.Files.Count.Should().Be(38 + 1);
        result.Value.Files.Should().Contain(f => f.LocalPath == "objects/Edgware_Community_Hospital/03_05_01.tif");
        result.Value.Files.Should().Contain(f => f.LocalPath == "objects/Edgware_Community_Hospital/presentation_site_plan_A3.pdf");
        result.Value.Files.Should().Contain(f => f.LocalPath == "objects/metadata/transfers/ARTCOOB9-4840a241-d397-4554-abfe-69f1ad674126/rights.csv");
            
        var objDir = phys.Directories[0]; 
        objDir.Files.Should().HaveCount(0);
        objDir.Directories.Should().HaveCount(5);
        objDir.Files.Should().HaveCount(0); // no direct children
        objDir.LocalPath.Should().Be(FolderNames.Objects);
        objDir.Name.Should().Be(FolderNames.Objects);
        objDir.Directories.Should().Contain(d => d.LocalPath == "objects/Edgware_Community_Hospital");
        objDir.Directories.Should().Contain(d => d.LocalPath == "objects/West_Middlesex");
        objDir.Directories.Should().Contain(d => d.LocalPath == "objects/GJW_King_s_College_Hospital");
        objDir.Directories.Should().Contain(d => d.LocalPath == "objects/submissionDocumentation");
        objDir.Directories.Should().Contain(d => d.LocalPath == "objects/metadata");
        var kings = objDir.FindDirectory("GJW_King_s_College_Hospital", false);
        kings.Should().NotBeNull();
        kings!.Name.Should().Be("GJW_King_s_College_Hospital"); // unaltered
        kings.Directories.Should().HaveCount(0);
        kings.Files.Should().HaveCount(13);
        var plan = kings.FindFile("Kings_1913_plan_altered.jpg");
        plan.Should().NotBeNull();
        plan!.Name.Should().Be("Kings 1913 plan altered.jpg"); // note with spaces from LABEL
        var edgware = objDir.FindDirectory("Edgware_Community_Hospital");
        edgware.Should().NotBeNull();
        edgware!.Name.Should().Be("Edgware Community Hospital"); // with spaces
        edgware.Directories.Should().HaveCount(0);
        edgware.Files.Should().HaveCount(11);
    }
    
    [Fact]
    public async Task Can_Parse_METS_From_FolderReference()
    {
        // FileSystemMetsLoader.FindMetsFile searches the directory for the first file
        // whose name (lowercased slug) contains "mets" and ends in ".xml", preferring
        // the canonical name "mets.xml". This test verifies that the folder-reference
        // code path resolves successfully and that the resolved METS can be fully parsed.

        var metsLoader = new FileSystemMetsLoader();
        var parser = new MetsParser(metsLoader, logger);
        var metsFolderContainer = new DirectoryInfo("Samples");
        var result = await parser.GetMetsFileWrapper(new Uri(metsFolderContainer.FullName));

        result.Success.Should().BeTrue();
        var wrapper = result.Value!;
        wrapper.Should().NotBeNull();

        // Whichever METS file was resolved, it must have a parseable physical structure
        wrapper.PhysicalStructure.Should().NotBeNull(
            "the resolved METS must have a structMap physical structure");

        // The METS file itself is always listed as a WorkingFile in the wrapper root
        // (FileSystemMetsLoader.LoadMetsFileAsWorkingFile populates it with ContentType=application/xml)
        wrapper.Files.Should().Contain(f => f.ContentType == "application/xml",
            "the resolved METS file itself must appear as a WorkingFile in the wrapper");

        // The resolved file came from the Samples directory
        wrapper.Files.Should().Contain(f => f.Name.EndsWith(".xml"),
            "the resolved file must be an XML file from the Samples folder");
    }

    [Fact]
    public async Task Third_Party_METS_Is_Not_Editable()
    {
        // MetsFileWrapper.Editable is true only when the Agent matches MetsCreatorAgent
        // (i.e., METS files we created ourselves). Third-party METS from Goobi,
        // Archivematica, and EPrints must not be treated as editable.

        var metsLoader = new FileSystemMetsLoader();
        var parser = new MetsParser(metsLoader, logger);

        var goobiResult = await parser.GetMetsFileWrapper(
            new Uri(new FileInfo("Samples/goobi-wc-b29356350-2.xml").FullName));
        goobiResult.Value!.Editable.Should().BeFalse();
        goobiResult.Value.Agent.Should().NotBe(Constants.MetsCreatorAgent);

        var archivematicaResult = await parser.GetMetsFileWrapper(
            new Uri(new FileInfo("Samples/archivematica-wc-METS.299eb16f-1e62-4bf6-b259-c82146153711.xml").FullName));
        archivematicaResult.Value!.Editable.Should().BeFalse();

        var eprintsResult = await parser.GetMetsFileWrapper(
            new Uri(new FileInfo("Samples/EPrints.10315.METS.xml").FullName));
        eprintsResult.Value!.Editable.Should().BeFalse();
    }

    [Fact]
    public async Task MetsParser_Extracts_Premis_Metadata_From_Goobi_METS()
    {
        // GetMetsFileWrapper must populate WorkingFile.Metadata with FileFormatMetadata
        // derived from PREMIS techMD in a Goobi METS file.
        // This data is what the ImportJob diff uses to determine fixity and format.

        var metsLoader = new FileSystemMetsLoader();
        var parser = new MetsParser(metsLoader, logger);
        var goobiMetsFile = new FileInfo("Samples/goobi-wc-b29356350-2.xml");
        var result = await parser.GetMetsFileWrapper(new Uri(goobiMetsFile.FullName));

        result.Success.Should().BeTrue();

        var file = result.Value!.Files.Single(f => f.LocalPath == "objects/b29356350_0001.jp2");

        // Top-level properties populated from fileSec
        file.ContentType.Should().Be("image/jp2");

        // FileFormatMetadata populated from PREMIS techMD
        var ffm = file.Metadata.OfType<FileFormatMetadata>().Single();
        ffm.Source.Should().Be("METS");
        ffm.PronomKey.Should().Be("x-fmt/392");
        ffm.FormatName.Should().Be("JP2 (JPEG 2000 part 1)");
        ffm.Size.Should().Be(1348420);
        // Note: the Goobi sample labels its digest algorithm as "sha256" but the
        // stored value is 40 hex characters (SHA-1 length) — a data quality issue
        // in the source METS, not a parser bug. The parser reads it as supplied.
        ffm.Digest.Should().Be("6eb6c17cd93e392fed8e1cb4d9de5617b8a9b4de");
    }

    [Fact]
    public async Task MetsParser_Extracts_Premis_Metadata_From_EPrints_METS()
    {
        // GetMetsFileWrapper must populate WorkingFile.Metadata with FileFormatMetadata
        // derived from PREMIS techMD in an EPrints METS file.

        var metsLoader = new FileSystemMetsLoader();
        var parser = new MetsParser(metsLoader, logger);
        var eprintsMets = new FileInfo("Samples/EPrints.10315.METS.xml");
        var result = await parser.GetMetsFileWrapper(new Uri(eprintsMets.FullName));

        result.Success.Should().BeTrue();

        // files[0] is the first object file; its digest is already asserted in
        // Can_Parse_EPrints_METS. Here we additionally verify the PRONOM metadata.
        var file = result.Value!.PhysicalStructure!.Directories
            .Single(d => d.Name == FolderNames.Objects).Files[0];

        var ffm = file.Metadata.OfType<FileFormatMetadata>().Single();
        ffm.Source.Should().Be("METS");
        ffm.PronomKey.Should().Be("fmt/43");
        ffm.FormatName.Should().Be("JPEG File Interchange Format");
        ffm.Size.Should().Be(876464);
        ffm.Digest.Should().Be("4675c73e6fd66d2ea9a684ec79e4e6559bb4d44a35e8234794b0691472b0385d");
    }

    [Fact]
    public async Task MetsParser_Extracts_Premis_Metadata_From_Archivematica_METS()
    {
        // GetMetsFileWrapper must populate WorkingFile.Metadata with FileFormatMetadata
        // derived from PREMIS techMD in an Archivematica METS file.

        var metsLoader = new FileSystemMetsLoader();
        var parser = new MetsParser(metsLoader, logger);
        var archivematicaMets = new FileInfo("Samples/archivematica-wc-METS.299eb16f-1e62-4bf6-b259-c82146153711.xml");
        var result = await parser.GetMetsFileWrapper(new Uri(archivematicaMets.FullName));

        result.Success.Should().BeTrue();

        var file = result.Value!.Files
            .Single(f => f.LocalPath == "objects/Edgware_Community_Hospital/03_05_01.tif");

        var ffm = file.Metadata.OfType<FileFormatMetadata>().Single();
        ffm.Source.Should().Be("METS");
        ffm.PronomKey.Should().Be("fmt/353");
        ffm.FormatName.Should().Be("Tagged Image File Format");
        ffm.Size.Should().Be(2383740);
        ffm.Digest.Should().Be("e05e9d3f5f6771b17274404a2d4230970e5a782b8f519e2447853032ef53ee84");
    }
}