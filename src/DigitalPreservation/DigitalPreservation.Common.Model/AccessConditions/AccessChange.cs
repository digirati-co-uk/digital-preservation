using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.AccessConditions;

/// <summary>
/// What a caller sends to set the access conditions and rights of one file or folder in a
/// deposit's METS.
/// </summary>
/// <remarks>
/// A property left out of the request is left as it is in the METS. A property sent as
/// <c>null</c> (or, for restrictions, an empty list) is cleared, so that the file or folder
/// inherits from its parent. Each property therefore remembers whether it was sent at all.
/// </remarks>
public class AccessChange
{
    private List<string>? accessRestrictions;
    private Uri? rightsStatement;
    private string? accessStatement;
    private Transit.EnteredExtent? extent;

    /// <summary>The file or folder, as a path from the root of the deposit, e.g. <c>objects/thesis.pdf</c>.</summary>
    [JsonPropertyName("path")]
    public required string Path { get; set; }

    /// <summary>Values from the installation's restriction vocabulary.</summary>
    [JsonPropertyName("accessRestrictions")]
    public List<string>? AccessRestrictions
    {
        get => accessRestrictions;
        set { accessRestrictions = value; AccessRestrictionsSent = true; }
    }

    /// <summary>
    /// A rightsstatements.org or Creative Commons URI. Not for an installation with a statement
    /// profile, where the rights statement follows from <see cref="AccessStatement"/>.
    /// </summary>
    [JsonPropertyName("rightsStatement")]
    public Uri? RightsStatement
    {
        get => rightsStatement;
        set { rightsStatement = value; RightsStatementSent = true; }
    }

    /// <summary>A statement from the installation's statement profile, if it has one.</summary>
    [JsonPropertyName("accessStatement")]
    public string? AccessStatement
    {
        get => accessStatement;
        set { accessStatement = value; AccessStatementSent = true; }
    }

    /// <summary>
    /// For a file the pipeline couldn't measure: its duration (seconds) and, for an image or video,
    /// width and height (pixels), as a person states them (issue #508). Recorded apart from measured
    /// values, which always win; null clears what was entered.
    /// </summary>
    [JsonPropertyName("extent")]
    public Transit.EnteredExtent? Extent
    {
        get => extent;
        set { extent = value; ExtentSent = true; }
    }

    [JsonIgnore] public bool AccessRestrictionsSent { get; private set; }
    [JsonIgnore] public bool RightsStatementSent { get; private set; }
    [JsonIgnore] public bool AccessStatementSent { get; private set; }
    [JsonIgnore] public bool ExtentSent { get; private set; }

    /// <summary>
    /// What is wrong with this request for an installation with this profile; empty if nothing is.
    /// The METS writer records whatever it is given, so the values are checked here.
    /// </summary>
    public List<string> Problems(AccessConditionProfile profile)
    {
        var problems = new List<string>();
        if (!AccessRestrictionsSent && !RightsStatementSent && !AccessStatementSent && !ExtentSent)
        {
            problems.Add("Send at least one of accessRestrictions, rightsStatement, accessStatement and extent.");
        }
        problems.AddRange(ExtentProblems());

        var restrictions = AccessRestrictions ?? [];
        var known = profile.RestrictionAxis.Values.Select(restriction => restriction.Value).ToList();
        foreach (var unknown in restrictions.Where(restriction => !known.Contains(restriction)))
        {
            problems.Add($"'{unknown}' is not an access restriction here. The choices are: {string.Join(", ", known)}.");
        }
        if (restrictions.Count > 1 && !profile.AllowMultipleRestrictions)
        {
            problems.Add("Only one access restriction is allowed here.");
        }

        if (RightsStatementSent && profile.HasStatements)
        {
            problems.Add("The rights statement follows from the access statement here; send accessStatement.");
        }
        else if (RightsStatement is not null && !Common.Model.RightsStatement.ByUri.ContainsKey(RightsStatement))
        {
            problems.Add($"'{RightsStatement}' is not a rights statement the platform knows.");
        }

        if (AccessStatement is not null && profile.Statements?.Find(AccessStatement) is null)
        {
            problems.Add(profile.HasStatements
                ? $"'{AccessStatement}' is not an access statement here. GET /access-conditions lists them."
                : "This installation has no access statements; send accessRestrictions and rightsStatement.");
        }

        return problems;
    }

    private IEnumerable<string> ExtentProblems()
    {
        if (Extent is { Duration: <= 0 } || Extent?.Duration is double.NaN or double.PositiveInfinity)
        {
            yield return "An extent's duration must be a positive number of seconds.";
        }
        if (Extent is { PixelWidth: <= 0 } or { PixelHeight: <= 0 })
        {
            yield return "An extent's width and height must be positive numbers of pixels.";
        }
    }
}
