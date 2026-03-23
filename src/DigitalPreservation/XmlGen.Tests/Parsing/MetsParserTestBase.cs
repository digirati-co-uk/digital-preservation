using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Xml.Linq;

namespace XmlGen.Tests.Parsing;

public abstract class MetsParserTestBase
{
    protected readonly MetsParser Parser;

    protected static readonly Uri TestMetsUri = new("file:///test/deposit/test.mets.xml");

    protected MetsParserTestBase()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        Parser = new MetsParser(
            new FileSystemMetsLoader(),
            services.GetRequiredService<ILoggerFactory>().CreateLogger<MetsParser>());
    }

    protected MetsFileWrapper Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var result = Parser.GetMetsFileWrapperFromXDocument(TestMetsUri, doc);
        result.Success.Should().BeTrue();
        return result.Value!;
    }
}
