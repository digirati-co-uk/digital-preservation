using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalPreservation.Core.Auth;

public static class ClientDirectoryServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="IClientDirectory"/> bound from the <c>KnownClients</c> configuration
    /// section (<c>{ "&lt;appId&gt;": { "name": "...", "depositBucket": "..." } }</c>). A missing or
    /// empty section yields an empty directory, in which case every machine caller falls through to
    /// the <c>X-Client-Identity</c> header (see <see cref="AuthFilterIdentifier"/>). A malformed
    /// entry fails at startup: the binder cannot enforce <see cref="ClientProfile"/>'s
    /// <c>required Name</c>, so a flat <c>"appId": "name"</c> entry or an object without a
    /// <c>name</c> would otherwise silently bind to a nameless profile.
    /// </summary>
    public static IServiceCollection AddClientDirectory(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("KnownClients");

        // A child with a scalar Value is the flat string form, which the binder would silently drop.
        var flatEntries = section.GetChildren().Where(c => c.Value != null).Select(c => c.Key).ToList();
        if (flatEntries.Count > 0)
        {
            throw new InvalidOperationException(
                "KnownClients entries must be objects, e.g. \"<appId>\": { \"name\": \"...\" }. " +
                $"Flat string entries found for: {string.Join(", ", flatEntries)}");
        }

        var clients = section.Get<Dictionary<string, ClientProfile>>()
                      ?? new Dictionary<string, ClientProfile>();

        var nameless = clients.Where(kvp => string.IsNullOrWhiteSpace(kvp.Value?.Name))
            .Select(kvp => kvp.Key).ToList();
        if (nameless.Count > 0)
        {
            throw new InvalidOperationException(
                $"KnownClients entries with missing or empty \"name\": {string.Join(", ", nameless)}");
        }

        services.AddSingleton<IClientDirectory>(new ClientDirectory(clients));
        return services;
    }
}
