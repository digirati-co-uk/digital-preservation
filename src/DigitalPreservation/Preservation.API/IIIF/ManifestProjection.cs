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
using DigitalPreservation.Common.Model.AccessConditions;
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

    /// <summary>
    /// The OCFL version a file's content was written in (the version in its <c>origin</c> path), for
    /// the IIIF-CS manifest's <c>contentVersion</c>.
    /// </summary>
    public Func<WorkingFile, string?>? ContentVersion { get; init; }

    /// <summary>The Archival Group's current version, the IIIF-CS manifest's <c>objectVersion</c>.</summary>
    public string? ObjectVersion { get; init; }

    /// <summary>The access condition types the installation derives (the Leeds Access Tier).</summary>
    public IReadOnlyCollection<string> DerivedTypes { get; init; } = [];

    /// <summary>
    /// The installation's statement profile, when it has one: what each statement publishes in IIIF,
    /// so a resource carries the same <c>requiredStatement</c> the access dialogue previews.
    /// </summary>
    public StatementProfile? Statements { get; init; }

    /// <summary>
    /// The object's own wording (its <c>requiredStatement</c>), worked out before anything else is
    /// projected, so canvases and ranges inherit it and carry their own only where they differ.
    /// </summary>
    internal (string Label, string Text)? ObjectRequiredStatement { get; set; }

    /// <summary>From <see cref="IiifOptions.RoleConditionTypes"/>: which conditions become role tokens.</summary>
    internal IReadOnlyCollection<string> RoleConditionTypes { get; set; } = [];

    /// <summary>
    /// Files whose ids would differ from another file's only by case (<c>A.tif</c>, <c>a.tif</c>).
    /// IIIF-CS matches canvas ids ignoring case and refuses a manifest with two such ids, so these
    /// get the hash suffix.
    /// </summary>
    internal HashSet<string> CaseCollisions { get; set; } = new(StringComparer.Ordinal);
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
    /// <summary>The media type IIIF Presentation 3 asks a manifest to be served with.</summary>
    public const string ManifestMediaType = "application/ld+json; profile=\"http://iiif.io/api/presentation/3/context.json\"";

    /// <summary>The profile of a METS document in a <c>seeAlso</c>: the METS namespace URI.</summary>
    public const string MetsProfile = "http://www.loc.gov/METS/"; // NOSONAR: S5332 - a namespace name, spelt as the METS standard defines it; nothing connects to it

    public const string AccessConditionsTerm = "accessConditions";
    public const string FormerAssetsTerm = "formerAssets";
    public const string PaintedResourcesProperty = "paintedResources";
    public const string LabelPrefix = "Preservation: ";

    /// <summary>
    /// The prefix of the placeholder roles on IIIF-CS assets. Shaped like a URI, as IIIF-CS roles
    /// are, and plainly not a real one: IIIF-CS doesn't check a role exists when it is assigned, so
    /// an unmapped token is stored, and the asset is then refused to everyone at delivery, rather
    /// than published openly.
    /// </summary>
    public const string RoleTokenPrefix = "urn:x-access-condition:";

    /// <summary>The role token of an asset with none of the role-making conditions.</summary>
    public const string NoConditionsRoleToken = RoleTokenPrefix + "none";

    public const string AccessDiffersFromAssetProperty = "accessDiffersFromAsset";
    public const string ContentVersionProperty = "contentVersion";
    public const string ObjectVersionTerm = "objectVersion";

    private const string En = "en";
    private const string None = "none";

    public void Project(Manifest manifest, MetsFileWrapper mets, ProjectionContext context)
    {
        var linkTargets = mets.Files.SelectMany(f => f.Links.Select(l => l.To)).ToHashSet(StringComparer.Ordinal);
        var canvasIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var paintedResources = new JArray();

        // The object as a whole: the platform records object-level metadata on objects/ (the
        // root is not editable), and objects/'s effective values include anything set on the root.
        var objectLevel = mets.PhysicalStructure?.Directories.FirstOrDefault(d => d.LocalPath == FolderNames.Objects)
                          ?? mets.PhysicalStructure;
        context.ObjectRequiredStatement = objectLevel == null ? null : RequiredStatementFor(objectLevel, context);
        context.RoleConditionTypes = options.Value.RoleConditionTypes;
        context.CaseCollisions = mets.Files
            .Select(f => f.LocalPath)
            .GroupBy(path => IdFor(path, context).ToLowerInvariant(), StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g)
            .ToHashSet(StringComparer.Ordinal);

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
            var range = MakeRange(logicalRange, canvasIds, context);
            if (range != null)
            {
                manifest.Structures ??= [];
                manifest.Structures.Add(range);
            }
        }
        var folders = MakeFolderRange(mets, canvasIds, context);
        if (folders != null)
        {
            manifest.Structures ??= [];
            manifest.Structures.Add(folders);
        }

        if (objectLevel != null)
        {
            SetAccessConditions(manifest, objectLevel, context);
            AddRecordIdentifiers(manifest, objectLevel.EffectiveRecordInfo);
        }
        if (context.Kind == ManifestKind.IiifCs)
        {
            manifest.AdditionalProperties[PaintedResourcesProperty] = paintedResources;
            if (context.ObjectVersion.HasText())
            {
                manifest.AdditionalProperties[ObjectVersionTerm] = context.ObjectVersion;
            }
        }
        SetContext(manifest, context.Kind);
    }

    /// <summary>
    /// The canvas or asset id of a file: <see cref="IiifIds.ForPath"/> of its full path, with the
    /// hash suffix when its id would otherwise differ from another file's only by case.
    /// </summary>
    public static string IdFor(string localPath, ProjectionContext context) =>
        IiifIds.ForPath(context.PathPrefix.HasText() ? $"{context.PathPrefix.TrimEnd('/')}/{localPath}" : localPath,
            withHash: context.CaseCollisions.Contains(localPath));

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
            if (IsAlto(link, target))
            {
                // OCR is not a transcript to show beside the page but text to index and search:
                // a seeAlso with the ALTO profile, which is what text services look for.
                var seeAlso = LinkedFileBody(target, bodyId, context);
                seeAlso.Profile = FileLinkProfiles.Alto;
                canvas.SeeAlso ??= [];
                canvas.SeeAlso.Add(seeAlso);
                continue;
            }
            var provides = FileLinkRoles.ToIiifProvides(link.Role);
            var annotation = new GeneralAnnotation("supplementing")
            {
                Id = $"{canvas.Id}/annotations/{items.Count}",
                Body =
                [
                    LinkedFileBody(target, bodyId, context)
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

    // A linked file needs access control as much as the file it belongs to, so it carries its own
    // effective access conditions: usually the same, by inheritance, but not necessarily.
    private static ExternalResource LinkedFileBody(WorkingFile target, string bodyId, ProjectionContext context)
    {
        var body = new ExternalResource(GetDcType(target.ContentType))
        {
            Id = bodyId,
            Label = new LanguageMap(None, target.Name ?? target.LocalPath.GetSlug() ?? target.LocalPath),
            Format = target.ContentType
        };
        var conditions = AccessConditionsOf(target, context);
        if (conditions.Count > 0)
        {
            body.AdditionalProperties[AccessConditionsTerm] = conditions;
        }
        return body;
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
        // The conditions beside the roles made from them, as on an adjunct, so a builder needn't go
        // to the canvas to see why an asset has the tokens it has. The canvas keeps its copy.
        var conditions = AccessConditionsOf(file, context);
        if (conditions.Count > 0)
        {
            asset[AccessConditionsTerm] = conditions;
        }
        asset["roles"] = RoleTokens(conditions, context);
        SetContentVersion(asset, file, context);
        var adjuncts = new JArray();
        foreach (var link in file.Links)
        {
            var target = mets.Files.FirstOrDefault(f => f.LocalPath == link.To);
            if (target != null)
            {
                adjuncts.Add(MakeAdjunct(target, link, conditions, context));
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
    //
    // It carries its own access conditions and placeholder roles. IIIF-CS gives an adjunct its
    // asset's access today, so where they differ accessDiffersFromAsset says so, for the builder to
    // deal with as it sees fit; if IIIF-CS comes to support roles on adjuncts, they are ready.
    private static JObject MakeAdjunct(WorkingFile target, FileLink link, JArray assetConditions, ProjectionContext context)
    {
        var adjunct = new JObject
        {
            ["id"] = IdFor(target.LocalPath, context),
            ["@type"] = GetDcType(target.ContentType),
            ["mediaType"] = target.ContentType ?? "application/octet-stream",
            ["label"] = new JObject { [None] = new JArray(target.Name ?? target.LocalPath.GetSlug() ?? target.LocalPath) }
        };
        var alto = IsAlto(link, target);
        if (alto)
        {
            // IIIF-CS publishes it as a seeAlso with this profile, which its text pipeline indexes.
            adjunct["iiifLink"] = "seeAlso";
            adjunct["profile"] = FileLinkProfiles.Alto;
        }
        else
        {
            adjunct["iiifLink"] = "inlineAnnotation";
            adjunct["motivation"] = "supplementing";
        }
        var origin = context.Origin?.Invoke(target);
        if (origin.HasText())
        {
            adjunct["origin"] = origin;
        }
        var provides = FileLinkRoles.ToIiifProvides(link.Role);
        if (!alto && provides.HasText() && provides != "supplementing")
        {
            adjunct["provides"] = provides;
        }
        SetContentVersion(adjunct, target, context);
        var conditions = AccessConditionsOf(target, context);
        if (conditions.Count > 0)
        {
            adjunct[AccessConditionsTerm] = conditions;
        }
        adjunct["roles"] = RoleTokens(conditions, context);
        if (!SameConditions(conditions, assetConditions))
        {
            adjunct[AccessDiffersFromAssetProperty] = true;
        }
        return adjunct;
    }

    // The version a file's bytes were written in. A builder compares it with the manifest's
    // objectVersion: equal means the content changed in this version, so IIIF-CS should ingest it
    // again (until dlcs/iiif-presentation#727, by sending reingest).
    private static void SetContentVersion(JObject assetOrAdjunct, WorkingFile file, ProjectionContext context)
    {
        var version = context.ContentVersion?.Invoke(file);
        if (version.HasText())
        {
            assetOrAdjunct[ContentVersionProperty] = version;
        }
    }

    // The same conditions, in any order: inheritance can reach the same set by different routes,
    // in a different order, and that is not a difference a builder needs to act on.
    private static bool SameConditions(JArray a, JArray b)
    {
        static HashSet<string> Set(JArray conditions) =>
            conditions.Select(c => c.ToString(Newtonsoft.Json.Formatting.None)).ToHashSet(StringComparer.Ordinal);
        return Set(a).SetEquals(Set(b));
    }

    /// <summary>
    /// Whether a linked file is OCR: the METS said so (its fileGrp), or format identification did.
    /// Its media type alone can't tell: it is just XML.
    /// </summary>
    private static bool IsAlto(FileLink link, WorkingFile target)
    {
        if (link.Profile?.Contains("alto", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }
        var formatName = target.GetFileFormatMetadata()?.FormatName;
        return formatName != null
               && (formatName.Contains("ALTO", StringComparison.Ordinal)
                   || formatName.Contains("Analyzed Layout and Text Object", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Placeholder roles for an asset or adjunct, from its access conditions: one
    /// <c>urn:x-access-condition:{type}:{value}</c> per condition of the configured types (both parts
    /// percent-encoded), or <see cref="NoConditionsRoleToken"/> when there is none. A builder maps each
    /// token to a real IIIF-CS role URI, or drops it for open access. A token left unmapped is stored
    /// as a role no one has, so the file is refused at delivery rather than published openly.
    /// </summary>
    private static JArray RoleTokens(JArray conditions, ProjectionContext context)
    {
        var types = context.RoleConditionTypes;
        var tokens = conditions
            .Select(c => (Type: c["type"]!.ToString(), Value: c["value"]!.ToString()))
            .Where(c => types.Count > 0 ? types.Contains(c.Type) : c.Type != UseAndReproduction)
            .Select(c => $"{RoleTokenPrefix}{Uri.EscapeDataString(c.Type)}:{Uri.EscapeDataString(c.Value)}")
            .Distinct()
            .ToList();
        return tokens.Count > 0 ? new JArray(tokens) : new JArray(NoConditionsRoleToken);
    }

    // ─── ranges ─────────────────────────────────────────────────────────────────

    // Null when nothing in the range resolves to a canvas (its files are all linked files, have no
    // media type, or aren't there): a Range must have items.
    private Range? MakeRange(LogicalRange logicalRange, Dictionary<string, string> canvasIds, ProjectionContext context)
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
            var childRange = MakeRange(child, canvasIds, context);
            if (childRange != null)
            {
                range.Items ??= [];
                range.Items.Add(childRange);
            }
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
        return range.Items is { Count: > 0 } ? range : null;
    }

    private static string Fragment(FilePointer pointer)
    {
        var parts = new List<string>(2);
        // A part from the very start (BEGIN 00:00:00) is still a part: only a pointer with no
        // times at all, or one from 0 with no end, is the whole file. An end at or before the
        // begin makes no sense, so no time fragment is given rather than a wrong one.
        var begin = pointer.BeginTime ?? 0;
        if (begin >= 0 && pointer.EndTime.HasValue)
        {
            if (pointer.EndTime > begin)
            {
                parts.Add($"t={Seconds(begin)},{Seconds(pointer.EndTime.Value)}");
            }
        }
        else if (begin > 0)
        {
            parts.Add($"t={Seconds(begin)}");
        }
        // Likewise a region whose corners are the wrong way round, or that has no area.
        if (pointer.Region is { } r && r.X1 >= 0 && r.Y1 >= 0 && r.X2 > r.X1 && r.Y2 > r.Y1)
        {
            parts.Add($"xywh={r.X1},{r.Y1},{r.X2 - r.X1},{r.Y2 - r.Y1}");
        }
        return parts.Count > 0 ? "#" + string.Join("&", parts) : "";
    }

    // Plain decimal, never an exponent (a double prints 0.00001 as 1E-05), to the millisecond.
    private static string Seconds(double value) =>
        Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);

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
        if (source.EffectiveRightsStatement != null)
        {
            resource.Rights = source.EffectiveRightsStatement.ToString();
        }
        SetRequiredStatement(resource, source, context);
        var conditions = AccessConditionsOf(source, context);
        if (conditions.Count > 0)
        {
            resource.AdditionalProperties[AccessConditionsTerm] = conditions;
        }
    }

    /// <summary>
    /// A resource's effective access conditions, in one order everywhere they appear (canvases,
    /// ranges, the manifest, linked files, adjuncts) and the same values the Preservation UI shows.
    /// </summary>
    private static JArray AccessConditionsOf(PreservationResource source, ProjectionContext context)
    {
        var conditions = new JArray();
        foreach (var restriction in source.EffectiveAccessRestrictions)
        {
            conditions.Add(Condition(RestrictionOnAccess, restriction, false));
        }
        if (source.EffectiveRightsStatement != null)
        {
            conditions.Add(Condition(UseAndReproduction, source.EffectiveRightsStatement.ToString(), false));
        }
        foreach (var (type, value) in source.EffectiveOtherAccessConditions)
        {
            conditions.Add(Condition(type, value, context.DerivedTypes.Contains(type)));
        }
        return conditions;
    }

    /// <summary>
    /// The wording a public manifest shows for a resource's effective statement (for Leeds, its
    /// copyright statement), from the statement's IIIF entry in the profile: what the access
    /// dialogue's "Preview IIIF rights" shows. The manifest carries the object's. A canvas or range
    /// carries its own only where it differs - IIIF applies the manifest's to everything in it - so
    /// the wording, which is looked up and not what a builder decides access by, isn't repeated on
    /// every canvas. A canvas's statement itself is always in its accessConditions.
    /// </summary>
    private static void SetRequiredStatement(IiifResource resource, PreservationResource source, ProjectionContext context)
    {
        var required = RequiredStatementFor(source, context);
        if (required == null || (resource is not Manifest && required == context.ObjectRequiredStatement))
        {
            return;
        }
        resource.RequiredStatement = new LabelValuePair(
            new LanguageMap(En, required.Value.Label), new LanguageMap(En, required.Value.Text));
    }

    /// <summary>Nothing when there is no statement, or the profile gives its entry no text.</summary>
    private static (string Label, string Text)? RequiredStatementFor(PreservationResource source, ProjectionContext context)
    {
        var statements = context.Statements;
        if (statements == null || !source.EffectiveOtherAccessConditions.TryGetValue(statements.Type, out var statement))
        {
            return null;
        }
        var entry = statements.IiifRightsFor(statement);
        if (entry?.RequiredStatement is not { Length: > 0 } text)
        {
            return null;
        }
        return (entry.RequiredStatementLabel ?? statements.Iiif?.RequiredStatementLabel ?? "Rights", text);
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
            terms[ObjectVersionTerm] = new JObject
            {
                ["@id"] = iiif.ExtensionNamespace + ObjectVersionTerm
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
