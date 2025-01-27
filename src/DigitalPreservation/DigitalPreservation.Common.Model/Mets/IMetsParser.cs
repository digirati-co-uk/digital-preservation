using DigitalPreservation.Common.Model.Results;

namespace DigitalPreservation.Common.Model.Mets;

public interface IMetsParser
{
    /// <summary>
    /// Simple model that doesn't use the XmlSerializer
    /// </summary>
    /// <param name="metsLocation"></param>
    /// <returns></returns>
    public Task<Result<MetsFileWrapper>> GetMetsFileWrapper(Uri metsLocation);
    
}