using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;

namespace DigitalPreservation.Common.Model.Mets;

public interface IMetsManager
{
    public const string MetsCreatorAgent = "University of Leeds Digital Library Infrastructure Project";
    // Create an empty METS file
    Task<Result<MetsFileWrapper>> CreateStandardMets(Uri metsLocation, string? agNameFromDeposit);
    // Reverse-engineer a METS file from an existing AG. This is OK for now but likely to be an error scenario
    Task<Result<MetsFileWrapper>> CreateStandardMets(Uri metsLocation, ArchivalGroup archivalGroup, string? agNameFromDeposit);
    bool IsMetsFile(string fileName);
    
    Task<Result> HandleSingleFileUpload(Uri workingRoot, WorkingFile workingFile, string depositETag);
    Task<Result> HandleDeleteObject(Uri workingRoot, string localPath, string depositETag);
    Task<Result> HandleCreateFolder(Uri workingRoot, WorkingDirectory workingDirectory, string depositETag);

    Task<Result<FullMets>> GetFullMets(Uri metsLocation, string? eTagToMatch);
    // Synchronous modification of FullMETS - does not save to disk!
    Result AddToMets(FullMets fullMets, WorkingBase workingBase);
    Result DeleteFromMets(FullMets fullMets, string deletePath);
    Task<Result> WriteMets(FullMets fullMets);
}