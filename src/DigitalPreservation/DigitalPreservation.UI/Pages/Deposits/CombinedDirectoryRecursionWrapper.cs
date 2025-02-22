using DigitalPreservation.Common.Model.Transit;

namespace DigitalPreservation.UI.Pages.Deposits;

public class CombinedDirectoryRecursionWrapper(
    CombinedDirectory combinedDirectory, bool editable, bool active, Counter rowCounter)
{
    public CombinedDirectory CombinedDirectory { get; set; } = combinedDirectory;
    public bool Editable { get; set; } = editable;
    public bool Active { get; set; } = active;
    public Counter RowCounter { get; set; } = rowCounter;
}

public class Counter
{
    public int Count { get; set; } = 0;
}