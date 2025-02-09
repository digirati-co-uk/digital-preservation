using System.Text.Json;
using Amazon.S3;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.Transit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Storage.Repository.Common.Mets;

namespace XmlGen.Tests;

public class MetsManagerTests
{
    private IMetsManager metsManager;
    private IMetsParser parser;

    public MetsManagerTests()
    {
        var serviceProvider = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        var factory = serviceProvider.GetService<ILoggerFactory>();
        var parserLogger = factory!.CreateLogger<MetsParser>();
        var s3Client = new AmazonS3Client();
        parser = new MetsParser(s3Client, parserLogger);
        metsManager = new MetsManager(parser, s3Client);
    }


    // [Fact(Skip = "Experimental")]
    [Fact]
    public async Task Can_Create_Empty_Mets()
    {
        var emptyMetsFi = new FileInfo("Outputs/empty-mets.xml");
        var result = await metsManager.CreateStandardMets(new Uri(emptyMetsFi.FullName), "Empty Mets File");

        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNull();

        result.Value.PhysicalStructure.Directories.Should().HaveCount(1);
        result.Value.PhysicalStructure.Directories.Should().Contain(wd => wd.Name == "objects");
        
        result.Value.PhysicalStructure.Files.Should().HaveCount(1);
        result.Value.PhysicalStructure.Files.Should().Contain(wd => wd.Name == "empty-mets.xml");
        
        // TODO: Validate result.Value.XDocument
    }
    
    // [Fact(Skip = "Experimental")]
    [Fact]
    public async Task Can_Create_Mets_From_Archival_Group()
    {
        var agFi = new FileInfo("Samples/archivalGroup.json");
        var agMetsFi = new FileInfo("Outputs/archivalGroup-mets.xml");
        
        var archivalGroup = JsonSerializer.Deserialize<ArchivalGroup>(await File.ReadAllTextAsync(agFi.FullName));
        var result = await metsManager.CreateStandardMets(
            new Uri(agMetsFi.FullName), 
            archivalGroup!, 
            "ArchivalGroup Mets File");

        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNull();

        result.Value.PhysicalStructure.Directories.Should().HaveCount(1);
        result.Value.PhysicalStructure.Directories.Should().Contain(wd => wd.Name == "objects");
        
        result.Value.PhysicalStructure.Files.Should().HaveCount(1);
        result.Value.PhysicalStructure.Files.Should().Contain(wd => wd.Name == "archivalGroup-mets.xml");

        var objectsDir = result.Value.PhysicalStructure.Directories[0];
        objectsDir.Files.Should().HaveCount(2);
        objectsDir.Files[0].Name.Should().Be("Minutes LAQM 21 June 2020.pdf");
        objectsDir.Files[0].LocalPath.Should().Be("objects/minutes-laqm-21-june-2020.pdf");
        objectsDir.Files[0].Digest.Should().Be("eb634d64ce8e6be5195174ceaef9ac9e19c37119f3b31618630aa633ccdbf68f");
        objectsDir.Files[1].Name.Should().Be("MINUTES LAQM 8 Sept 2020.pdf");
        objectsDir.Files[1].LocalPath.Should().Be("objects/minutes-laqm-8-sept-2020.pdf");
        objectsDir.Files[1].Digest.Should().Be("9c5aa04a39812c80bcc824e366044c16fb090efb076c8552ca9ca932f0dfc981");

        objectsDir.Directories.Should().HaveCount(1);
        
        var folderB = objectsDir.Directories[0];
        folderB.Name.Should().Be("folder b");
        folderB.LocalPath.Should().Be("objects/folder-b");
        folderB.Files.Should().HaveCount(0);
        folderB.Directories.Should().HaveCount(1);
        
        var folderBB = folderB.Directories[0];
        folderBB.Name.Should().Be("folder bb");
        folderBB.LocalPath.Should().Be("objects/folder-b/folder-bb");
        folderBB.Files.Should().HaveCount(1);
        folderBB.Directories.Should().HaveCount(0);

        var fileBB1 = folderBB.Files[0];
        fileBB1.Name.Should().Be("MINUTES LAQM 22 April 2020.pdf");
        fileBB1.LocalPath.Should().Be("objects/folder-b/folder-bb/minutes-laqm-22-april-2020.pdf");
        fileBB1.Digest.Should().Be("310fa7a479e0d0f79caf21e6a2c607e81bb0ccd5c2829bff7b816a49925419e7");
        
        // TODO: Validate result.Value.XDocument
    }
    
    
    // [Fact(Skip = "Experimental")]
    [Fact]
    public async Task Can_Add_Directories_To_Empty_Mets()
    {
        var emptyMetsFi = new FileInfo("Outputs/empty-met-add-dirs.xml");
        var metsUri = new Uri(emptyMetsFi.FullName);
        var result = await metsManager.CreateStandardMets(
            metsUri, "Empty Mets File - Add Directories");
        
        result.Success.Should().BeTrue();
        result.Value.PhysicalStructure.Directories.Should().HaveCount(1);
        result.Value.PhysicalStructure.Directories.Should().Contain(wd => wd.Name == "objects");
        
        var dir = new WorkingDirectory
        {
            LocalPath = "objects/child-dir",
            Name = "Child Directory",
            Modified = DateTime.UtcNow
        };
        var addResult = await metsManager.HandleCreateFolder(metsUri, dir, "etag");
        addResult.Success.Should().BeTrue();
        
        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();

        parseResult.Value.PhysicalStructure.Directories[0].Directories.Should().HaveCount(1);
        parseResult.Value.PhysicalStructure.Directories[0].Directories[0].Name.Should().Be("Child Directory");
        parseResult.Value.PhysicalStructure.Directories[0].Directories[0].LocalPath.Should().Be("objects/child-dir");
        
        // TODO: Validate result.Value.XDocument
    }
    
    // [Fact(Skip = "Experimental")]
    [Fact]
    public void Can_Add_Directories_To_ArchivalGroup_Mets()
    {
        
        // TODO: Validate result.Value.XDocument
    }
    
    // [Fact(Skip = "Experimental")]
    [Fact]
    public void Can_Add_Files_To_Empty_Mets()
    {
        
        // TODO: Validate result.Value.XDocument
    }
    
    // [Fact(Skip = "Experimental")]
    [Fact]
    public void Can_Add_Files_To_ArchivalGroup_Mets()
    {
        
        // TODO: Validate result.Value.XDocument
    }
    
    
    // [Fact(Skip = "Experimental")]
    [Fact]
    public void Can_Delete_Files_From_Mets()
    {
        
        // TODO: Validate result.Value.XDocument
    }
    
    
    // [Fact(Skip = "Experimental")]
    [Fact]
    public void Can_Delete_Directories_From_Mets()
    {
        
        // TODO: Validate result.Value.XDocument
    }
    
    
    
}