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
using Newtonsoft.Json.Linq;
using Range = IIIF.Presentation.V3.Range;

namespace Preservation.API.IIIF;

public class ManifestBuilder
{
    public void MakeCanvasesAndRanges(
        Manifest manifest,
        MetsFileWrapper wrapper,
        string plainBaseUrl,
        string? mediaServerBaseUrl,
        Func<WorkingFile, string>? originUriResolver = null)
    {
        var canvasMap = new Dictionary<string, Canvas>();
        var linkTargets = GetLinkTargetPaths(wrapper);

        manifest.Items = [];
        foreach (var file in wrapper.Files)
        {
            if (!file.LocalPath.StartsWith("objects/"))
                continue;
            if (file.ContentType.IsNullOrWhiteSpace())
                continue;

            if (linkTargets.Contains(file.LocalPath))
            {
                // Adjunct file (e.g. transcript target) — skipped as a canvas; served via file/ URL.
                continue;
            }

            var canvas = MakeCanvas(file, wrapper, plainBaseUrl, mediaServerBaseUrl, originUriResolver);
            manifest.Items.Add(canvas);
            canvasMap[file.LocalPath] = canvas;
        }

        if (wrapper.LogicalStructures.Count > 0)
        {
            foreach (var range in wrapper.LogicalStructures)
            {
                manifest.Structures ??= [];
                manifest.Structures.Add(MakeRange(range, canvasMap, $"{plainBaseUrl}ranges/"));
            }
        }

        manifest.EnsurePresentation3Context();
    }

    private static HashSet<string> GetLinkTargetPaths(MetsFileWrapper wrapper) =>
        wrapper.Files.SelectMany(f => f.Links.Select(link => link.To)).ToHashSet();

    private static Canvas MakeCanvas(
        WorkingFile file,
        MetsFileWrapper wrapper,
        string plainBaseUrl,
        string? mediaServerBaseUrl,
        Func<WorkingFile, string>? originUriResolver)
    {
        var escapedLocal = file.LocalPath.EscapePathElements();
        var canvas = new Canvas
        {
            Id = $"{plainBaseUrl}canvases/{escapedLocal}",
            Label = new LanguageMap("none", file.LocalPath)
        };
        var extents = file.Metadata.OfType<ExtentMetadata>().SingleOrDefault();
        if (extents != null)
        {
            canvas.Width = extents.PixelWidth;
            canvas.Height = extents.PixelHeight;
            canvas.Duration = extents.Duration;
        }
        var paintingAnno = new PaintingAnnotation
        {
            Id = $"{canvas.Id}/painting/annotation",
            Target = new Canvas { Id = canvas.Id }
        };
        canvas.Items =
        [
            new AnnotationPage
            {
                Id = $"{canvas.Id}/painting",
                Items = [paintingAnno]
            }
        ];

        SetPaintingBody(canvas, paintingAnno, file, escapedLocal, mediaServerBaseUrl, originUriResolver);
        AddLinkAnnotations(canvas, file, wrapper, mediaServerBaseUrl, originUriResolver);
        return canvas;
    }

    // Every canvas is a placeholder (issue #261). The platform holds only the originals - a master
    // TIFF, an archival video - and nothing web-friendly is made from them until a downstream
    // builder registers them with IIIF-CS, so there is nothing to paint but a stand-in image. The
    // rendering link reaches the original.
    //
    // The canvas keeps the file's own extents when it has them, so that range fragments (xywh, t=)
    // stay in the file's coordinates and the real asset can later be painted on the same canvas.
    // The placeholder image is painted over the whole canvas; without extents, the canvas takes
    // the placeholder's size.
    private static void SetPaintingBody(
        Canvas canvas,
        PaintingAnnotation paintingAnno,
        WorkingFile file,
        string escapedLocal,
        string? mediaServerBaseUrl,
        Func<WorkingFile, string>? originUriResolver)
    {
        if (canvas is not { Width: > 0, Height: > 0 })
        {
            canvas.Width = PlaceholderWidth;
            canvas.Height = PlaceholderHeight;
        }
        canvas.Behavior = ["placeholder"];
        paintingAnno.Body = new Image
        {
            Id = $"{mediaServerBaseUrl}placeholder/canvas.png",
            Width = PlaceholderWidth,
            Height = PlaceholderHeight,
            Format = "image/png"
        };
        canvas.Thumbnail =
        [
            new Image
            {
                Id = $"{mediaServerBaseUrl}placeholder/thumb.png",
                Width = 100,
                Height = 80,
                Format = "image/png"
            }
        ];
        canvas.Rendering =
        [
            new ExternalResource(GetDcType(file.ContentType))
            {
                Id = originUriResolver?.Invoke(file) ?? $"{mediaServerBaseUrl}file/{escapedLocal}",
                Format = file.ContentType,
                Behavior = ["original"],
                Label = canvas.Label
            }
        ];
    }

    private const int PlaceholderWidth = 1000;
    private const int PlaceholderHeight = 800;

    // Collect all links into a single AnnotationPage
    private static void AddLinkAnnotations(
        Canvas canvas,
        WorkingFile file,
        MetsFileWrapper wrapper,
        string? mediaServerBaseUrl,
        Func<WorkingFile, string>? originUriResolver)
    {
        var linkItems = new List<IAnnotation>();
        foreach (var fileLink in file.Links)
        {
            var target = wrapper.Files.SingleOrDefault(f => f.LocalPath == fileLink.To);
            if (target == null) continue;
            var bodyId = originUriResolver != null
                ? originUriResolver(target)
                : $"{mediaServerBaseUrl}file/{target.LocalPath.EscapePathElements()}";
            var provides = FileLinkRoles.ToIiifProvides(fileLink.Role);
            var annotation = new GeneralAnnotation("supplementing") // may need
            {
                Body =
                [
                    new ExternalResource(GetDcType(target.ContentType))
                    {
                        Id = bodyId,
                        Label = new LanguageMap("none", $"{bodyId.GetSlug()} ({provides ?? fileLink.Role?.ToString()} for {file.Name})"),
                        Format = target.ContentType
                    }
                ],
                Target = new Canvas { Id = canvas.Id }
            };
            if (provides.HasText())
            {
                annotation.AdditionalProperties["provides"] = new JArray(provides);
            }
            linkItems.Add(annotation);
        }
        if (linkItems.Count > 0)
        {
            canvas.Annotations =
            [
                new AnnotationPage
                {
                    Id = $"{canvas.Id}/annotations",
                    Items = linkItems
                }
            ];
        }
    }

    private static string GetDcType(string? targetContentType)
    {
        if (targetContentType.IsNullOrWhiteSpace())
        {
            return "Dataset";
        }
        if (targetContentType.StartsWith("image/", StringComparison.InvariantCultureIgnoreCase))
        {
            return "Image";
        }
        if (targetContentType.StartsWith("video/", StringComparison.InvariantCultureIgnoreCase))
        {
            return "Video";
        }
        if (targetContentType.StartsWith("audio/", StringComparison.InvariantCultureIgnoreCase))
        {
            return "Sound";
        }
        if (targetContentType.StartsWith("text/", StringComparison.InvariantCultureIgnoreCase)
            || targetContentType.Equals("application/pdf", StringComparison.InvariantCultureIgnoreCase))
        {
            return "Text";
        }
        return "Dataset";
    }

    private Range MakeRange(LogicalRange logicalRange, Dictionary<string, Canvas> canvasMap, string rangeBaseUrl)
    {
        var label = $"{logicalRange.Type}: {logicalRange.Name ?? logicalRange.Id}";
        var iiifRange = new Range
        {
            Id = $"{rangeBaseUrl}{logicalRange.Id}",
            Label = new LanguageMap("en", label),
            Metadata =
            [
                new LabelValuePair("en", "Type", logicalRange.Type),
                new LabelValuePair("en", "Name", logicalRange.Name ?? ""),
                new LabelValuePair("en", "id", logicalRange.Id)
            ]
        };
        if (logicalRange.AccessRestrictions != null)
        {
            iiifRange.Metadata.AddRange(logicalRange.AccessRestrictions.Select(
                a => new LabelValuePair("en", "access restriction", a)));
        }
        if (logicalRange.RightsStatement != null)
            iiifRange.Rights = logicalRange.RightsStatement.ToString();

        if (logicalRange.RecordInfo != null)
        {
            iiifRange.Metadata.AddRange(logicalRange.RecordInfo.RecordIdentifiers.Select(
                r => new LabelValuePair("en", $"record identifier: {r.Source}", r.Value)));
        }

        foreach (var childRange in logicalRange.Ranges)
        {
            iiifRange.Items ??= [];
            iiifRange.Items.Add(MakeRange(childRange, canvasMap, rangeBaseUrl));
        }

        foreach (var filePointer in logicalRange.Files)
        {
            if (!canvasMap.TryGetValue(filePointer.LocalPath, out var canvas))
                continue;

            var canvasRef = new Canvas { Id = canvas.Id };
            var fragment = "";
            if (filePointer.BeginTime > 0)
            {
                fragment = $"t={filePointer.BeginTime}";
                if (filePointer.EndTime > filePointer.BeginTime)
                    fragment += $",{filePointer.EndTime}";
            }
            if (filePointer.Region != null)
            {
                if (fragment.HasText()) fragment += "&";
                fragment +=
                    $"xywh={filePointer.Region.X1},{filePointer.Region.Y1},{filePointer.Region.X2 - filePointer.Region.X1},{filePointer.Region.Y2 - filePointer.Region.Y1}";
            }
            if (fragment.HasText())
                canvasRef.Id += $"#{fragment}";

            iiifRange.Items ??= [];
            iiifRange.Items.Add(canvasRef);
        }

        return iiifRange;
    }
}
