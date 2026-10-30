using DigitalPreservation.Common.Model.AccessConditions;
using DigitalPreservation.Mets.AccessConditions;
using Preservation.Client;

namespace DigitalPreservation.UI.Infrastructure;

/// <summary>
/// The UI writes METS through the same <c>MetsManager</c> as the Preservation API, so it must
/// derive access conditions from the SAME profile (issue #433). It has no copy of its own: it
/// fetches the API's, which the client caches briefly. If the API cannot be reached the write goes
/// ahead with nothing derived rather than failing - the next successful write recomputes the whole
/// document anyway.
/// </summary>
public class UiAccessConditionProfileProvider(
    IPreservationApiClient preservationApiClient,
    ILogger<UiAccessConditionProfileProvider> logger) : IAccessConditionProfileProvider
{
    public async Task<AccessConditionProfile?> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        var result = await preservationApiClient.GetAccessConditions(cancellationToken);
        if (result.Success) return result.Value;
        logger.LogWarning("Could not fetch the access condition profile from the Preservation API ({Error}); nothing will be derived on this write",
            result.ErrorMessage);
        return null;
    }
}
