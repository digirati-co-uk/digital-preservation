using DigitalPreservation.Common.Model.AccessConditions;
using DigitalPreservation.Mets.AccessConditions;
using Preservation.Client;

namespace DigitalPreservation.UI.Infrastructure;

/// <summary>
/// The UI writes METS through the same <c>MetsManager</c> as the Preservation API, so it must
/// derive access conditions from the SAME profile (issue #433). It has no copy of its own: it
/// fetches the API's, which the client caches briefly. If the API cannot be reached the last
/// profile this process fetched successfully is used; if there has never been one, the write is
/// refused (<see cref="AccessConditionProfileUnavailableException"/>) rather than allowed to go
/// ahead with stale derived values beside freshly changed chosen ones.
/// </summary>
public class UiAccessConditionProfileProvider(
    IPreservationApiClient preservationApiClient,
    ILogger<UiAccessConditionProfileProvider> logger) : IAccessConditionProfileProvider
{
    // The provider is registered as a singleton, so this lives as long as the process: a profile
    // changes only when the API is redeployed, so the last one fetched is a sound fallback.
    private AccessConditionProfile? lastKnownGood;

    public async Task<AccessConditionProfile?> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        var result = await preservationApiClient.GetAccessConditions(cancellationToken);
        if (result is { Success: true, Value: not null })
        {
            lastKnownGood = result.Value;
            return result.Value;
        }

        var fallback = lastKnownGood;
        if (fallback != null)
        {
            logger.LogWarning(
                "Could not fetch the access condition profile from the Preservation API ({Error}); using the profile fetched earlier in this process",
                result.ErrorMessage);
            return fallback;
        }

        logger.LogError(
            "Could not fetch the access condition profile from the Preservation API ({Error}) and none was fetched earlier; refusing to write METS",
            result.ErrorMessage);
        throw new AccessConditionProfileUnavailableException(
            $"The Preservation API's access condition profile could not be fetched: {result.ErrorMessage}");
    }
}
