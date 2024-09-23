namespace Storage.API.Fedora.Vocab;

public static class Prefer
{
    /// <summary>
    /// Include assertions from other Fedora resources to this node (excluded from representation by default)
    /// </summary>
    public const string PreferInboundReferences = "http://fedora.info/definitions/fcrepo#PreferInboundReferences";

    /// <summary>
    /// Embed server managed properties in the representation (enabled by default)
    /// </summary>
    public const string ServerManaged = "http://fedora.info/definitions/fcrepo#ServerManaged";

    /// <summary>
    /// Include/Exclude "ldp:contains" assertions to contained resources(enabled by default)
    /// </summary>
    public const string PreferContainment = "http://www.w3.org/ns/ldp#PreferContainment";

    /// <summary>
    /// Include/Exclude assertions to member resources established by the Direct and Indirect containers(enabled by default)
    /// </summary>
    public const string PreferMembership = "http://www.w3.org/ns/ldp#PreferMembership";

    /// <summary>
    /// Include/Exclude triples that would be present when the container is empty(enabled by default)
    /// </summary>
    public const string PreferMinimalContainer = "http://www.w3.org/ns/ldp#PreferMinimalContainer";

    /// <summary>
    /// Embed "child" resources in the returned representation
    /// </summary>
    public const string PreferContainedDescriptions = "http://www.w3.org/ns/oa#PreferContainedDescriptions";
}