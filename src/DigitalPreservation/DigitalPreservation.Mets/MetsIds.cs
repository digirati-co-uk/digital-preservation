using System.Text.RegularExpressions;
using System.Xml;
using DigitalPreservation.Utils;

namespace DigitalPreservation.Mets;

/// <summary>
/// The METS IDs the platform mints for a deposit-relative path.
/// </summary>
/// <remarks>
/// One authority, so that every part of the system spells an ID the same way and a change to the
/// scheme is a change to one file. Before this existed the prefix and the encoding were combined
/// by hand at a dozen sites, and the tests had a tidier abstraction than the production code did.
/// <para>
/// An ID carries the path deliberately - it is legible to a person reading the METS, and unique
/// without any allocation, because a deposit-relative path is already unique in a document. But it
/// is opaque to code: nothing may read a path back out of one. See 02d, "Opaque to code, legible
/// to people".
/// </para>
/// </remarks>
public static class MetsIds
{
    public static string Phys(string localPath) => Constants.PhysIdPrefix + localPath.ToMetsId();
    public static string File(string localPath) => Constants.FileIdPrefix + localPath.ToMetsId();
    public static string Adm(string localPath) => Constants.AdmIdPrefix + localPath.ToMetsId();
    public static string Tech(string localPath) => Constants.TechIdPrefix + localPath.ToMetsId();
    public static string Dmd(string localPath) => Constants.DmdIdPrefix + localPath.ToMetsId();

    /// <summary>
    /// A dmdSec ID for a div with no deposit-relative path — a logical div, where the div's own ID
    /// is the only stable stem there is. The same scheme as <see cref="Dmd"/>, named separately so
    /// the call site says which stem it is using; sharing the implementation is the point, so that
    /// a change to how a dmdSec ID is spelt stays one edit here.
    /// </summary>
    public static string DmdFromDivId(string divId) => Dmd(divId);

    /// <summary>
    /// The digiprovMD ID for a file's FIRST virus scan. Later scans of the same file are numbered
    /// by <see cref="Constants.NumberedVirusProvEventId"/>, which takes the identifier this is
    /// built from rather than taking this ID apart again.
    /// </summary>
    public static string VirusProvEvent(string identifier) => Constants.VirusProvEventPrefix + identifier;

    /// <summary>
    /// Whether a string may be used as an xs:ID. Non-throwing, because this is asked of every ID in
    /// a document and most of them, in a document written before issue #214, are not valid.
    /// </summary>
    public static bool IsValidId(string? id)
    {
        if (string.IsNullOrEmpty(id) || !XmlConvert.IsStartNCNameChar(id[0]))
        {
            return false;
        }
        for (var i = 1; i < id.Length; i++)
        {
            if (!XmlConvert.IsNCNameChar(id[i]))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// The prefixes above, so that <see cref="Normalise"/> can encode the stem an ID was built from
    /// rather than the whole string, and produce exactly what the methods above would mint. None is
    /// a prefix of another, so the order they are tried in does not matter.
    /// </summary>
    private static readonly string[] StemPrefixes =
    [
        Constants.PhysIdPrefix, Constants.FileIdPrefix, Constants.AdmIdPrefix,
        Constants.TechIdPrefix, Constants.DmdIdPrefix
    ];

    /// <summary>
    /// <c>digiprovMD_ClamAV_</c>, optionally followed by the occurrence counter that
    /// <see cref="Constants.NumberedVirusProvEventId"/> puts there. What follows is another ID
    /// (see <see cref="VirusProvEvent"/>, which concatenates rather than encoding), so it is
    /// normalised in its own right.
    /// </summary>
    private static readonly Regex VirusProvEventPrefixPattern =
        new($"^{Regex.Escape(Constants.VirusProvEventPrefix)}(?:[0-9]+_)?", RegexOptions.Compiled);

    /// <summary>
    /// The same ID, spelt the way this platform spells IDs now - for migrating a document minted
    /// before issue #214, where the stem was concatenated raw and the result was not a legal
    /// NCName. An ID that is already valid is returned untouched, whoever minted it and whatever
    /// scheme it follows.
    /// </summary>
    /// <remarks>
    /// This never asks what an ID means, and never derives one from a path: it re-encodes the
    /// string it was given, which is what keeps the migration inside the rule that IDs are opaque
    /// to code (see 02d, "Opaque to code, legible to people"). Because the encoding is applied to
    /// the stem after a known prefix, the result is character-for-character what
    /// <see cref="Phys"/> and its siblings would mint from the same path.
    /// <para>
    /// Leaving a valid ID alone is what makes this safe to run repeatedly, and what keeps it away
    /// from IDs that are not ours to renumber - a client-supplied logical range ID, for instance,
    /// which is already an NCName and is public through the IIIF Range URI built from it.
    /// </para>
    /// </remarks>
    public static string Normalise(string id)
    {
        if (IsValidId(id))
        {
            return id;
        }

        var virusPrefix = VirusProvEventPrefixPattern.Match(id);
        if (virusPrefix.Success)
        {
            return virusPrefix.Value + Normalise(id[virusPrefix.Length..]);
        }

        foreach (var prefix in StemPrefixes)
        {
            if (id.StartsWith(prefix, StringComparison.Ordinal))
            {
                return prefix + id[prefix.Length..].ToMetsId();
            }
        }

        // Not one of ours, or one whose prefix we no longer mint. Encoding the whole string still
        // produces a legal, unique, reversible ID - it just isn't split at the prefix.
        return id.ToMetsId();
    }
}
