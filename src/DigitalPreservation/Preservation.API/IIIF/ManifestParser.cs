using System.Text.Json;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Utils;
using IIIF.Presentation.V3;
using IIIF.Presentation.V3.Strings;
using Range = IIIF.Presentation.V3.Range;

namespace Preservation.API.IIIF;

public static class ManifestParser
{
    private const string RecordIdentifierPrefix = "record identifier: ";

    /// <summary>
    /// Build a map from canvas ID (no fragment) → local file path, using the canvas list in the manifest.
    /// </summary>
    public static Dictionary<string, string> BuildCanvasIdToLocalPath(Manifest manifest, string iiifBaseUrl)
    {
        var canvasBaseUrl = $"{iiifBaseUrl}canvases/";
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in manifest.Items ?? [])
        {
            if (item.Id?.StartsWith(canvasBaseUrl) == true)
            {
                var localPath = item.Id[canvasBaseUrl.Length..].UnEscapePathElements();
                result[item.Id] = localPath;
            }
        }
        return result;
    }

    /// <summary>
    /// Convert the manifest's Structures (IIIF Ranges) back to LogicalRange trees.
    /// rawManifestJson: the original JSON, used as fallback for FragmentSelector (not modelled in iiif-net 0.3.13).
    /// </summary>
    public static List<LogicalRange> ExtractLogicalRanges(
        Manifest manifest,
        string iiifBaseUrl,
        string rawManifestJson)
    {
        var canvasIdToLocalPath = BuildCanvasIdToLocalPath(manifest, iiifBaseUrl);
        var fragmentIndex = BuildFragmentIndex(rawManifestJson);

        return (manifest.Structures ?? [])
            .Select(r => RangeToLogicalRange(r, canvasIdToLocalPath, fragmentIndex, isRoot: true))
            .ToList();
    }

    private static string NewRangeId() => "r" + Guid.NewGuid().ToString("N")[..11];

    private static string? MetaValue(Range range, string label) =>
        (range.Metadata ?? [])
            .Where(m => FirstValue(m.Label) == label)
            .Select(m => FirstValue(m.Value))
            .FirstOrDefault();

    private static LogicalRange RangeToLogicalRange(
        Range range,
        Dictionary<string, string> canvasIdToLocalPath,
        Dictionary<string, string?> fragmentIndex,
        bool isRoot)
    {
        // ManifestBuilder embeds Type, Name, id as metadata for lossless round-tripping.
        // If present, use them; otherwise this is an externally-authored range.
        var metaId   = MetaValue(range, "id");
        var metaType = MetaValue(range, "Type");
        var metaName = MetaValue(range, "Name");

        string id, type;
        string? name;
        if (metaId != null)
        {
            id   = metaId;
            type = metaType ?? (isRoot ? "Collection" : "Item");
            name = string.IsNullOrEmpty(metaName) ? null : metaName;
        }
        else
        {
            id   = NewRangeId();
            type = isRoot ? "Collection" : "Item";
            name = FirstValue(range.Label) is { Length: > 0 } l ? l : null;
        }

        var logicalRange = new LogicalRange { Id = id, Type = type, Name = name };

        var restrictions = (range.Metadata ?? [])
            .Where(m => FirstValue(m.Label) == "access restriction")
            .Select(m => FirstValue(m.Value))
            .Where(v => v != null)
            .Select(v => v!)
            .ToList();
        if (restrictions.Count > 0)
            logicalRange.AccessRestrictions = restrictions;

        if (range.Rights != null && Uri.TryCreate(range.Rights, UriKind.Absolute, out var rightsUri))
            logicalRange.RightsStatement = rightsUri;

        var recordIdentifiers = (range.Metadata ?? [])
            .Where(m => FirstValue(m.Label)?.StartsWith(RecordIdentifierPrefix) == true)
            .Select(m => new RecordIdentifier
            {
                Source = FirstValue(m.Label)![RecordIdentifierPrefix.Length..],
                Value = FirstValue(m.Value) ?? ""
            })
            .ToList();
        if (recordIdentifiers.Count > 0)
            logicalRange.RecordInfo = new RecordInfo { RecordIdentifiers = recordIdentifiers };

        foreach (var item in range.Items ?? [])
        {
            if (item is Range childRange)
            {
                logicalRange.Ranges.Add(RangeToLogicalRange(childRange, canvasIdToLocalPath, fragmentIndex, isRoot: false));
            }
            else
            {
                var fp = ExtractFilePointer(item, canvasIdToLocalPath, fragmentIndex);
                if (fp != null) logicalRange.Files.Add(fp);
            }
        }

        return logicalRange;
    }

    private static FilePointer? ExtractFilePointer(
        IStructuralLocation item,
        Dictionary<string, string> canvasIdToLocalPath,
        Dictionary<string, string?> fragmentIndex)
    {
        string? canvasId;
        string? fragment;

        if (item is Canvas canvas)
        {
            var id = canvas.Id ?? "";
            var hashIdx = id.IndexOf('#');
            canvasId = hashIdx >= 0 ? id[..hashIdx] : id;
            fragment = hashIdx >= 0 ? id[(hashIdx + 1)..] : null;
        }
        else if (item is SpecificResource sr)
        {
            canvasId = (sr.Source as Canvas)?.Id;
            // iiif-net 0.3.13 does not model FragmentSelector; fall back to raw JSON index
            fragmentIndex.TryGetValue(canvasId ?? "", out fragment);
        }
        else
        {
            return null;
        }

        if (canvasId == null || !canvasIdToLocalPath.TryGetValue(canvasId, out var localPath))
            return null;

        return BuildFilePointer(localPath, fragment);
    }

    private static FilePointer BuildFilePointer(string localPath, string? fragment)
    {
        var fp = new FilePointer { LocalPath = localPath };
        if (string.IsNullOrWhiteSpace(fragment)) return fp;

        // Fragment is media-frags format: "t=0,10" or "xywh=0,0,100,100" or combined with "&"
        foreach (var part in fragment.Split('&'))
        {
            if (part.StartsWith("t=", StringComparison.Ordinal))
            {
                var tParts = part[2..].Split(',');
                if (tParts.Length >= 1 && double.TryParse(tParts[0], out var begin))
                    fp.BeginTime = begin;
                if (tParts.Length >= 2 && double.TryParse(tParts[1], out var end))
                    fp.EndTime = end;
            }
            else if (part.StartsWith("xywh=", StringComparison.Ordinal))
            {
                var xywh = part[5..].Split(',');
                if (xywh.Length == 4 &&
                    int.TryParse(xywh[0], out var x) && int.TryParse(xywh[1], out var y) &&
                    int.TryParse(xywh[2], out var w) && int.TryParse(xywh[3], out var h))
                {
                    fp.Region = new Rectangle { X1 = x, Y1 = y, X2 = x + w, Y2 = y + h };
                }
            }
        }

        return fp;
    }

    /// <summary>
    /// Walk the raw JSON structures array and build a map from canvas source ID → fragment value string.
    /// This is the fallback for FragmentSelector, which iiif-net 0.3.13 does not deserialise.
    /// </summary>
    private static Dictionary<string, string?> BuildFragmentIndex(string rawManifestJson)
    {
        var index = new Dictionary<string, string?>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(rawManifestJson);
        if (doc.RootElement.TryGetProperty("structures", out var structures))
            IndexSpecificResources(structures, index);
        return index;
    }

    private static void IndexSpecificResources(JsonElement items, Dictionary<string, string?> index)
    {
        if (items.ValueKind != JsonValueKind.Array) return;
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var typeProp)) continue;
            switch (typeProp.GetString())
            {
                case "Range":
                    if (item.TryGetProperty("items", out var children))
                        IndexSpecificResources(children, index);
                    break;

                case "SpecificResource":
                    string? sourceId = null;
                    if (item.TryGetProperty("source", out var src))
                    {
                        sourceId = src.ValueKind == JsonValueKind.String
                            ? src.GetString()
                            : src.TryGetProperty("id", out var sid) ? sid.GetString() : null;
                    }
                    if (sourceId != null)
                        index[sourceId] = GetFragmentSelectorValue(item);
                    break;
            }
        }
    }

    private static string? GetFragmentSelectorValue(JsonElement specificResource)
    {
        if (!specificResource.TryGetProperty("selector", out var sel)) return null;

        if (sel.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in sel.EnumerateArray())
            {
                var v = FragmentValueFrom(s);
                if (v != null) return v;
            }
            return null;
        }

        return FragmentValueFrom(sel);
    }

    private static string? FragmentValueFrom(JsonElement selector)
    {
        if (!selector.TryGetProperty("type", out var t) || t.GetString() != "FragmentSelector") return null;
        return selector.TryGetProperty("value", out var v) ? v.GetString() : null;
    }

    private static string? FirstValue(LanguageMap? map) =>
        map?.Values.FirstOrDefault()?.FirstOrDefault();
}
