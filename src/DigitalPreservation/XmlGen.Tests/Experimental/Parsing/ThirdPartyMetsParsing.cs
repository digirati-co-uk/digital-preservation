using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XmlGen.Tests.Experimental.Parsing;

public class ThirdPartyMetsParsing
{
    private readonly MetsParser parser;
    
    public ThirdPartyMetsParsing()
    {
        var serviceProvider = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        var factory = serviceProvider.GetService<ILoggerFactory>();
        var parserLogger = factory!.CreateLogger<MetsParser>();
        var metsLoader = new FileSystemMetsLoader();
        parser = new MetsParser(metsLoader, parserLogger);
    }
    
}