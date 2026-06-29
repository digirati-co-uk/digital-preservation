using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalPreservation.Core.Auth;

public static class ClientDirectoryServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="IClientDirectory"/> bound from the <c>KnownClients</c> configuration
    /// section (<c>{ "&lt;appId&gt;": { "name": "...", "depositBucket": "..." } }</c>). A missing or
    /// empty section yields an empty directory, in which case every machine caller falls through to
    /// the <c>X-Client-Identity</c> header (see <see cref="AuthFilterIdentifier"/>).
    /// </summary>
    public static IServiceCollection AddClientDirectory(this IServiceCollection services, IConfiguration configuration)
    {
        var clients = configuration.GetSection("KnownClients").Get<Dictionary<string, ClientProfile>>()
                      ?? new Dictionary<string, ClientProfile>();

        services.AddSingleton<IClientDirectory>(new ClientDirectory(clients));
        return services;
    }
}
