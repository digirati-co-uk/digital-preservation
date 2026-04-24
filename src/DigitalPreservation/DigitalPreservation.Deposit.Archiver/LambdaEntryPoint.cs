using Microsoft.AspNetCore.Hosting;

namespace DigitalPreservation.Deposit.Archiver;

public class LambdaEntryPoint : Amazon.Lambda.AspNetCoreServer.APIGatewayProxyFunction
{
    protected override void Init(IWebHostBuilder builder)
    {
        builder.UseStartup<Startup>();
    }
}