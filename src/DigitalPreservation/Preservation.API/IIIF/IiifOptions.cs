namespace Preservation.API.IIIF;

/// <summary>
/// Configuration of the IIIF projection (issue #261), section <c>Iiif</c>.
/// </summary>
public class IiifOptions
{
    public const string SectionName = "Iiif";

    /// <summary>
    /// The JSON-LD context document for the platform's IIIF extensions (<c>accessConditions</c>,
    /// <c>formerAssets</c>). Configurable because the docs site that hosts it may move: a JSON
    /// consumer keys on the property names, so moving it changes only what new manifests say.
    /// </summary>
    public string ExtensionContext { get; set; } =
        "https://digirati-co-uk.github.io/digital-preservation-docs/ns/iiif/context.json";

    /// <summary>
    /// The access condition types that become placeholder <c>roles</c> on the assets of the IIIF-CS
    /// manifest (issue #261): one token per condition of these types, which a builder maps to real
    /// IIIF-CS role URIs. Empty means every type except <c>use and reproduction</c>, which is about
    /// rights, not access. Leeds names <c>tier</c> alone, since the tier already combines the
    /// restriction and the copyright statement.
    /// </summary>
    public List<string> RoleConditionTypes { get; set; } = [];

    /// <summary>The namespace the extension terms are defined in; it moves with the context.</summary>
    public string ExtensionNamespace { get; set; } =
        "https://digirati-co-uk.github.io/digital-preservation-docs/ns/iiif#";
}
