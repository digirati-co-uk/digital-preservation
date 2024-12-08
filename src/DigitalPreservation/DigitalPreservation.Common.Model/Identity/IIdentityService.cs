namespace DigitalPreservation.Common.Model.Identity;

public interface IIdentityService
{
    /// <summary>
    /// This is a guess at the Leeds identity service.
    ///
    /// It would need to mint distinct identities for Deposits for the same object,
    /// but a single repeated identity for the ArchivalGroup for that object.
    ///
    /// And keep track...
    /// </summary>
    /// <param name="resourceType">For what kind of thing are we asking for an identity?</param>
    /// <param name="equivalent">The URI of </param>
    /// <returns></returns>
    string MintIdentity(string resourceType, Uri? equivalent = null);
}