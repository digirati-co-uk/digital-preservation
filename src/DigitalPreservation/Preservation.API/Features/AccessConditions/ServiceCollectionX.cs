using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.AccessConditions;
using DigitalPreservation.Mets.AccessConditions;
using Microsoft.Extensions.Options;

namespace Preservation.API.Features.AccessConditions;

public static class ServiceCollectionX
{
    /// <summary>
    /// The configuration section that held the profile inline before it moved to its own document
    /// (issue #433). Still recognised, only to say where it went.
    /// </summary>
    public const string RetiredInlineSectionName = "AccessConditionProfile";

    /// <summary>
    /// Build the installation's <see cref="AccessConditionProfile"/> (issue #433) from whichever of
    /// the two configuration forms is present, validate it at startup, and make it the profile the
    /// METS writer derives from:
    /// <list type="bullet">
    /// <item><c>AccessConditions</c>: the standard form, a plain list of restriction values, with
    /// the optional <c>AllowMultipleAccessConditions</c> flag beside it. People choose a
    /// restriction and a rights statement.</item>
    /// <item><c>AccessConditionProfileFile</c>: the path (relative to the content root) of a
    /// profile document, for an installation with statements, IIIF rights and derived values
    /// (Leeds). The document is the same JSON <c>GET /access-conditions</c> serves.</item>
    /// </list>
    /// A profile document replaces the standard list: the base appsettings.json carries the standard
    /// list and an environment's appsettings.{env}.json names the document, and layered configuration
    /// cannot remove the base list. An unreadable or inconsistent profile stops the service. Must be
    /// called before <c>AddDepositWorkspace</c>, which otherwise installs the null provider.
    /// </summary>
    public static IServiceCollection AddAccessConditionProfile(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AccessConditionProfile>()
            .Configure<IHostEnvironment>((profile, environment) => ApplyConfiguration(profile, configuration, environment.ContentRootPath))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AccessConditionProfile>, AccessConditionProfileValidator>();
        services.AddSingleton<IAccessConditionProfileProvider, OptionsAccessConditionProfileProvider>();
        return services;
    }

    /// <summary>Fills a profile from configuration; see <see cref="AddAccessConditionProfile"/>.</summary>
    public static void ApplyConfiguration(AccessConditionProfile profile, IConfiguration configuration, string contentRoot)
    {
        if (configuration.GetSection(RetiredInlineSectionName).Exists())
        {
            throw new InvalidOperationException(
                $"The '{RetiredInlineSectionName}' configuration section is no longer read: the profile is now a JSON " +
                $"document named by '{AccessConditionProfile.FileKey}' (see docs/internals/access-condition-profile.md).");
        }

        var file = configuration[AccessConditionProfile.FileKey];
        if (!string.IsNullOrWhiteSpace(file))
        {
            CopyFrom(profile, Load(Path.Combine(contentRoot, file)));
            return;
        }
        profile.RestrictionAxis.Values = configuration.GetSection(AccessConditionProfile.SimpleSectionName).Get<List<AccessRestriction>>() ?? [];
        profile.AllowMultipleRestrictions = configuration.GetSection(AccessConditionProfile.AllowMultipleKey).Get<bool>();
    }

    private static AccessConditionProfile Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"{AccessConditionProfile.FileKey}: '{path}' does not exist.");
        }
        try
        {
            return AccessConditionProfile.FromJson(File.ReadAllText(path));
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidOperationException($"{AccessConditionProfile.FileKey}: '{path}' could not be read: {ex.Message}", ex);
        }
    }

    // The options system hands Configure an instance it created; the document's values go into it.
    private static void CopyFrom(AccessConditionProfile target, AccessConditionProfile source)
    {
        target.Name = source.Name;
        target.Version = source.Version;
        target.AllowMultipleRestrictions = source.AllowMultipleRestrictions;
        target.RestrictionAxis = source.RestrictionAxis;
        target.RightsAxis = source.RightsAxis;
        target.Statements = source.Statements;
    }
}

public class AccessConditionProfileValidator : IValidateOptions<AccessConditionProfile>
{
    public ValidateOptionsResult Validate(string? name, AccessConditionProfile options)
    {
        var errors = options.Validate();
        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors.Select(e => $"Access condition profile: {e}"));
    }
}

/// <summary>The Preservation API's profile is its own configuration.</summary>
public class OptionsAccessConditionProfileProvider(IOptions<AccessConditionProfile> profile) : IAccessConditionProfileProvider
{
    public Task<AccessConditionProfile?> GetProfileAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<AccessConditionProfile?>(profile.Value);
}
