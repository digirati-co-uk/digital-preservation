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
        objectsDir.Directories.Should().HaveCount(1);
        
        objectsDir.Files[0].Name.Should().Be("Minutes LAQM 21 June 2020.pdf");
        objectsDir.Files[0].LocalPath.Should().Be("objects/minutes-laqm-21-june-2020.pdf");
        objectsDir.Files[0].Digest.Should().Be("eb634d64ce8e6be5195174ceaef9ac9e19c37119f3b31618630aa633ccdbf68f");
        objectsDir.Files[1].Name.Should().Be("MINUTES LAQM 8 Sept 2020.pdf");
        objectsDir.Files[1].LocalPath.Should().Be("objects/minutes-laqm-8-sept-2020.pdf");
        objectsDir.Files[1].Digest.Should().Be("9c5aa04a39812c80bcc824e366044c16fb090efb076c8552ca9ca932f0dfc981");
        
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
        var emptyMetsFi = new FileInfo("Outputs/empty-mets-add-dirs.xml");
        var metsUri = new Uri(emptyMetsFi.FullName);
        var result = await metsManager.CreateStandardMets(
            metsUri, "Empty Mets File - Add Directories");
        
        result.Success.Should().BeTrue();
        var metsWrapper = result.Value!;
        metsWrapper.PhysicalStructure!.Directories.Should().HaveCount(1);
        metsWrapper.PhysicalStructure.Directories.Should().Contain(wd => wd.Name == "objects");
        
        var dir = new WorkingDirectory
        {
            LocalPath = "objects/child-dir",
            Name = "Child Directory",
            Modified = DateTime.UtcNow
        };
        var addResult = await metsManager.HandleCreateFolder(metsUri, dir, metsWrapper.ETag!);
        addResult.Success.Should().BeTrue();
        
        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();

        var updatedWrapper = parseResult.Value!;
        updatedWrapper.PhysicalStructure!.Directories[0].Directories.Should().HaveCount(1);
        updatedWrapper.PhysicalStructure.Directories[0].Directories[0].Name.Should().Be("Child Directory");
        updatedWrapper.PhysicalStructure.Directories[0].Directories[0].LocalPath.Should().Be("objects/child-dir");
        
        // TODO: Validate result.Value.XDocument
    }
    
    // [Fact(Skip = "Experimental")]
    [Fact]
    public async Task Can_Add_Directories_To_ArchivalGroup_Mets()
    {
        var agFi = new FileInfo("Samples/archivalGroup.json");
        var agMetsFi = new FileInfo("Outputs/archivalGroup-mets-add-dirs.xml");
        var agMetsUri = new Uri(agMetsFi.FullName);
        
        var archivalGroup = JsonSerializer.Deserialize<ArchivalGroup>(await File.ReadAllTextAsync(agFi.FullName));
        var result = await metsManager.CreateStandardMets(agMetsUri, archivalGroup!, 
            "ArchivalGroup Mets File - add folders");
        
        result.Success.Should().BeTrue();
        var metsWrapper = result.Value!;
        
        var dir = new WorkingDirectory
        {
            LocalPath = "objects/child-dir",
            Name = "Child Directory",
            Modified = DateTime.UtcNow
        };
        var addResult = await metsManager.HandleCreateFolder(agMetsUri, dir, metsWrapper.ETag!);
        addResult.Success.Should().BeTrue();
        
        var parseResult = await parser.GetMetsFileWrapper(agMetsUri);
        parseResult.Success.Should().BeTrue();

        var updatedWrapper = parseResult.Value!;
        var objectsDir = updatedWrapper.PhysicalStructure!.Directories[0];
        objectsDir.Directories.Should().HaveCount(2);
        objectsDir.Files.Should().HaveCount(2); // no changes here yet
        
        objectsDir.Files[0].Name.Should().Be("Minutes LAQM 21 June 2020.pdf");
        objectsDir.Files[0].LocalPath.Should().Be("objects/minutes-laqm-21-june-2020.pdf");
        objectsDir.Files[0].Digest.Should().Be("eb634d64ce8e6be5195174ceaef9ac9e19c37119f3b31618630aa633ccdbf68f");
        objectsDir.Files[1].Name.Should().Be("MINUTES LAQM 8 Sept 2020.pdf");
        objectsDir.Files[1].LocalPath.Should().Be("objects/minutes-laqm-8-sept-2020.pdf");
        objectsDir.Files[1].Digest.Should().Be("9c5aa04a39812c80bcc824e366044c16fb090efb076c8552ca9ca932f0dfc981");
        
        // But we do have one new directory
        // We expect case-insensitive alphabetical order
        objectsDir.Directories[0].Name.Should().Be("Child Directory");
        objectsDir.Directories[0].LocalPath.Should().Be("objects/child-dir");
        var folderB = objectsDir.Directories[1]; // now in second position
        folderB.Name.Should().Be("folder b");
        folderB.LocalPath.Should().Be("objects/folder-b");
        folderB.Files.Should().HaveCount(0);
        folderB.Directories.Should().HaveCount(1);
        
        // And this should be the same
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
        
        // Now let's make some further folder changes
        var dir2 = new WorkingDirectory
        {
            LocalPath = "objects/folder-b/z-child-of-b",
            Name = "Z Child of Directory B",
            Modified = DateTime.UtcNow
        };
        var addResult2 = await metsManager.HandleCreateFolder(agMetsUri, dir2, updatedWrapper.ETag!);
        addResult2.Success.Should().BeTrue();
        
        var parseResult2 = await parser.GetMetsFileWrapper(agMetsUri);
        parseResult2.Success.Should().BeTrue();
        
        var updatedWrapper2 = parseResult2.Value!;
        objectsDir = updatedWrapper2.PhysicalStructure!.Directories[0];
        folderB = objectsDir.Directories[1]; // now in second position
        folderB.Name.Should().Be("folder b");
        folderB.Directories.Should().HaveCount(2); 
        folderB.Directories[0].Name.Should().Be("folder bb");
        folderB.Directories[0].LocalPath.Should().Be("objects/folder-b/folder-bb");
        folderB.Directories[1].Name.Should().Be("Z Child of Directory B");
        folderB.Directories[1].LocalPath.Should().Be("objects/folder-b/z-child-of-b");
        
        // And another folder
        var dir3 = new WorkingDirectory
        {
            LocalPath = "objects/folder-b/folder-bb/child-of-bb",
            Name = "Child of Directory BB",
            Modified = DateTime.UtcNow
        };
        var addResult3 = await metsManager.HandleCreateFolder(agMetsUri, dir3, updatedWrapper2.ETag!);
        addResult3.Success.Should().BeTrue();
        
        var parseResult3 = await parser.GetMetsFileWrapper(agMetsUri);
        parseResult3.Success.Should().BeTrue();
        
        var updatedWrapper3 = parseResult3.Value!;
        objectsDir = updatedWrapper3.PhysicalStructure!.Directories[0];
        folderB = objectsDir.Directories[1]; // still in second position
        folderB.Name.Should().Be("folder b");
        folderB.Directories.Should().HaveCount(2); 
        folderBB = folderB.Directories[0];
        folderBB.Name.Should().Be("folder bb");
        folderBB.LocalPath.Should().Be("objects/folder-b/folder-bb");
        folderB.Directories[1].Name.Should().Be("Z Child of Directory B");
        folderB.Directories[1].LocalPath.Should().Be("objects/folder-b/z-child-of-b");

        // Now a child dir
        folderBB.Directories.Should().HaveCount(1);
        folderBB.Directories[0].Name.Should().Be("Child of Directory BB");
        folderBB.Directories[0].LocalPath.Should().Be("objects/folder-b/folder-bb/child-of-bb");
    }
    
    // [Fact(Skip = "Experimental")]
    [Fact]
    public async Task Can_Add_Files_To_Empty_Mets()
    {
        var emptyMetsFi = new FileInfo("Outputs/empty-mets-add-files.xml");
        var metsUri = new Uri(emptyMetsFi.FullName);
        var result = await metsManager.CreateStandardMets(
            metsUri, "Empty Mets File - Add Files");
        
        result.Success.Should().BeTrue();
        var metsWrapper = result.Value!;
        metsWrapper.PhysicalStructure!.Directories.Should().HaveCount(1);
        metsWrapper.PhysicalStructure.Directories.Should().Contain(wd => wd.Name == "objects");
        
        var file = new WorkingFile
        {
            ContentType = "text/plain",
            Digest = "801d4a031510adb61ae11412c1554fbaa769a6b4428225ad87a489f92889f105",
            LocalPath = "objects/readme.txt",
            Name = "readme.txt",
            Modified = DateTime.UtcNow
        };
        var addResult = await metsManager.HandleSingleFileUpload(metsUri, file, metsWrapper.ETag!);
        addResult.Success.Should().BeTrue();
        
        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();

        var updatedWrapper = parseResult.Value!;
        var objectsDir = updatedWrapper.PhysicalStructure!.Directories[0];
        objectsDir.Directories.Should().HaveCount(0);
        objectsDir.Files.Should().HaveCount(1);
        objectsDir.Files[0].Name.Should().Be("readme.txt");
        objectsDir.Files[0].LocalPath.Should().Be("objects/readme.txt");
        objectsDir.Files[0].ContentType.Should().Be("text/plain");
        objectsDir.Files[0].Digest.Should().Be("801d4a031510adb61ae11412c1554fbaa769a6b4428225ad87a489f92889f105");
        
        // TODO: Validate result.Value.XDocument
    }
    
    // [Fact(Skip = "Experimental")]
    [Fact]
    public async Task Can_Add_Files_To_ArchivalGroup_Mets()
    {
        var agFi = new FileInfo("Samples/archivalGroup.json");
        var agMetsFi = new FileInfo("Outputs/archivalGroup-mets-add-files.xml");
        var agMetsUri = new Uri(agMetsFi.FullName);
        
        var archivalGroup = JsonSerializer.Deserialize<ArchivalGroup>(await File.ReadAllTextAsync(agFi.FullName));
        var result = await metsManager.CreateStandardMets(agMetsUri, archivalGroup!, 
            "ArchivalGroup Mets File - add folders");
        
        result.Success.Should().BeTrue();
        var metsWrapper = result.Value!;
        
        var file = new WorkingFile
        {
            ContentType = "text/plain",
            Digest = "801d4a031510adb61ae11412c1554fbaa769a6b4428225ad87a489f92889f105",
            LocalPath = "objects/readme.txt",
            Name = "readme.txt",
            Modified = DateTime.UtcNow
        };
        var addResult = await metsManager.HandleSingleFileUpload(agMetsUri, file, metsWrapper.ETag!);
        addResult.Success.Should().BeTrue();
        
        var parseResult = await parser.GetMetsFileWrapper(agMetsUri);
        parseResult.Success.Should().BeTrue();

        var updatedWrapper = parseResult.Value!;
        var objectsDir = updatedWrapper.PhysicalStructure!.Directories[0];
        objectsDir.Directories.Should().HaveCount(1);
        objectsDir.Files.Should().HaveCount(3); // an extra file, should be last because "r"
        objectsDir.Files[2].Name.Should().Be("readme.txt");
        objectsDir.Files[2].LocalPath.Should().Be("objects/readme.txt");
        objectsDir.Files[2].ContentType.Should().Be("text/plain");
        objectsDir.Files[2].Digest.Should().Be("801d4a031510adb61ae11412c1554fbaa769a6b4428225ad87a489f92889f105");

        var file2 = new WorkingFile
        {
            ContentType = "text/plain",
            Digest = "801d4a031510adb61ae11412c1554fbaa769a6b4428225ad87a489f92889f105",
            LocalPath = "objects/folder-b/folder-bb/readme.txt",
            Name = "README",
            Modified = DateTime.UtcNow
        };
        var addResult2 = await metsManager.HandleSingleFileUpload(agMetsUri, file2, updatedWrapper.ETag!);
        addResult2.Success.Should().BeTrue();
        
        var parseResult2 = await parser.GetMetsFileWrapper(agMetsUri);
        parseResult2.Success.Should().BeTrue();

        var updatedWrapper2 = parseResult2.Value!;
        objectsDir = updatedWrapper2.PhysicalStructure!.Directories[0];
        var folderBB = objectsDir.Directories[0].Directories[0];
        folderBB.Files.Should().HaveCount(2);
        folderBB.Files[1].Name.Should().Be("README");
        folderBB.Files[1].LocalPath.Should().Be("objects/folder-b/folder-bb/readme.txt");
        folderBB.Files[1].ContentType.Should().Be("text/plain");
        folderBB.Files[1].Digest.Should().Be("801d4a031510adb61ae11412c1554fbaa769a6b4428225ad87a489f92889f105");

        // TODO: Validate result.Value.XDocument
    }
    
    
    // [Fact(Skip = "Experimental")]
    [Fact]
    public async Task Can_Delete_Files_From_Mets()
    {
        var agFi = new FileInfo("Samples/archivalGroup.json");
        var agMetsFi = new FileInfo("Outputs/archivalGroup-mets-delete-files.xml");
        var agMetsUri = new Uri(agMetsFi.FullName);
        
        var archivalGroup = JsonSerializer.Deserialize<ArchivalGroup>(await File.ReadAllTextAsync(agFi.FullName));
        var result = await metsManager.CreateStandardMets(agMetsUri, archivalGroup!, "ArchivalGroup Mets File Deletion");

        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNull();

        var fileToDelete = "objects/minutes-laqm-21-june-2020.pdf";
        var deleteResult = await metsManager.HandleDeleteObject(agMetsUri, fileToDelete, result.Value!.ETag!);
        deleteResult.Success.Should().BeTrue();
        
        var parseResult = await parser.GetMetsFileWrapper(agMetsUri);
        parseResult.Success.Should().BeTrue();
        
        var updatedWrapper = parseResult.Value!;
        var objectsDir = updatedWrapper.PhysicalStructure!.Directories[0];
        objectsDir.Directories.Should().HaveCount(1);
        objectsDir.Files.Should().HaveCount(1); // ONE LESS FILE
        objectsDir.Files[0].LocalPath.Should().Be("objects/minutes-laqm-8-sept-2020.pdf");
        
        // TODO: Validate result.Value.XDocument
        // Need to verify that fileSec and ADMSec have been updated
    }
    
    
    // [Fact(Skip = "Experimental")]
    [Fact]
    public async Task Can_Delete_Directories_From_Mets()
    {
        var agFi = new FileInfo("Samples/archivalGroup.json");
        var agMetsFi = new FileInfo("Outputs/archivalGroup-mets-delete-dirs.xml");
        var agMetsUri = new Uri(agMetsFi.FullName);
        
        var archivalGroup = JsonSerializer.Deserialize<ArchivalGroup>(await File.ReadAllTextAsync(agFi.FullName));
        var result = await metsManager.CreateStandardMets(agMetsUri, archivalGroup!, "ArchivalGroup Mets Directory Deletion");

        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNull();
        
        // Try to delete the folder straight away:
        var folderToDelete = "objects/folder-b/folder-bb";
        var deleteFolderResult = await metsManager.HandleDeleteObject(agMetsUri, folderToDelete, result.Value!.ETag!);
        deleteFolderResult.Success.Should().BeFalse();
        deleteFolderResult.ErrorCode.Should().Be(ErrorCodes.BadRequest);
        deleteFolderResult.ErrorMessage.Should().Be("Cannot delete a non-empty directory.");

        // Can't do that! need to delete its contents first
        var fileToDelete = "objects/folder-b/folder-bb/minutes-laqm-22-april-2020.pdf";
        var deleteFileResult = await metsManager.HandleDeleteObject(agMetsUri, fileToDelete, result.Value!.ETag!);
        deleteFileResult.Success.Should().BeTrue();
        
        var parseResult = await parser.GetMetsFileWrapper(agMetsUri);
        parseResult.Success.Should().BeTrue();
        
        var updatedWrapper = parseResult.Value!;
        var objectsDir = updatedWrapper.PhysicalStructure!.Directories[0];
        objectsDir.Directories.Should().HaveCount(1);
        objectsDir.Directories[0].Directories[0].Files.Should().HaveCount(0);
        
        // Now try to delete the folder again
        var deleteFolderResult2 = await metsManager.HandleDeleteObject(agMetsUri, folderToDelete, parseResult.Value!.ETag!);
        deleteFolderResult2.Success.Should().BeTrue();
        
        var parseResult2 = await parser.GetMetsFileWrapper(agMetsUri);
        parseResult2.Success.Should().BeTrue();
        var updatedWrapper2 = parseResult2.Value!;
        
        objectsDir = updatedWrapper2.PhysicalStructure!.Directories[0];
        objectsDir.Directories.Should().HaveCount(1);
        objectsDir.Directories[0].Directories.Should().HaveCount(0);
        
        

        // TODO: Validate result.Value.XDocument
        // Need to verify that fileSec and ADMSec have been updated
    }
    
    
    
}