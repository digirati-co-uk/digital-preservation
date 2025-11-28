using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions;

namespace DigitalPreservation.Common.Model.Mets;

public interface IMetsManager
{
    public const string MetsCreatorAgent = "University of Leeds Digital Library Infrastructure Project";
    
    public const string RestrictionOnAccess = "restriction on access";
    public const string UseAndReproduction = "use and reproduction";
    
    // Create an empty METS file
    Task<Result<MetsFileWrapper>> CreateStandardMets(Uri metsLocation, string? agNameFromDeposit);
    // Reverse-engineer a METS file from an existing AG. This is OK for now but likely to be an error scenario
    Task<Result<MetsFileWrapper>> CreateStandardMets(Uri metsLocation, ArchivalGroup archivalGroup, string? agNameFromDeposit);
    
    Task<Result> HandleSingleFileUpload(Uri workingRoot, WorkingFile workingFile, string depositETag); // , Uri? storageLocation
    Task<Result> HandleDeleteObject(Uri workingRoot, string localPath, string depositETag);
    Task<Result> HandleCreateFolder(Uri workingRoot, WorkingDirectory workingDirectory, string depositETag); // , Uri? storageLocation

    Task<Result<FullMets>> GetFullMets(Uri metsLocation, string? eTagToMatch);
    // Synchronous modification of FullMETS - does not save to disk!
    Result AddToMets(FullMets fullMets, WorkingBase workingBase); // , Uri? storageLocation
    Result DeleteFromMets(FullMets fullMets, string deletePath); // , Uri? storageLocation
    Task<Result> WriteMets(FullMets fullMets);

    [Obsolete]
    List<string> GetRootAccessRestrictions(FullMets fullMets);
    [Obsolete]
    void SetRootAccessRestrictions(FullMets fullMets, List<string> accessRestrictions);
    [Obsolete]
    void SetRootRightsStatement(FullMets fullMets, Uri? uri);
    [Obsolete]
    Uri? GetRootRightsStatement(FullMets fullMets);
    
    // Extensions
    // We don't need getters because this information will be exposed in either WorkingFile/Dir or Logical ... Ranges
    void SetRecordIdentifier(FullMets mets, string physicalPath, string source, string value);
    void SetRightsStatement(FullMets mets, string physicalPath, Uri? rightsStatement);
    void SetAccessRestrictions(FullMets mets, string physicalPath, List<string> accessRestrictions);


    void SetStructMap(FullMets mets, LogicalRange logSm);
    void LinkFile(FullMets mets, string from, string to, Uri role);
}