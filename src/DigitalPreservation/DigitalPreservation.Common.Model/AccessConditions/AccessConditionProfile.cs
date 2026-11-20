using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.AccessConditions;

/// <summary>
/// An installation's access-condition vocabulary (issue #433), as served by <c>GET /access-conditions</c>.
/// </summary>
/// <remarks>
/// <para>The standard model is two independent choices per node, both inherited down the tree:</para>
/// <list type="bullet">
/// <item>an access <b>restriction</b>, from <see cref="RestrictionAxis"/>, written as
/// <c>mods:accessCondition type="restriction on access"</c>;</item>
/// <item>a <b>rights statement</b>, from the fixed rightsstatements.org and Creative Commons
/// vocabulary (<see cref="RightsStatement"/>), written as <c>type="use and reproduction"</c>.</item>
/// </list>
/// <para>That is all most installations need, and it is configured as a plain list
/// (<c>AccessConditions</c>). An installation whose rules are richer (Leeds) adds
/// <see cref="Statements"/> from a profile document: everything about that extension - what is
/// chosen instead of a rights statement, what it publishes in IIIF, and what is derived - lives in
/// <see cref="StatementProfile"/>, so this class stays the standard model.</para>
/// </remarks>
public class AccessConditionProfile
{
    /// <summary>The configuration key for the standard form: a plain list of restriction values.</summary>
    public const string SimpleSectionName = "AccessConditions";

    /// <summary>The configuration key that, with the standard form, allows several restrictions per node.</summary>
    public const string AllowMultipleKey = "AllowMultipleAccessConditions";

    /// <summary>The configuration key naming a profile document (a path relative to the content root).</summary>
    public const string FileKey = "AccessConditionProfileFile";

    /// <summary>What the installation calls this profile; informational.</summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    /// <summary>The profile document's own version, so a consumer can tell which one it read; informational.</summary>
    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; set; }

    /// <summary>When true the restriction dropdown is a multi-select.</summary>
    [JsonPropertyName("allowMultipleRestrictions")]
    public bool AllowMultipleRestrictions { get; set; }

    [JsonPropertyName("restrictionAxis")]
    public AccessAxis RestrictionAxis { get; set; } = new();

    [JsonPropertyName("rightsAxis")]
    public RightsAxis RightsAxis { get; set; } = new();

    /// <summary>
    /// The extension, or null for the standard model. When present, a person chooses a statement
    /// instead of a rights statement; see <see cref="StatementProfile"/>.
    /// </summary>
    [JsonPropertyName("statements")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StatementProfile? Statements { get; set; }

    [JsonIgnore]
    public bool HasStatements => Statements != null;

    /// <summary>The standard form: a restriction vocabulary and nothing else.</summary>
    public static AccessConditionProfile Simple(IEnumerable<AccessRestriction> restrictions, bool allowMultiple = false) => new()
    {
        AllowMultipleRestrictions = allowMultiple,
        RestrictionAxis = new AccessAxis { Values = restrictions.ToList() }
    };

    /// <summary>
    /// How a profile document is read: property names exactly as served (camelCase), comments and
    /// trailing commas allowed because people edit it, and any property this model does not know
    /// refused, so that a typo is an error rather than a silently missing setting.
    /// </summary>
    public static readonly JsonSerializerOptions DocumentOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>Read a profile document. Throws <see cref="JsonException"/> on malformed JSON or an unknown property.</summary>
    public static AccessConditionProfile FromJson(string json) =>
        JsonSerializer.Deserialize<AccessConditionProfile>(json, DocumentOptions)
        ?? throw new JsonException("The access condition profile document is empty.");

    /// <summary>Validation errors for the whole profile; empty when it is usable. Checked at startup.</summary>
    public List<string> Validate() => new AccessConditionProfileValidation(this).Run();

    /// <summary>
    /// The two <c>mods:accessCondition/@type</c> values the standard model uses. Mirrors the
    /// constants in DigitalPreservation.Mets, which this project cannot reference.
    /// </summary>
    public static class ReservedTypes
    {
        public const string RestrictionOnAccess = "restriction on access";
        public const string UseAndReproduction = "use and reproduction";
    }
}

/// <summary>The restriction dropdown: its label and its vocabulary.</summary>
public class AccessAxis
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "Access Restriction(s)";

    [JsonPropertyName("values")]
    public List<AccessRestriction> Values { get; set; } = [];
}

/// <summary>The rights statement dropdown. Its vocabulary is the fixed <see cref="RightsStatement"/> lists; config only labels it.</summary>
public class RightsAxis
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "Rights statement";
}
