using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.AccessConditions;
using DigitalPreservation.Mets.AccessConditions;
using Microsoft.Extensions.Options;

namespace Preservation.API.Features.AccessConditions;

public static class ServiceCollectionX
{
    /// <summary>
    /// Build the installation's <see cref="AccessConditionProfile"/> (issue #433) from whichever of
    /// the two configuration forms is present, validate it at startup, and make it the profile the
    /// METS writer derives from:
    /// <list type="bullet">
    /// <item><c>AccessConditions</c>: the standard form, a plain list of restriction values, with
    /// the optional <c>AllowMultipleAccessConditions</c> flag beside it.</item>
    /// <item><c>AccessConditionProfile</c>: the two-axis form, for an installation that derives
    /// something (the Leeds Access Tier) from its choices.</item>
    /// </list>
    /// Both present is a configuration error and stops the service, as does an inconsistent
    /// profile. Must be called before <c>AddDepositWorkspace</c>, which otherwise installs the null
    /// provider.
    /// </summary>
    public static IServiceCollection AddAccessConditionProfile(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AccessConditionProfile>()
            .Configure(profile => ApplyConfiguration(profile, configuration))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AccessConditionProfile>, AccessConditionProfileValidator>();
        services.AddSingleton<IAccessConditionProfileProvider, OptionsAccessConditionProfileProvider>();
        return services;
    }

    /// <summary>Fills a profile from configuration; see <see cref="AddAccessConditionProfile"/>.</summary>
    public static void ApplyConfiguration(AccessConditionProfile profile, IConfiguration configuration)
    {
        var simple = configuration.GetSection(AccessConditionProfile.SimpleSectionName);
        var full = configuration.GetSection(AccessConditionProfile.SectionName);
        var allowMultiple = configuration.GetSection(AccessConditionProfile.AllowMultipleKey);

        if (simple.Exists() && full.Exists())
        {
            throw new InvalidOperationException(
                $"Configure either '{AccessConditionProfile.SimpleSectionName}' (a list of access conditions) or " +
                $"'{AccessConditionProfile.SectionName}' (the two-axis form with derived conditions), not both.");
        }
        if (full.Exists())
        {
            if (allowMultiple.Exists())
            {
                throw new InvalidOperationException(
                    $"'{AccessConditionProfile.AllowMultipleKey}' belongs with '{AccessConditionProfile.SimpleSectionName}'; " +
                    $"in '{AccessConditionProfile.SectionName}' use AllowMultipleRestrictions (which is refused alongside Derived).");
            }
            full.Bind(profile);
            return;
        }
        profile.RestrictionAxis.Values = simple.Get<List<AccessRestriction>>() ?? [];
        profile.AllowMultipleRestrictions = allowMultiple.Get<bool>();
    }
}

public class AccessConditionProfileValidator : IValidateOptions<AccessConditionProfile>
{
    public ValidateOptionsResult Validate(string? name, AccessConditionProfile options)
    {
        var errors = options.Validate();
        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors.Select(e => $"{AccessConditionProfile.SectionName}: {e}"));
    }
}

/// <summary>The Preservation API's profile is its own configuration.</summary>
public class OptionsAccessConditionProfileProvider(IOptions<AccessConditionProfile> profile) : IAccessConditionProfileProvider
{
    public Task<AccessConditionProfile?> GetProfileAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<AccessConditionProfile?>(profile.Value);
}
