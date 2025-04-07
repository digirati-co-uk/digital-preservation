namespace DigitalPreservation.Common.Model.Identity;

public class IdentityMinter : IIdentityMinter
{
    public string MintIdentity(string resourceType, Uri? equivalent = null)
    {
        return Identifiable.Generate(12, true);
    }
}