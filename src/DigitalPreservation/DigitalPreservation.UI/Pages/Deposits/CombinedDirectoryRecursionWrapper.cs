using DigitalPreservation.Common.Model.Transit;

namespace DigitalPreservation.UI.Pages.Deposits;

public class CombinedDirectoryRecursionWrapper(
    CombinedDirectory combinedDirectory, string metsPath, bool editable, bool active, bool lockedByOtherUser, Counter rowCounter)
{
    public CombinedDirectory CombinedDirectory { get; set; } = combinedDirectory;
    public bool Editable { get; set; } = editable;
    public bool Active { get; set; } = active;
    public bool LockedByOtherUser { get; set; } = lockedByOtherUser;
    public Counter RowCounter { get; set; } = rowCounter;
    public string MetsPath { get; set; } = metsPath;
}

public class Counter
{
    public int Count { get; set; } = 0;
}