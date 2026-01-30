using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.Transit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Storage.Repository.Common.Mets;
using Storage.Repository.Common.Mets.StorageImpl;

namespace XmlGen.Tests;

public class MetsManagerWithPremis
{
    private readonly MetsManager metsManager;
    private readonly MetsParser parser;

    public MetsManagerWithPremis()
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
        metsManager = new MetsManager(parser, metsStorage, metadataManager, premisManager, premisEventManager);
    }
    
    
    [Fact]
    public async Task Can_Update_Premis()
    {
        var premisMetsFi = new FileInfo("Outputs/premis-mets.xml");
        var metsUri = new Uri(premisMetsFi.FullName);
        var result = await metsManager.CreateStandardMets(
            metsUri, "Empty Mets File - Premis tests");
        
        result.Success.Should().BeTrue();
        var metsWrapper = result.Value!;
        metsWrapper.PhysicalStructure!.Directories.Should().HaveCount(2);
        metsWrapper.PhysicalStructure.Directories.Should().Contain(wd => wd.Name == FolderNames.Objects);
        metsWrapper.PhysicalStructure.Directories.Should().Contain(wd => wd.Name == FolderNames.Metadata);
        
        var file = new WorkingFile
        {
            ContentType = "text/plain",
            Digest = "801d4a031510adb61ae11412c1554fbaa769a6b4428225ad87a489f92889f105",
            LocalPath = "objects/readme.txt",
            Size = 9999,
            Name = "readme.txt",
            Modified = DateTime.UtcNow
        };
        var addResult = await metsManager.HandleSingleFileUpload(metsUri, file, metsWrapper.ETag!);
        addResult.Success.Should().BeTrue();
        
        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();

        var updatedWrapper = parseResult.Value!;
        var objectsDir = updatedWrapper.PhysicalStructure!.Directories.Single(d => d.Name == FolderNames.Objects);
        objectsDir.Directories.Should().HaveCount(0);
        objectsDir.Files.Should().HaveCount(1);
        objectsDir.Files[0].Name.Should().Be("readme.txt");
        objectsDir.Files[0].LocalPath.Should().Be("objects/readme.txt");
        objectsDir.Files[0].Size.Should().Be(9999);
        objectsDir.Files[0].ContentType.Should().Be("text/plain");
        objectsDir.Files[0].Digest.Should().Be("801d4a031510adb61ae11412c1554fbaa769a6b4428225ad87a489f92889f105");
        
        // TODO: Validate result.Value.XDocument
    }
}