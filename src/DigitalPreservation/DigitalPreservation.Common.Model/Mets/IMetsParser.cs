using DigitalPreservation.Common.Model.Results;

namespace DigitalPreservation.Common.Model.Mets;

public interface IMetsParser
{
    public Task<Result<MetsFileWrapper>> GetMetsFileWrapper(Uri metsLocation);
}