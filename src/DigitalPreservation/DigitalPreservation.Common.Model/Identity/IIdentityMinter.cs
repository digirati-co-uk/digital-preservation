namespace DigitalPreservation.Common.Model.Identity;

public interface IIdentityMinter
{
    string MintIdentity(string resourceType, Uri? equivalent = null);
}