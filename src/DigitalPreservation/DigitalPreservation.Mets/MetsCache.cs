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
/// navigable by path; the diagnostics returned by <see cref="Populate"/> say why. Those
/// diagnostics are the seed of a future conformance check for whether a METS file is editable.
/// </summary>
public static class MetsCache
{
    /// <summary>
    /// Rebuild the path cache from the current state of the METS. Returns diagnostics for any
    /// div whose path could not be resolved, or that collided with another div's path; an empty
    /// list means the whole physical structMap is navigable by path.
    /// </summary>
    public static List<string> Populate(FullMets fullMets)
    {
        fullMets.PhysicalDivsByPath.Clear();
        return Build(fullMets.Mets, fullMets.PhysicalDivsByPath);
    }

    /// <summary>
    /// Build the path→div mapping into the supplied (empty) dictionary. Used by
    /// <see cref="Populate"/> and by MetsManager's debug-build cache-consistency assertion.
    /// </summary>
    internal static List<string> Build(
        DigitalPreservation.XmlGen.Mets.Mets mets, Dictionary<string, DivType> cache)
    {
        var diagnostics = new List<string>();

        var physRoot = mets.StructMap
            .SingleOrDefault(sm => sm.Type == Constants.Physical)?.Div;
        if (physRoot == null)
        {
            diagnostics.Add("METS has no PHYSICAL structMap");
            return diagnostics;
        }

        Walk(physRoot, mets, cache, diagnostics);
        return diagnostics;
    }

    private static void Walk(
        DivType div,
        DigitalPreservation.XmlGen.Mets.Mets mets,
        Dictionary<string, DivType> cache,
        List<string> diagnostics)
    {
        foreach (var child in div.Div)
        {
            var key = ResolvePath(child, mets, diagnostics);
            if (key != null && !cache.TryAdd(key, child))
            {
                diagnostics.Add(
                    $"Divs '{cache[key].Id}' and '{child.Id}' both resolve to path '{key}'");
            }

            Walk(child, mets, cache, diagnostics);
        }
    }

    private static string? ResolvePath(
        DivType child,
        DigitalPreservation.XmlGen.Mets.Mets mets,
        List<string> diagnostics)
    {
        string? path = null;
        switch (child.Type)
        {
            case Constants.DirectoryType when child.Admid.Count == 0:
                diagnostics.Add($"Directory div '{child.Id}' has no ADMID, so no path");
                break;
            case Constants.DirectoryType:
            {
                // Legacy IDs may contain spaces, which the XML processor splits into multiple
                // IDREFS tokens; joining reconstructs the single amdSec ID (see MetadataManager).
                var admId = string.Join(' ', child.Admid);
                var amdSec = mets.AmdSec.FirstOrDefault(a => a.Id == admId);
                path = ExtractPremisOriginalName(amdSec);
                if (path == null)
                {
                    diagnostics.Add(
                        $"Directory div '{child.Id}' has no premis:originalName via ADMID '{admId}'");
                }
                break;
            }
            case Constants.ItemType when child.Fptr.Count == 0:
                diagnostics.Add($"Item div '{child.Id}' has no fptr, so no path");
                break;
            case Constants.ItemType:
            {
                var fileId = child.Fptr[0].Fileid;
                var file = mets.FileSec?.FileGrp
                    .SelectMany(fg => fg.File)
                    .FirstOrDefault(f => f.Id == fileId);
                path = file?.FLocat.FirstOrDefault()?.Href;
                if (path == null)
                {
                    diagnostics.Add(
                        $"Item div '{child.Id}' has no FLocat href via FILEID '{fileId}'");
                }
                break;
            }
            default:
                diagnostics.Add(
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
            diagnostics.Add($"Div '{child.Id}' resolves to empty path '{path}'");
        }
        return normalised;
    }

    /// <summary>
    /// Normalise a path extracted from METS metadata to the deposit-relative form used
    /// everywhere else in the system (and by MetsManager's own localPath handling).
    /// </summary>
    internal static string? NormalisePathKey(string path)
    {
        var normalised = FolderNames.RemovePathPrefix(path.Trim())!
            .RemoveStart("./")!
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
