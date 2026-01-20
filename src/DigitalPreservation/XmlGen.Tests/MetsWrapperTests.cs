using DigitalPreservation.Common.Model.Transit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Storage.Repository.Common.Mets;
using Storage.Repository.Common.Mets.StorageImpl;

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
    public async void Can_Parse_Goobi_METS_For_Wrapper()
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
    public async void Can_Parse_METS_From_FolderReference()
    {
        var metsLoader = new FileSystemMetsLoader();
        var parser = new MetsParser(metsLoader, logger);
        var metsFolderContainer = new DirectoryInfo("Samples");
        var result = await parser.GetMetsFileWrapper(new Uri(metsFolderContainer.FullName));

        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNull();
    }
    
}