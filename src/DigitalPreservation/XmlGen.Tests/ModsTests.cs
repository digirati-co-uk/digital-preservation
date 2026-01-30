using DigitalPreservation.Common.Model.Transit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Storage.Repository.Common.Mets;
using Storage.Repository.Common.Mets.StorageImpl;

namespace XmlGen.Tests;

public class ModsTests
{
    private readonly MetsManager metsManager;
    private readonly MetsParser parser;
    
    public ModsTests()
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
    public void Build_Premis_Get_XmlElement()
    {
        var mods = ModsManager.Create("This is the name of the object");
        var xmlElement = ModsManager.GetXmlElement(mods);
        // testOutputHelper.WriteLine(xmlElement?.OuterXml);
    }
    
    [Fact]
    public async Task Can_Create_Empty_Mets_With_Mods()
    {
        var name = "Empty Mets File With MODS title";
        var emptyMetsFi = new FileInfo("Outputs/empty-mets-mods.xml");
        var metsUri = new Uri(emptyMetsFi.FullName);
        var result = await metsManager.CreateStandardMets(new Uri(emptyMetsFi.FullName), name);

        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNull();

        result.Value!.PhysicalStructure!.Files.Should().HaveCount(1);
        result.Value.PhysicalStructure.Files.Should().Contain(wd => wd.Name == "empty-mets-mods.xml");
        
        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();
        var updatedWrapper = parseResult.Value!;
        
        updatedWrapper.Name.Should().Be(name);
        
    }
    
    [Fact]
    public async Task Can_Create_Access_Control_Entry()
    {
        var name = "Empty Mets File With MODS title for access Control";
        var metsFi = new FileInfo("Outputs/empty-mets-mods-with-access.xml");
        var metsUri = new Uri(metsFi.FullName);
        var result = await metsManager.CreateStandardMets(new Uri(metsFi.FullName), name);

        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNull();
        var metsWrapper = result.Value!;
        var metsResult = await metsManager.GetFullMets(metsUri, metsWrapper.ETag!);
        var mets = metsResult.Value;
        mets.Should().NotBeNull();

        var accessRestrictions = metsManager.GetRootAccessRestrictions(mets!);
        accessRestrictions.Should().HaveCount(0);
        metsManager.SetRootAccessRestrictions(mets!, [ "my-access-restriction" ]);
        accessRestrictions = metsManager.GetRootAccessRestrictions(mets!);
        accessRestrictions.Should().HaveCount(1);
        accessRestrictions[0].Should().Be("my-access-restriction");
        
        await metsManager.WriteMets(mets!);
        
        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();
        var updatedWrapper = parseResult.Value!;
        
        updatedWrapper.Name.Should().Be(name);
        updatedWrapper.RootAccessConditions.Should().HaveCount(1);
        updatedWrapper.RootAccessConditions[0].Should().Be("my-access-restriction");
        
    }
    
    
    [Fact]
    public async Task Can_Create_Rights_Statement()
    {
        var name = "Empty Mets File With MODS title for rights statement";
        var metsFi = new FileInfo("Outputs/empty-mets-mods-with-rights.xml");
        var metsUri = new Uri(metsFi.FullName);
        var result = await metsManager.CreateStandardMets(new Uri(metsFi.FullName), name);
        
        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNull();
        var metsWrapper = result.Value!;
        var metsResult = await metsManager.GetFullMets(metsUri, metsWrapper.ETag!);
        var mets = metsResult.Value;
        mets.Should().NotBeNull();

        var rightsStatement = metsManager.GetRootRightsStatement(mets!);
        rightsStatement.Should().BeNull();
        metsManager.SetRootRightsStatement(mets!, new Uri("https://rightsstatements.org/vocab/NoC-NC/1.0/"));
        rightsStatement = metsManager.GetRootRightsStatement(mets!);
        rightsStatement.Should().NotBeNull();
        rightsStatement.Should().Be("https://rightsstatements.org/vocab/NoC-NC/1.0/");
        
        await metsManager.WriteMets(mets!);
        
        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();
        var updatedWrapper = parseResult.Value!;
        
        updatedWrapper.Name.Should().Be(name);
        updatedWrapper.RootRightsStatement.Should().NotBeNull();
        updatedWrapper.RootRightsStatement.Should().Be("https://rightsstatements.org/vocab/NoC-NC/1.0/");
        
    }
    
    
    [Fact(Skip = "To be implemented in phase 2")]
    public async Task Can_Update_File_Level_MODS()
    {
        var metsFi = new FileInfo("Outputs/mets-with-file-level-mods.xml");
        var metsUri = new Uri(metsFi.FullName);
        var result = await metsManager.CreateStandardMets(
            metsUri, "Empty Mets File - For file testing");
        
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
            Modified = DateTime.UtcNow,
            AccessRestrictions = [ "my-access-restriction" ],
            RightsStatement = new Uri("https://rightsstatements.org/vocab/NoC-NC/1.0/")
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
        objectsDir.Files[0].AccessRestrictions.Should().HaveCount(1);
        objectsDir.Files[0].AccessRestrictions[0].Should().Be("my-access-restriction");
        objectsDir.Files[0].RightsStatement.Should().Be("https://rightsstatements.org/vocab/NoC-NC/1.0/");
        
    }
}