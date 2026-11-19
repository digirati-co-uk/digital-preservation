namespace DigitalPreservation.Mets;

/// <summary>
/// Whether a <c>mets:area</c> picks out part of a file. One with none of these attributes - only a
/// FILEID, say - points at the whole file, exactly like an <c>mets:fptr</c> with no area. The
/// parser reads these; the writer checks the same attributes on its typed area (DerivedAccessConditionWriter), so they agree on which logical ranges a file falls back to
/// for its access conditions (issue #433).
/// </summary>
public static class AreaExtent
{
    public static readonly IReadOnlySet<string> Attributes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BEGIN", "END", "BETYPE", "SHAPE", "COORDS", "EXTENT", "EXTTYPE" };

    /// <summary>The parser's view: an area's attributes other than the time segment or region it recognised.</summary>
    public static bool AnyIn(IEnumerable<string>? attributeNames) =>
        attributeNames?.Any(Attributes.Contains) ?? false;
}
