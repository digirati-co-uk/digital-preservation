using DigitalPreservation.Common.Model.Results;

namespace LeedsDlipServices.Identity;

public interface IIdentityService
{
    /// <summary>
    /// This is for unique local IDs
    /// </summary>
    /// <param name="resourceType">For what kind of thing are we asking for an identity?</param>
    /// <param name="equivalent">The URI of </param>
    /// <returns></returns>
    string MintIdentity(string resourceType, Uri? equivalent = null);
    
    
    public Task<Result<IdentityRecord>> GetIdentityBySchema(SchemaAndValue schemaAndValue, CancellationToken cancellationToken);
    
    public Task<Result<IdentityRecord>> GetIdentityByCatIrn(string catIrn, CancellationToken cancellationToken);
    
    
    public Task<Result<IdentityRecord>> GetIdentityByArchivalGroup(Uri archivalGroupUri, CancellationToken cancellationToken);
}