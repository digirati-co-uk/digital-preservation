using DigitalPreservation.Common.Model.AccessConditions;

namespace DigitalPreservation.Mets.AccessConditions;

/// <summary>
/// Where the installation's <see cref="AccessConditionProfile"/> comes from. The Preservation API
/// reads it from its own configuration; the UI, which writes METS through the same
/// <see cref="MetsManager"/>, fetches it from the API so that both agree; services with no access
/// vocabulary of their own (Pipeline API, Deposit Archiver) use <see cref="NullAccessConditionProfileProvider"/>.
/// </summary>
public interface IAccessConditionProfileProvider
{
    /// <summary>The profile, or null when this service has none (nothing is then derived on write).</summary>
    Task<AccessConditionProfile?> GetProfileAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// No profile: METS writes leave every typed access condition exactly as the caller set it.
/// </summary>
public class NullAccessConditionProfileProvider : IAccessConditionProfileProvider
{
    public Task<AccessConditionProfile?> GetProfileAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<AccessConditionProfile?>(null);
}
