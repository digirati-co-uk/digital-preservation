namespace DigitalPreservation.Mets;

/// <summary>
/// Resolution helpers for METS IDREFS attributes (ADMID, DMDID) read through the XmlGen typed
/// model, where the XmlSerializer splits the attribute value on whitespace into a token
/// collection. Two incompatible realities have to be served:
/// <list type="bullet">
/// <item>legacy platform METS (pre issue #188) minted IDs containing spaces, so ONE intended
/// ID arrives as SEVERAL tokens and must be rejoined before lookup;</item>
/// <item>schema-valid METS may genuinely reference several elements, one complete ID per
/// token.</item>
/// </list>
/// The joined form is tried first: no schema-valid ID can contain a space, so a joined
/// multi-token string can only match in a legacy document, where it is the right answer.
/// After issue #188 step 2 no newly minted ID can contain a space, so the joined tier only
/// ever fires for legacy content; it is removable after a step 3 bulk migration.
/// </summary>
public static class IdRefs
{
    /// <summary>The single intended ID that a legacy space-containing ID was split from.</summary>
    public static string Joined(IEnumerable<string> tokens) => string.Join(' ', tokens);

    /// <summary>
    /// Resolve an IDREFS token collection to one referenced element: first as a legacy single
    /// ID containing spaces (the joined form), otherwise each token individually with the
    /// first match winning. Returns null - never throws - when nothing matches or the
    /// collection is empty.
    /// </summary>
    public static T? ResolveSingle<T>(IReadOnlyList<string> tokens, Func<string, T?> lookupById)
        where T : class
    {
        return tokens.Count switch
        {
            0 => null,
            1 => lookupById(tokens[0]),
            _ => lookupById(Joined(tokens))
                 ?? tokens.Select(lookupById).FirstOrDefault(match => match != null)
        };
    }

    /// <summary>
    /// Mirror-image resolution for the XDocument/raw-string side (MetsParser), where the whole
    /// IDREFS attribute value arrives as one string: try the whole value first (a single ID,
    /// or a legacy ID containing spaces), then each whitespace-separated token (schema-valid
    /// IDREFS list), first match winning.
    /// </summary>
    public static T? ResolveSingle<T>(string attributeValue, Func<string, T?> lookupById)
        where T : class
    {
        var whole = lookupById(attributeValue);
        if (whole != null || !attributeValue.Contains(' '))
        {
            return whole;
        }
        return attributeValue
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(lookupById)
            .FirstOrDefault(match => match != null);
    }
}
