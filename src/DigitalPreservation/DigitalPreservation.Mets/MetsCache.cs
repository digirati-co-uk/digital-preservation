using System.Xml;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Utils;
using DigitalPreservation.XmlGen.Mets;

namespace DigitalPreservation.Mets;

/// <summary>
/// Builds <see cref="FullMets.PhysicalDivsByPath"/>: the mapping from deposit-relative path to
/// physical structMap div. Paths are resolved from the div's own metadata — premis:originalName
/// for directories, the referenced FILE's FLocat href for files — never from the div's ID, so
/// the mapping holds whatever form the IDs take (issue #188).
/// A METS file whose physical structMap cannot be fully and unambiguously mapped is not fully
/// navigable by path; the diagnostics returned by <see cref="Populate"/> (and kept on
/// <see cref="FullMets.PathDiagnostics"/>) say why. Those diagnostics are the seed of a future
/// conformance check for whether a METS file is editable.
/// </summary>
public static class MetsCache
{
    /// <summary>
    /// Rebuild the path cache from the current state of the METS. Returns diagnostics for any
    /// div whose path could not be resolved, or that collided with another div's path; an empty
    /// list means the whole physical structMap is navigable by path. The diagnostics are also
    /// stored on <see cref="FullMets.PathDiagnostics"/> so later failures can explain themselves.
    /// </summary>
    internal const string DuplicatePathFragment = "both resolve to path";

    public static List<string> Populate(FullMets fullMets)
    {
        fullMets.PhysicalDivsByPath.Clear();
        var diagnostics = Build(fullMets.Mets, fullMets.PhysicalDivsByPath);
        fullMets.PathDiagnostics.Clear();
        fullMets.PathDiagnostics.AddRange(diagnostics);
        fullMets.HasDuplicatePaths = diagnostics.Any(d => d.Contains(DuplicatePathFragment));
        return diagnostics;
    }

    /// <summary>
    /// Build the path→div mapping into the supplied (empty) dictionary. Used by
    /// <see cref="Populate"/> and by MetsManager's debug-build cache-consistency assertion.
    /// </summary>
    internal static List<string> Build(
        DigitalPreservation.XmlGen.Mets.Mets mets, Dictionary<string, DivType> cache)
    {
        var diagnostics = new List<string>();
        // One ID index for the whole walk. Without it each div's resolution scans the fileSec
        // (or the amdSecs) end to end, which makes building the cache quadratic in the size of
        // the deposit - and the cache is built on every load.
        var index = new MetsIdIndex(mets);

        // Tolerate malformed METS: never throw here, this runs on every load. Zero or many
        // PHYSICAL structMaps are diagnostics, not exceptions; with many, the first is used
        // (matching the parser's structMap selection order).
        var physicalStructMaps = mets.StructMap.Where(sm => sm.Type == Constants.Physical).ToList();
        if (physicalStructMaps.Count == 0)
        {
            diagnostics.Add("METS has no PHYSICAL structMap");
            return diagnostics;
        }
        if (physicalStructMaps.Count > 1)
        {
            diagnostics.Add(
                $"METS has {physicalStructMaps.Count} PHYSICAL structMaps; only the first is navigable");
        }

        var physRoot = physicalStructMaps[0].Div;
        if (physRoot == null)
        {
            diagnostics.Add("PHYSICAL structMap has no root div");
            return diagnostics;
        }

        Walk(physRoot, mets, cache, diagnostics, index);
        return diagnostics;
    }

    private static void Walk(
        DivType div,
        DigitalPreservation.XmlGen.Mets.Mets mets,
        Dictionary<string, DivType> cache,
        List<string> diagnostics,
        MetsIdIndex index)
    {
        foreach (var child in div.Div)
        {
            var key = ResolvePath(child, mets, diagnostics, index);
            if (key != null && !cache.TryAdd(key, child))
            {
                diagnostics.Add(
                    $"Divs '{cache[key].Id}' and '{child.Id}' {DuplicatePathFragment} '{key}'");
            }

            Walk(child, mets, cache, diagnostics, index);
        }
    }

    /// <summary>
    /// Resolve a single div to its deposit-relative path from its own metadata, without
    /// reporting diagnostics. Used by MetsManager's navigation fallback when the cache entry
    /// for a path is missing or ambiguous, and by MetsFromArchivalGroup to match template
    /// divs by path rather than by reconstructed ID.
    /// </summary>
    public static string? TryResolvePath(DivType div, DigitalPreservation.XmlGen.Mets.Mets mets)
        => ResolvePath(div, mets, null, null);

    /// <summary>
    /// As <see cref="TryResolvePath(DivType, DigitalPreservation.XmlGen.Mets.Mets)"/>, but reusing
    /// a caller-built ID index. Use this when resolving several divs against the same document -
    /// resolving each one independently rescans the fileSec every time.
    /// </summary>
    internal static string? TryResolvePath(
        DivType div, DigitalPreservation.XmlGen.Mets.Mets mets, MetsIdIndex index)
        => ResolvePath(div, mets, null, index);

    private static string? ResolvePath(
        DivType child,
        DigitalPreservation.XmlGen.Mets.Mets mets,
        List<string>? diagnostics,
        MetsIdIndex? index)
    {
        string? path = null;
        switch (child.Type)
        {
            case Constants.DirectoryType when child.Admid.Count == 0:
                diagnostics?.Add($"Directory div '{child.Id}' has no ADMID, so no path");
                break;
            case Constants.DirectoryType:
            {
                // ADMID is an IDREFS token collection; IdRefs resolves both legacy
                // space-containing IDs and genuine multi-references.
                var amdSec = IdRefs.ResolveSingle(child.Admid, id => index != null
                    ? index.AmdSecById(id)
                    : mets.AmdSec.FirstOrDefault(a => a.Id == id));
                path = ExtractPremisOriginalName(amdSec);
                if (path == null)
                {
                    diagnostics?.Add(
                        $"Directory div '{child.Id}' has no premis:originalName via ADMID '{IdRefs.Joined(child.Admid)}'");
                }
                break;
            }
            case Constants.ItemType when child.Fptr.Count == 0:
                diagnostics?.Add($"Item div '{child.Id}' has no fptr, so no path");
                break;
            case Constants.ItemType:
            {
                var fileId = child.Fptr[0].Fileid;
                var file = index != null
                    ? index.FileById(fileId)
                    : mets.FileSec?.FileGrp.SelectMany(fg => fg.File).FirstOrDefault(f => f.Id == fileId);
                path = file?.FLocat.FirstOrDefault()?.Href;
                if (path == null)
                {
                    diagnostics?.Add(
                        $"Item div '{child.Id}' has no FLocat href via FILEID '{fileId}'");
                }
                break;
            }
            default:
                diagnostics?.Add(
                    $"Div '{child.Id}' has unrecognised TYPE '{child.Type}', so no path");
                break;
        }

        if (path == null)
        {
            return null;
        }

        var normalised = NormalisePathKey(path);
        if (normalised == null)
        {
            diagnostics?.Add($"Div '{child.Id}' resolves to empty path '{path}'");
        }
        return normalised;
    }

    /// <summary>
    /// Normalise a path extracted from METS metadata to the deposit-relative form used
    /// everywhere else in the system (and by MetsManager's own localPath handling). Only
    /// structural variants are normalised (leading ./, BagIt data/ prefix, trailing /);
    /// the characters of the path itself are never altered.
    /// </summary>
    internal static string? NormalisePathKey(string? path)
    {
        if (path == null)
        {
            return null;
        }
        var normalised = FolderNames.RemovePathPrefix(path.RemoveStart("./"))!
            .TrimEnd('/');
        return normalised.HasText() ? normalised : null;
    }

    private static string? ExtractPremisOriginalName(AmdSecType? amdSec)
    {
        var premisXml = amdSec?.TechMd.FirstOrDefault()?.MdWrap?.XmlData?.Any?.FirstOrDefault();
        if (premisXml is not XmlElement element)
        {
            return null;
        }

        var originalName = element
            .GetElementsByTagName("originalName", XNames.premis.NamespaceName)
            .OfType<XmlElement>()
            .FirstOrDefault()?.InnerText;
        return originalName.HasText() ? originalName : null;
    }
}
