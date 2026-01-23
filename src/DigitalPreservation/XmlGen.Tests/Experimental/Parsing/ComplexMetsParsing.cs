using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Storage.Repository.Common.Mets;
using Storage.Repository.Common.Mets.StorageImpl;

namespace XmlGen.Tests.Experimental;

public class ComplexMetsParsing
{
    private readonly MetsParser parser;
    
    public ComplexMetsParsing()
    {
        var serviceProvider = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        var factory = serviceProvider.GetService<ILoggerFactory>();
        var parserLogger = factory!.CreateLogger<MetsParser>();
        var metsLoader = new FileSystemMetsLoader();
        parser = new MetsParser(metsLoader, parserLogger);
    }

    [Fact]
    public async Task Can_Parse_Women_Of_Westminster()
    {
        var wowMets = new FileInfo("Samples/wow.mets.xml");
        var result = await parser.GetMetsFileWrapper(new Uri(wowMets.FullName));
        
        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Self.Should().NotBeNull();
        result.Value.Self!.Digest.Should().NotBeEmpty();
        var phys = result.Value!.PhysicalStructure;
        phys!.Files.Should().Contain(f => f.Name == "wow.mets.xml");
        
        
        result.Value.Name.Should().Be("[Example title]");
        phys.Directories.Should().HaveCount(1);
        var objects = phys.Directories[0];
        objects.Name.Should().Be(FolderNames.Objects);
        objects.Files.Should().HaveCount(5);
        objects.AccessRestrictions.Should().HaveCount(1);
        objects.AccessRestrictions[0].Should().Be("Level1");
        objects.EffectiveAccessRestrictions.Should().HaveCount(1);
        objects.EffectiveAccessRestrictions[0].Should().Be("Level1");
        var inCopyright = new Uri("http://rightsstatements.org/vocab/InC/1.0/");
        objects.RightsStatement.Should().Be(inCopyright);
        objects.EffectiveRightsStatement.Should().Be(inCopyright);
        objects.RecordInfo.Should().NotBeNull();
        objects.RecordInfo!.RecordIdentifiers.Should().HaveCount(2);
        objects.RecordInfo.RecordIdentifiers[0].Source.Should().Be("identity-service");
        objects.RecordInfo.RecordIdentifiers[0].Value.Should().Be("b6n9e4c2");
        objects.RecordInfo.RecordIdentifiers[1].Source.Should().Be("EMu");
        objects.RecordInfo.RecordIdentifiers[1].Value.Should().Be("MS 2249");
        
        
        // physical files
        objects.Files[0].LocalPath.Should().Be("objects/amber-rudd.m4a");
        objects.Files[0].ContentType.Should().Be("audio/m4a");
        objects.Files[1].LocalPath.Should().Be("objects/amber-rudd.docx");
        objects.Files[1].ContentType.Should().Be("application/msword");
        objects.Files[1].Metadata.OfType<FileFormatMetadata>().Single().PronomKey.Should().Be("fmt/200");
        objects.Files[1].Metadata.OfType<FileFormatMetadata>().Single().FormatName.Should().Be("Microsoft Word");
        objects.Files[2].LocalPath.Should().Be("objects/angela-eagle.m4a");
        objects.Files[3].LocalPath.Should().Be("objects/angela-eagle-redacted.m4a");
        objects.Files[4].LocalPath.Should().Be("objects/angela-eagle-transcript.docx");

        // links
        var supplementing = new Uri("http://iiif.io/api/presentation/3#supplementing");
        objects.Files[0].Links.Should().HaveCount(1);
        objects.Files[0].Links[0].To.Should().Be("objects/amber-rudd.docx");
        objects.Files[0].Links[0].Role.Should().Be(supplementing);
        objects.Files[1].Links.Should().HaveCount(0);
        objects.Files[2].Links.Should().HaveCount(0);
        objects.Files[3].Links[0].To.Should().Be("objects/angela-eagle-transcript.docx");
        objects.Files[3].Links[0].Role.Should().Be(supplementing);
        objects.Files[4].Links.Should().HaveCount(0);

        // logical structmap

        // effective rights on files





    }
}