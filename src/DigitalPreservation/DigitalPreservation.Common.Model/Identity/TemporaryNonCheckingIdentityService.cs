namespace DigitalPreservation.Common.Model.Identity;

/// <summary>
/// Until we integrate with Leeds we'll just use this, and assume that collisions are so unlikely that we won't ever see one.
///
/// THIS IS NOT FOR PRODUCTION!!!!!!!!
/// </summary>
public class TemporaryNonCheckingIdentityService : IIdentityService
{
    public string MintIdentity(string resourceType, Uri? equivalent = null)
    {
        return Identifiable.Generate(8, true);
    }
}