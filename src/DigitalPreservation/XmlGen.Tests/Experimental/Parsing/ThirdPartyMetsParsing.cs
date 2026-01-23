using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Storage.Repository.Common.Mets;
using Storage.Repository.Common.Mets.StorageImpl;

namespace XmlGen.Tests.Experimental;

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