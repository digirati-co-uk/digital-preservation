using DigitalPreservation.Common.Model.Results;


namespace DigitalPreservation.Common.Model.Mets;

public interface IMetsStorage
{
    Task<Result> WriteMets(FullMets fullMets);
    Task<Result<FullMets>> GetFullMets(Uri metsLocation, string? eTagToMatch);
}