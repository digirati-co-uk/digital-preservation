using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Mets;
using DigitalPreservation.Utils;
using IIIF;
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
        const string none = "none";
        var canvasMap = new Dictionary<string, Canvas>();

        // Establish which files are targets of links before building canvases
        var linkTargets = new Dictionary<string, List<WorkingFile>>();
        foreach (var file in wrapper.Files)
        {
            foreach (var to in file.Links.Select(fileLink => fileLink.To))
            {
                if (!linkTargets.TryGetValue(to, out var from))
                {
                    from = [];
                    linkTargets[to] = from;
                }
                from.Add(file);
            }
        }

        manifest.Items = [];
        foreach (var file in wrapper.Files)
        {
            if (!file.LocalPath.StartsWith("objects/"))
                continue;
            if (file.ContentType.IsNullOrWhiteSpace())
                continue;

            if (linkTargets.TryGetValue(file.LocalPath, out _))
            {
                // Adjunct file (e.g. transcript target) — skipped as a canvas; served via file/ URL.
                continue;
            }

            var escapedLocal = file.LocalPath.EscapePathElements();
            var canvas = new Canvas
            {
                Id = $"{plainBaseUrl}canvases/{escapedLocal}",
                Label = new LanguageMap(none, file.LocalPath)
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

            // Image in Deposit: IIIF image service for mediaServer, scaled JPEG + thumbnail
            if (file.ContentType.StartsWith("image/") && canvas is { Width: > 0, Height: > 0 } && originUriResolver == null)
            {
                var size = new Size(canvas.Width.Value, canvas.Height.Value);
                var mainSize = Size.Confine(1200, size);
                var thumbSize = Size.Confine(100, size);
                paintingAnno.Body = new Image
                {
                    Id = $"{mediaServerBaseUrl}imagesvc/{escapedLocal}/full/{mainSize.Width},{mainSize.Height}/0/default.jpg",
                    Width = mainSize.Width,
                    Height = mainSize.Height,
                    Format = "image/jpeg"
                };
                canvas.Thumbnail =
                [
                    new Image
                    {
                        Id = $"{mediaServerBaseUrl}imagesvc/{escapedLocal}/full/{thumbSize.Width},{thumbSize.Height}/0/default.jpg",
                        Width = thumbSize.Width,
                        Height = thumbSize.Height,
                        Format = "image/jpeg"
                    }
                ];
            }
            else if (file.ContentType.StartsWith("image/") && originUriResolver != null)
            {
                paintingAnno.Body = new Image
                {
                    Id = originUriResolver(file),
                    Width = canvas.Width,
                    Height = canvas.Height,
                    Format = file.ContentType
                };
            }
            // Video/audio: body type, structure, and rendering are the same in both modes — only the ID differs.
            else if (file.ContentType.StartsWith("video/") && canvas is { Width: > 0, Height: > 0, Duration: > 0 })
            {
                var id = originUriResolver?.Invoke(file) ?? $"{mediaServerBaseUrl}video/{escapedLocal}";
                paintingAnno.Body = new Video
                {
                    Id = id,
                    Width = canvas.Width,
                    Height = canvas.Height,
                    Duration = canvas.Duration,
                    Format = file.ContentType
                };
            }
            else if (file.ContentType.StartsWith("audio/") && canvas is { Duration: > 0 })
            {
                var id = originUriResolver?.Invoke(file) ?? $"{mediaServerBaseUrl}audio/{escapedLocal}";
                paintingAnno.Body = new Sound
                {
                    Id = id,
                    Duration = canvas.Duration,
                    Format = file.ContentType
                };
            }
            // Placeholder: ExternalResource has no extents, there is no painting body. —
            // the canvas carries only a rendering link pointing to the binary.
            else
            {
                canvas.Behavior = ["placeholder"];
                var id = originUriResolver?.Invoke(file) ?? $"{mediaServerBaseUrl}file/{escapedLocal}";
                paintingAnno.Body = new Image
                {
                    Id = $"{mediaServerBaseUrl}placeholder/canvas.png",
                    Width = 1000,
                    Height = 800,
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
                    new ExternalResource("Text")
                    {
                        Id = id,
                        Format = file.ContentType,
                        Behavior = ["original"],
                        Label = canvas.Label
                    }
                ];
            }


            // Collect all links into a single AnnotationPage
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
