using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Combined;

namespace DigitalPreservation.UI.Pages.Deposits;

public class CombinedDirectoryRecursionWrapper(
    CombinedDirectory combinedDirectory, string metsPath, bool editable, bool active, bool lockedByOtherUser, 
    List<string> rootAccessRestrictions, Uri? rootRightsStatement, // TODO: temporary, this will be learned from the CombinedDirectory / CombinedFile hierarchically
    Counter rowCounter)
{
    public CombinedDirectory CombinedDirectory { get; set; } = combinedDirectory;
    public bool Editable { get; set; } = editable;
    public bool Active { get; set; } = active;
    public bool LockedByOtherUser { get; set; } = lockedByOtherUser;
    public Counter RowCounter { get; set; } = rowCounter;
    public string MetsPath { get; set; } = metsPath;

    // TODO: temporary, this will be learned from the CombinedDirectory / CombinedFile hierarchically
    public List<string> RootAccessRestrictions { get; set; } = rootAccessRestrictions;
    public Uri? RootRightsStatement { get; set; } = rootRightsStatement;
}

public class Counter
{
    public int Count { get; set; } = 0;
}