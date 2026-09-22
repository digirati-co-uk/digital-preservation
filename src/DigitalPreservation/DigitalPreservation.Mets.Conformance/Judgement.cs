namespace DigitalPreservation.Mets.Conformance;

/// <summary>
/// The verdicts of the editability judge, as defined in src/mets-editability/CONTRACT.md (which
/// implements 02e-METS-Editability.md in the docs repo). Exactly one per document.
/// </summary>
public static class Verdicts
{
    public const string Editable = "editable";
    public const string EditableWithNormalisation = "editable-with-normalisation";
    public const string NavigableReadOnly = "navigable-read-only";
    public const string NotEditable = "not-editable";
}

/// <summary>One judged fact about the document. The codes are CONTRACT.md's shared vocabulary,
/// identical between this implementation and the Python judge.</summary>
public sealed record Finding(string Code, string Message);

/// <summary>
/// What the judge concluded about one document: the verdict, why it is not higher
/// (<see cref="Reasons"/>), what the EPrints tier satisfied by assumption rather than assertion
/// (<see cref="Assumptions"/>), informational <see cref="Notes"/> (legacy IDs, corpus quirks),
/// and - for editable-with-normalisation only - the <see cref="Mutations"/> the first save would
/// perform, in order. A dry run, never an action: the judge is read-only.
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
