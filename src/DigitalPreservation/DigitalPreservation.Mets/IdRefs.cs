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
    /// <summary>
    /// The characters XML treats as whitespace, and therefore as IDREFS separators. The
    /// XmlSerializer splits on all of them; the raw-string side must do the same.
    /// </summary>
    private static readonly char[] XmlWhitespace = [' ', '\t', '\n', '\r'];

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
    /// As <see cref="ResolveSingle{T}(IReadOnlyList{string}, Func{string, T?})"/>, but for a
    /// caller that can only USE certain referenced elements — a file's technical metadata has to
    /// come from a section that actually carries PREMIS, not merely from one that exists.
    /// </summary>
    /// <remarks>
    /// The two tiers are deliberately NOT treated alike, and the asymmetry is the whole point.
    /// The joined form is an IDENTITY match: it says "this one section is what the reference
    /// names", so if it resolves, that is the answer whether the caller can use it or not —
    /// returning it lets the caller report a precise diagnostic, where walking on would resolve
    /// a FRAGMENT of a legacy ID against some unrelated section and answer with confident
    /// nonsense. Per-token resolution is a genuine LIST whose order carries no meaning, so there
    /// an unusable candidate is simply skipped and the next tried.
    /// A single token is an identity match too, and is returned unfiltered for the same reason.
    /// </remarks>
    public static T? ResolveSingle<T>(
        IReadOnlyList<string> tokens, Func<string, T?> lookupById, Func<T, bool> isUsable)
        where T : class
    {
        switch (tokens.Count)
        {
            case 0:
                return null;
            case 1:
                return lookupById(tokens[0]);
            default:
                return lookupById(Joined(tokens))
                       ?? tokens.Select(lookupById)
                           .FirstOrDefault(match => match != null && isUsable(match));
        }
    }

    /// <summary>
    /// Mirror-image resolution for the XDocument/raw-string side (MetsParser), where the whole
    /// IDREFS attribute value arrives as one string: try the whole value first (a single ID,
    /// or a legacy ID containing spaces), then each whitespace-separated token (schema-valid
    /// IDREFS list - XML allows tab/newline/CR separators as well as spaces), first match
    /// winning. Deliberately NOT implemented by delegating to the token-collection overload:
    /// this side's first tier must match the attribute value VERBATIM (a legacy ID could
    /// contain any whitespace a filename can), whereas the collection overload's joined tier
    /// reconstructs with single spaces because that is all the XmlSerializer's split leaves
    /// it - the two first tiers are subtly different and each is right for its input.
    /// </summary>
    public static T? ResolveSingle<T>(string attributeValue, Func<string, T?> lookupById)
        where T : class
    {
        var whole = lookupById(attributeValue);
        if (whole != null || attributeValue.IndexOfAny(XmlWhitespace) < 0)
        {
            return whole;
        }
        return attributeValue
            .Split(XmlWhitespace, StringSplitOptions.RemoveEmptyEntries)
            .Select(lookupById)
            .FirstOrDefault(match => match != null);
    }

    /// <summary>
    /// The raw-string counterpart of
    /// <see cref="ResolveSingle{T}(IReadOnlyList{string}, Func{string, T?}, Func{T, bool})"/>,
    /// with the same asymmetry: the whole attribute value is an identity match and is returned
    /// whether the caller can use it or not, while the per-token tier skips what it cannot use.
    /// </summary>
    public static T? ResolveSingle<T>(
        string attributeValue, Func<string, T?> lookupById, Func<T, bool> isUsable)
        where T : class
    {
        var whole = lookupById(attributeValue);
        if (whole != null || attributeValue.IndexOfAny(XmlWhitespace) < 0)
        {
            return whole;
        }
        return attributeValue
            .Split(XmlWhitespace, StringSplitOptions.RemoveEmptyEntries)
            .Select(lookupById)
            .FirstOrDefault(match => match != null && isUsable(match));
    }

    /// <summary>
    /// Resolve an IDREFS token collection to every referenced element. The same tiering as
    /// <see cref="ResolveSingle{T}(IReadOnlyList{string},Func{string,T})"/>: a joined-form
    /// match means the tokens are ONE legacy space-containing ID, so exactly that element is
    /// returned; otherwise each token resolves individually (genuine IDREFS list), skipping
    /// tokens that don't resolve. Never throws; empty when nothing matches.
    /// </summary>
    public static IReadOnlyList<T> ResolveAll<T>(IReadOnlyList<string> tokens, Func<string, T?> lookupById)
        where T : class
    {
        switch (tokens.Count)
        {
            case 0:
                return [];
            case 1:
                return lookupById(tokens[0]) is { } single ? [single] : [];
            default:
                if (lookupById(Joined(tokens)) is { } legacy)
                {
                    return [legacy];
                }
                return tokens
                    .Select(lookupById)
                    .Where(match => match != null)
                    .Distinct()
                    .Cast<T>()
                    .ToList();
        }
    }

    /// <summary>
    /// Remove from an IDREFS token collection the reference to an element previously resolved
    /// from it: every token when the tokens jointly formed a legacy space-containing ID, else
    /// just the token equal to the element's ID. Other references are left intact.
    /// </summary>
    public static void RemoveReference(IList<string> tokens, string resolvedId)
    {
        if (Joined(tokens) == resolvedId)
        {
            tokens.Clear();
        }
        else
        {
            tokens.Remove(resolvedId);
        }
    }
}
