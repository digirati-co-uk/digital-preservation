using DigitalPreservation.Common.Model.AccessConditions;
using DigitalPreservation.Mets.AccessConditions;
using Microsoft.Extensions.Options;

namespace Preservation.API.Features.AccessConditions;

public static class ServiceCollectionX
{
    /// <summary>
    /// Bind and validate the <c>AccessConditionProfile</c> section (issue #433) and make it the
    /// profile the METS writer derives from. Validation runs at startup so a matrix that names an
    /// unknown restriction value, rights group or derived value - or multi-select restrictions
    /// alongside derived conditions - stops the service rather than surfacing on the first edit.
    /// Must be called before <c>AddDepositWorkspace</c>, which otherwise installs the null provider.
    /// </summary>
    public static IServiceCollection AddAccessConditionProfile(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AccessConditionProfile>()
            .Bind(configuration.GetSection(AccessConditionProfile.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AccessConditionProfile>, AccessConditionProfileValidator>();
        services.AddSingleton<IAccessConditionProfileProvider, OptionsAccessConditionProfileProvider>();
        return services;
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
