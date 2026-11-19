namespace DigitalPreservation.Mets.Conformance;

/// <summary>
/// The verdicts of the editability judge, as defined in src/mets-editability/CONTRACT.md.
/// Exactly one per document.
/// </summary>
public static class Verdicts
{
    public const string Editable = "editable";
    public const string EditableWithNormalisation = "editable-with-normalisation";
    public const string NavigableReadOnly = "navigable-read-only";
    public const string NotEditable = "not-editable";
}

/// <summary>
/// One judged fact about the document. The codes are CONTRACT.md's shared vocabulary, identical
/// in both judges. A finding that occurs more than once is reported once: <see cref="Message"/>
/// describes the first occurrence and <see cref="Count"/> says how many there were.
/// </summary>
public sealed record Finding(string Code, string Message, int Count = 1);

/// <summary>
/// What the judge concluded about one document: the verdict, why it is not higher
/// (<see cref="Reasons"/>), what the EPrints tier satisfied by assumption rather than assertion
/// (<see cref="Assumptions"/>), informational <see cref="Notes"/>, and - for
/// editable-with-normalisation only - the <see cref="Mutations"/> the first save would perform,
/// in order. A dry run, never an action: the judge is read-only. The twin of app/judgement.py.
/// </summary>
public sealed class Judgement
{
    public required string Verdict { get; init; }
    public int FileCount { get; init; }
    public List<Finding> Reasons { get; init; } = [];
    public List<Finding> Assumptions { get; init; } = [];
    public List<Finding> Notes { get; init; } = [];
    public List<string> Mutations { get; init; } = [];
}

internal static class Findings
{
    /// <summary>Record one occurrence: the first carries the message, later ones only add to
    /// the count.</summary>
    internal static void Add(List<Finding> findings, string code, string message)
    {
        var index = findings.FindIndex(finding => finding.Code == code);
        if (index < 0)
        {
            findings.Add(new Finding(code, message));
            return;
        }
        findings[index] = findings[index] with { Count = findings[index].Count + 1 };
    }
}
