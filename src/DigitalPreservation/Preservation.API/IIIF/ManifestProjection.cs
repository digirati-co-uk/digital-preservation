using System.Globalization;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Mets;
using DigitalPreservation.Utils;
using IIIF.Presentation;
using IIIF.Presentation.V3;
using IIIF.Presentation.V3.Annotation;
using IIIF.Presentation.V3.Content;
using IIIF.Presentation.V3.Strings;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Range = IIIF.Presentation.V3.Range;
using IiifResource = IIIF.Presentation.V3.ResourceBase;
using PreservationResource = DigitalPreservation.Common.Model.Transit.ResourceBase;
using static DigitalPreservation.Common.Model.AccessConditions.AccessConditionProfile.ReservedTypes;

namespace Preservation.API.IIIF;

/// <summary>The two kinds of manifest the projection makes (issue #261).</summary>
public enum ManifestKind
{
    /// <summary>
    /// Plain IIIF Presentation 3, for Deposits and Archival Groups. Every canvas is a placeholder.
    /// </summary>
    Vanilla,

    /// <summary>
    /// For IIIF Cloud Services, with <c>paintedResources</c>: Archival Groups only, because it names
    /// each file's location in preservation storage. IIIF-CS paints the canvases.
    /// </summary>
    IiifCs
}

/// <summary>What the projection needs to know beyond the METS.</summary>
public sealed class ProjectionContext
{
    public required ManifestKind Kind { get; init; }

    /// <summary>Base (trailing slash) of range ids, and of canvas ids in a vanilla manifest.</summary>
    public required string IdBase { get; init; }

    /// <summary>Base (trailing slash) of the placeholder images: <c>{root}/placeholders/</c>.</summary>
    public required string PlaceholderBase { get; init; }

    /// <summary>
    /// The path under <c>/repository</c> that a file's local path is appended to before
    /// <see cref="IiifIds.ForPath"/>: the Archival Group's path. For a Deposit, the path of the
    /// Archival Group it will become, when it has one.
    /// </summary>
    public string? PathPrefix { get; init; }

    /// <summary>
    /// The URI a vanilla Archival Group canvas links to as its <c>rendering</c> (the Binary's
    /// <c>/repository</c> URI). Null for a Deposit, whose files are not reachable from a manifest.
    /// </summary>
    public Func<WorkingFile, string?>? Rendering { get; init; }

    /// <summary>The <c>origin</c> of a file's asset in the IIIF-CS manifest.</summary>
    public Func<WorkingFile, string?>? Origin { get; init; }

    /// <summary>The access condition types the installation derives (the Leeds Access Tier).</summary>
    public IReadOnlyCollection<string> DerivedTypes { get; init; } = [];
}

/// <summary>
/// Projects a METS file as a IIIF Manifest for a downstream builder (issue #261; the design is on
/// the issue). The METS supplies everything: its physical structure the canvases, its logical
/// structMaps and folders the ranges, its sm:links the annotations or adjuncts, and its effective
/// access conditions - resolved for every file, as the Preservation UI shows them - the
/// <c>accessConditions</c> on each canvas and range.
/// </summary>
public class ManifestProjection(IOptions<IiifOptions> options)
{
    public const string AccessConditionsTerm = "accessConditions";
    public const string FormerAssetsTerm = "formerAssets";
    public const string PaintedResourcesProperty = "paintedResources";
    public const string LabelPrefix = "Preservation: ";

    private const string En = "en";
    private const string None = "none";

    public void Project(Manifest manifest, MetsFileWrapper mets, ProjectionContext context)
    {
        var linkTargets = mets.Files.SelectMany(f => f.Links.Select(l => l.To)).ToHashSet(StringComparer.Ordinal);
        var canvasIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var paintedResources = new JArray();

        manifest.Items = [];
        foreach (var file in mets.Files)
        {
            if (!IsCanvasFile(file, linkTargets))
            {
                continue;
            }
            var assetId = IdFor(file.LocalPath, context);
            var canvas = MakeCanvas(file, assetId, mets, context);
            canvasIds[file.LocalPath] = canvas.Id!;
            if (context.Kind == ManifestKind.IiifCs)
            {
                paintedResources.Add(MakePaintedResource(file, assetId, canvas.Id!, manifest.Items.Count, mets, context));
            }
            manifest.Items.Add(canvas);
        }

        foreach (var logicalRange in mets.LogicalStructures)
        {
            manifest.Structures ??= [];
            manifest.Structures.Add(MakeRange(logicalRange, canvasIds, context));
        }
        var folders = MakeFolderRange(mets, canvasIds, context);
        if (folders != null)
        {
            manifest.Structures ??= [];
            manifest.Structures.Add(folders);
        }

        // The object as a whole: the platform records object-level metadata on objects/ (the
        // root is not editable), and objects/'s effective values include anything set on the root.
        var objectLevel = mets.PhysicalStructure?.Directories.FirstOrDefault(d => d.LocalPath == FolderNames.Objects)
                          ?? mets.PhysicalStructure;
        if (objectLevel != null)
        {
            SetAccessConditions(manifest, objectLevel, context);
            AddRecordIdentifiers(manifest, objectLevel.EffectiveRecordInfo);
        }
        if (context.Kind == ManifestKind.IiifCs)
        {
            manifest.AdditionalProperties[PaintedResourcesProperty] = paintedResources;
        }
        SetContext(manifest, context.Kind);
    }

    /// <summary>The canvas or asset id of a file: <see cref="IiifIds.ForPath"/> of its full path.</summary>
    public static string IdFor(string localPath, ProjectionContext context) =>
        IiifIds.ForPath(context.PathPrefix.HasText() ? $"{context.PathPrefix.TrimEnd('/')}/{localPath}" : localPath);

    private static bool IsCanvasFile(WorkingFile file, HashSet<string> linkTargets) =>
        file.LocalPath.StartsWith($"{FolderNames.Objects}/")
        && file.ContentType.HasText()
        && !linkTargets.Contains(file.LocalPath);

    // ─── canvases ───────────────────────────────────────────────────────────────

    private Canvas MakeCanvas(WorkingFile file, string assetId, MetsFileWrapper mets, ProjectionContext context)
    {
        var canvas = new Canvas
        {
            Id = context.Kind == ManifestKind.Vanilla ? $"{context.IdBase}canvases/{assetId}" : assetId,
            Label = new LanguageMap(None, file.Name ?? file.LocalPath.GetSlug() ?? file.LocalPath),
            Metadata = FileMetadata(file)
        };
        AddRecordIdentifiers(canvas, file.EffectiveRecordInfo);
        SetAccessConditions(canvas, file, context);

        if (context.Kind == ManifestKind.Vanilla)
        {
            PaintPlaceholder(canvas, file, context);
            AddLinkAnnotations(canvas, file, mets, context);
        }
        // In the IIIF-CS manifest a canvas carries no painting, extents or rendering: IIIF-CS
        // paints the asset (a matched canvas with painting content is refused), takes the extents
        // from it, and the builder chooses its delivery channels.
        return canvas;
    }

    // The canvas keeps the file's own extents, so a range's xywh or t= fragment means the same on
    // the canvas as in the file, and a thumbnail can later be painted in the placeholder's place
    // without the canvas changing. The placeholder has the same aspect ratio, within limits.
    private static void PaintPlaceholder(Canvas canvas, WorkingFile file, ProjectionContext context)
    {
        var extents = file.Metadata.OfType<ExtentMetadata>().FirstOrDefault();
        var size = Placeholders.SizeFor(extents?.PixelWidth, extents?.PixelHeight);
        canvas.Width = extents?.PixelWidth is > 0 && extents.PixelHeight is > 0 ? extents.PixelWidth : size.Width;
        canvas.Height = extents?.PixelWidth is > 0 && extents.PixelHeight is > 0 ? extents.PixelHeight : size.Height;
        if (extents?.Duration is > 0)
        {
            canvas.Duration = extents.Duration;
        }
        canvas.Behavior = ["placeholder"];

        var format = file.GetFileFormatMetadata();
        var paintingAnnotation = new PaintingAnnotation
        {
            Id = $"{canvas.Id}/painting/annotation",
            Body = new Image
            {
                Id = $"{context.PlaceholderBase}{Placeholders.CanvasPath(size, format?.PronomKey, file.ContentType)}",
                Width = size.Width,
                Height = size.Height,
                Format = "image/png"
            },
            Target = new Canvas { Id = canvas.Id }
        };
        canvas.Items = [new AnnotationPage { Id = $"{canvas.Id}/painting", Items = [paintingAnnotation] }];
        canvas.Thumbnail =
        [
            new Image
            {
                Id = $"{context.PlaceholderBase}{Placeholders.ThumbnailPath(file.ContentType)}",
                Width = Placeholders.ThumbnailSize.Width,
                Height = Placeholders.ThumbnailSize.Height,
                Format = "image/png"
            }
        ];

        var rendering = context.Rendering?.Invoke(file);
        if (rendering.HasText())
        {
            canvas.Rendering =
            [
                new ExternalResource(GetDcType(file.ContentType))
                {
                    Id = rendering,
                    Format = file.ContentType,
                    Behavior = ["original"],
                    Label = canvas.Label
                }
            ];
        }
    }

    // Vanilla: each sm:link becomes a supplementing annotation on the canvas, carrying "provides".
    private static void AddLinkAnnotations(Canvas canvas, WorkingFile file, MetsFileWrapper mets, ProjectionContext context)
    {
        var items = new List<IAnnotation>();
        foreach (var link in file.Links)
        {
            var target = mets.Files.FirstOrDefault(f => f.LocalPath == link.To);
            if (target == null)
            {
                continue;
            }
            // A Deposit's files are not reachable from its manifest, so the body is identified, not linked.
            var bodyId = context.Rendering?.Invoke(target) ?? $"{context.IdBase}files/{IdFor(target.LocalPath, context)}";
            var provides = FileLinkRoles.ToIiifProvides(link.Role);
            var annotation = new GeneralAnnotation("supplementing")
            {
                Id = $"{canvas.Id}/annotations/{items.Count}",
                Body =
                [
                    new ExternalResource(GetDcType(target.ContentType))
                    {
                        Id = bodyId,
                        Label = new LanguageMap(None, target.Name ?? target.LocalPath.GetSlug() ?? target.LocalPath),
                        Format = target.ContentType
                    }
                ],
                Target = new Canvas { Id = canvas.Id }
            };
            if (provides.HasText() && provides != "supplementing")
            {
                annotation.AdditionalProperties["provides"] = new JArray(provides);
            }
            items.Add(annotation);
        }
        if (items.Count > 0)
        {
            canvas.Annotations = [new AnnotationPage { Id = $"{canvas.Id}/annotations", Items = items }];
        }
    }

    // ─── IIIF-CS ────────────────────────────────────────────────────────────────

    // canvasOrder is explicit and equals the canvas's index in items, which is what IIIF-CS requires
    // of a matched canvas. canvasOriginalId is never sent: IIIF-CS would treat the canvas as
    // items-only, with no asset painted.
    private static JObject MakePaintedResource(WorkingFile file, string assetId, string canvasId, int order,
        MetsFileWrapper mets, ProjectionContext context)
    {
        var asset = new JObject
        {
            ["id"] = assetId,
            ["mediaType"] = file.ContentType
        };
        var origin = context.Origin?.Invoke(file);
        if (origin.HasText())
        {
            asset["origin"] = origin;
        }
        var adjuncts = new JArray();
        foreach (var link in file.Links)
        {
            var target = mets.Files.FirstOrDefault(f => f.LocalPath == link.To);
            if (target != null)
            {
                adjuncts.Add(MakeAdjunct(target, link, context));
            }
        }
        if (adjuncts.Count > 0)
        {
            asset["adjuncts"] = adjuncts;
        }
        return new JObject
        {
            ["canvasPainting"] = new JObject
            {
                ["canvasId"] = canvasId,
                ["canvasOrder"] = order
            },
            ["asset"] = asset
        };
    }

    // A linked file (a transcript, OCR) is an adjunct of the asset it belongs to, so IIIF-CS ingests
    // and serves it. "inlineAnnotation" with motivation "supplementing" is how IIIF-CS emits a
    // supplementing annotation whose body is the adjunct.
    private static JObject MakeAdjunct(WorkingFile target, FileLink link, ProjectionContext context)
    {
        var adjunct = new JObject
        {
            ["id"] = IdFor(target.LocalPath, context),
            ["@type"] = GetDcType(target.ContentType),
            ["mediaType"] = target.ContentType ?? "application/octet-stream",
            ["label"] = new JObject { [None] = new JArray(target.Name ?? target.LocalPath.GetSlug() ?? target.LocalPath) },
            ["iiifLink"] = "inlineAnnotation",
            ["motivation"] = "supplementing"
        };
        var origin = context.Origin?.Invoke(target);
        if (origin.HasText())
        {
            adjunct["origin"] = origin;
        }
        var provides = FileLinkRoles.ToIiifProvides(link.Role);
        if (provides.HasText() && provides != "supplementing")
        {
            adjunct["provides"] = provides;
        }
        return adjunct;
    }

    // ─── ranges ─────────────────────────────────────────────────────────────────

    private Range MakeRange(LogicalRange logicalRange, Dictionary<string, string> canvasIds, ProjectionContext context)
    {
        var range = new Range
        {
            Id = $"{context.IdBase}ranges/{logicalRange.Id}",
            Label = new LanguageMap(En, $"{logicalRange.Type}: {logicalRange.Name ?? logicalRange.Id}"),
            Metadata = [Pair("Structure type", logicalRange.Type)]
        };
        if (logicalRange.Name.HasText())
        {
            range.Metadata.Add(Pair("Name", logicalRange.Name));
        }
        AddRecordIdentifiers(range, logicalRange.EffectiveRecordInfo);
        SetAccessConditions(range, logicalRange, context);

        foreach (var child in logicalRange.Ranges)
        {
            range.Items ??= [];
            range.Items.Add(MakeRange(child, canvasIds, context));
        }
        foreach (var pointer in logicalRange.Files)
        {
            if (!canvasIds.TryGetValue(pointer.LocalPath, out var canvasId))
            {
                continue;
            }
            range.Items ??= [];
            range.Items.Add(new Canvas { Id = canvasId + Fragment(pointer) });
        }
        return range;
    }

    private static string Fragment(FilePointer pointer)
    {
        var fragment = "";
        if (pointer.BeginTime > 0)
        {
            fragment = $"t={pointer.BeginTime.Value.ToString(CultureInfo.InvariantCulture)}";
            if (pointer.EndTime > pointer.BeginTime)
            {
                fragment += $",{pointer.EndTime.Value.ToString(CultureInfo.InvariantCulture)}";
            }
        }
        if (pointer.Region != null)
        {
            if (fragment.HasText()) fragment += "&";
            var r = pointer.Region;
            fragment += $"xywh={r.X1},{r.Y1},{r.X2 - r.X1},{r.Y2 - r.Y1}";
        }
        return fragment.HasText() ? $"#{fragment}" : "";
    }

    /// <summary>
    /// The original arrangement of the files below objects/, as a Range - only when there is one:
    /// some canvas file sits in a subfolder. A flat objects/, with or without linked files (ALTO, a
    /// transcript) beside the content, has no folder Range. It is kept alongside any logical
    /// structMaps, as a record of how the files were arranged; consumers can drop it.
    /// </summary>
    private Range? MakeFolderRange(MetsFileWrapper mets, Dictionary<string, string> canvasIds, ProjectionContext context)
    {
        var hasStructure = canvasIds.Keys.Any(p => p[(FolderNames.Objects.Length + 1)..].Contains('/'));
        var objects = mets.PhysicalStructure?.Directories.FirstOrDefault(d => d.LocalPath == FolderNames.Objects);
        return hasStructure && objects != null ? MakeFolderRange(objects, canvasIds, context) : null;
    }

    // Files first, in their METS order, then subfolders; a folder whose files are all linked files
    // (so none has a canvas) is left out.
    private Range? MakeFolderRange(WorkingDirectory directory, Dictionary<string, string> canvasIds, ProjectionContext context)
    {
        var items = new List<IStructuralLocation>();
        foreach (var file in directory.Files)
        {
            if (canvasIds.TryGetValue(file.LocalPath, out var canvasId))
            {
                items.Add(new Canvas { Id = canvasId });
            }
        }
        foreach (var subdirectory in directory.Directories)
        {
            var subRange = MakeFolderRange(subdirectory, canvasIds, context);
            if (subRange != null)
            {
                items.Add(subRange);
            }
        }
        if (items.Count == 0)
        {
            return null;
        }
        var range = new Range
        {
            Id = $"{context.IdBase}ranges/folders/{IdFor(directory.LocalPath, context)}",
            Label = new LanguageMap(None, directory.Name ?? directory.LocalPath.GetSlug() ?? directory.LocalPath),
            Metadata = [Pair("Structure type", "Folder"), Pair("Path", directory.LocalPath)],
            Items = items
        };
        AddRecordIdentifiers(range, directory.EffectiveRecordInfo);
        SetAccessConditions(range, directory, context);
        return range;
    }

    // ─── metadata and access conditions ─────────────────────────────────────────

    private static LabelValuePair Pair(string label, string value) =>
        new(new LanguageMap(En, LabelPrefix + label), new LanguageMap(None, value));

    private static List<LabelValuePair> FileMetadata(WorkingFile file)
    {
        var format = file.GetFileFormatMetadata();
        var storage = file.Metadata.OfType<StorageMetadata>().FirstOrDefault();
        var extents = file.Metadata.OfType<ExtentMetadata>().FirstOrDefault();
        var virus = file.Metadata.OfType<VirusScanMetadata>().FirstOrDefault();

        var pairs = new List<LabelValuePair>();
        void Add(string label, string? value)
        {
            if (value.HasText()) pairs.Add(Pair(label, value));
        }

        // The platform records a file's path as its original name; only a name that differs (from
        // Archivematica, say, or a renamed file) says anything the path doesn't.
        var originalName = storage?.OriginalName ?? format?.OriginalName;
        if (originalName != file.LocalPath)
        {
            Add("Original name", originalName);
        }
        Add("Path", file.LocalPath);
        var size = file.Size ?? format?.Size;
        if (size.HasValue)
        {
            Add("Size", $"{size.Value.ToString("N0", CultureInfo.InvariantCulture)} bytes ({StringUtils.FormatFileSize(size.Value)})");
        }
        Add("Format", format?.FormatName);
        Add("PRONOM key", format?.PronomKey);
        Add("Media type", file.ContentType);
        Add("SHA-256", file.Digest ?? format?.Digest);
        if (virus != null)
        {
            Add("Virus check", VirusCheck(virus));
        }
        if (extents?.PixelWidth is > 0 && extents.PixelHeight is > 0)
        {
            Add("Width", extents.PixelWidth.Value.ToString(CultureInfo.InvariantCulture));
            Add("Height", extents.PixelHeight.Value.ToString(CultureInfo.InvariantCulture));
        }
        if (extents?.Duration is > 0)
        {
            Add("Duration", $"{extents.Duration.Value.ToString(CultureInfo.InvariantCulture)} s");
        }
        return pairs;
    }

    private static string VirusCheck(VirusScanMetadata virus)
    {
        var result = virus.NotScanned ? $"Not fully scanned ({virus.VirusFound})"
            : virus.HasVirus ? $"Virus found: {virus.VirusFound}"
            : "No virus found";
        return $"{result}, {virus.Source}, {virus.Timestamp.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
    }

    private static void AddRecordIdentifiers(IiifResource resource, RecordInfo? recordInfo)
    {
        if (recordInfo == null || recordInfo.RecordIdentifiers.Count == 0)
        {
            return;
        }
        resource.Metadata ??= [];
        foreach (var identifier in recordInfo.RecordIdentifiers)
        {
            var label = identifier.Source.HasText() ? $"Record identifier ({identifier.Source})" : "Record identifier";
            resource.Metadata.Add(Pair(label, identifier.Value));
        }
    }

    /// <summary>
    /// The effective access conditions of a resource as METS records them after inheritance -
    /// including the derived ones (for Leeds, the Access Tier), flagged - and its effective rights
    /// statement as IIIF <c>rights</c>. A builder uses these to register assets with roles, without
    /// keeping its own copy of the inheritance rules.
    /// </summary>
    private static void SetAccessConditions(IiifResource resource, PreservationResource source, ProjectionContext context)
    {
        var conditions = new JArray();
        foreach (var restriction in source.EffectiveAccessRestrictions)
        {
            conditions.Add(Condition(RestrictionOnAccess, restriction, false));
        }
        if (source.EffectiveRightsStatement != null)
        {
            conditions.Add(Condition(UseAndReproduction, source.EffectiveRightsStatement.ToString(), false));
            resource.Rights = source.EffectiveRightsStatement.ToString();
        }
        foreach (var (type, value) in source.EffectiveOtherAccessConditions)
        {
            conditions.Add(Condition(type, value, context.DerivedTypes.Contains(type)));
        }
        if (conditions.Count > 0)
        {
            resource.AdditionalProperties[AccessConditionsTerm] = conditions;
        }
    }

    private static JObject Condition(string type, string value, bool derived)
    {
        var condition = new JObject { ["type"] = type, ["value"] = value };
        if (derived)
        {
            condition["derived"] = true;
        }
        return condition;
    }

    // The extension terms are declared once, in the head: each is a JSON-LD 1.1 scoped term whose
    // own context (the platform's document) applies inside it, so "type" and "value" there are ours,
    // not the Presentation context's. The Presentation context comes last, as IIIF requires.
    private void SetContext(Manifest manifest, ManifestKind kind)
    {
        var iiif = options.Value;
        var terms = new JObject
        {
            [AccessConditionsTerm] = new JObject
            {
                ["@id"] = iiif.ExtensionNamespace + AccessConditionsTerm,
                ["@context"] = iiif.ExtensionContext
            }
        };
        if (kind == ManifestKind.IiifCs)
        {
            terms[FormerAssetsTerm] = new JObject
            {
                ["@id"] = iiif.ExtensionNamespace + FormerAssetsTerm,
                ["@context"] = iiif.ExtensionContext
            };
        }
        manifest.Context = new List<object> { terms, Context.Presentation3Context };
    }

    // Documents a person reads, as opposed to data: a transcript is as likely to be Word as PDF.
    private static readonly HashSet<string> TextDocumentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.oasis.opendocument.text",
        "application/rtf",
        "application/epub+zip"
    };

    private static string GetDcType(string? contentType)
    {
        if (contentType.IsNullOrWhiteSpace()) return "Dataset";
        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return "Image";
        if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return "Video";
        if (contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return "Sound";
        if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || TextDocumentTypes.Contains(contentType.Split(';')[0].Trim())) return "Text";
        return "Dataset";
    }
}
