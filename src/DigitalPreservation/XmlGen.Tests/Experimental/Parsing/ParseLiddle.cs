using DigitalPreservation.Common.Model.Transit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Storage.Repository.Common.Mets;
using Storage.Repository.Common.Mets.StorageImpl;

namespace XmlGen.Tests.Experimental.Parsing;

public class ParseLiddle
{
    
    private readonly MetsParser parser;
    
    public ParseLiddle()
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
    public async Task Can_Parse_Liddle()
    {
        var liddleMets = new FileInfo("Samples/liddle.mets.xml");
        var result = await parser.GetMetsFileWrapper(new Uri(liddleMets.FullName));
        
        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Self.Should().NotBeNull();
        result.Value.Self!.Digest.Should().NotBeEmpty();
        var phys = result.Value!.PhysicalStructure;
        phys!.Files.Should().Contain(f => f.Name == "liddle.mets.xml");
        
        result.Value.Name.Should().Be("Liddle Tapes 1 and 2");
        phys.Directories.Should().HaveCount(1);
        var objects = phys.Directories[0];
        objects.Name.Should().Be(FolderNames.Objects);
        objects.Files.Should().HaveCount(4);
    }
}