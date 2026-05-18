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
using Range = IIIF.Presentation.V3.Range;

namespace Preservation.API.IIIF;

public class ManifestBuilder
{
    public void MakeCanvasesAndRanges(Manifest manifest, MetsFileWrapper wrapper, string plainBaseUrl, string mediaServerBaseUrl)
    {
        const string none = "none";
        var canvasMap = new Dictionary<string, Canvas>();

        // Establish which files are targets of links before building canvases
        var linkTargets = new Dictionary<string, List<WorkingFile>>();
        foreach (var file in wrapper.Files)
        {
            foreach (var fileLink in file.Links)
            {
                if (!linkTargets.TryGetValue(fileLink.To, out var from))
                {
                    from = [];
                    linkTargets[fileLink.To] = from;
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

            if (file.ContentType.StartsWith("image/") && canvas is { Width: > 0, Height: > 0 })
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
            else if (file.ContentType.StartsWith("video/") && canvas is { Width: > 0, Height: > 0, Duration: > 0 })
            {
                paintingAnno.Body = new Video
                {
                    Id = $"{mediaServerBaseUrl}video/{escapedLocal}",
                    Width = canvas.Width,
                    Height = canvas.Height,
                    Duration = canvas.Duration,
                    Format = file.ContentType
                };
                canvas.Rendering =
                [
                    new ExternalResource("Video")
                    {
                        Id = $"{mediaServerBaseUrl}video/{escapedLocal}",
                        Format = file.ContentType,
                        Behavior = ["original"],
                        Label = canvas.Label
                    }
                ];
            }
            else if (file.ContentType.StartsWith("audio/") && canvas is { Duration: > 0 })
            {
                paintingAnno.Body = new Sound
                {
                    Id = $"{mediaServerBaseUrl}audio/{escapedLocal}",
                    Duration = canvas.Duration,
                    Format = file.ContentType
                };
                canvas.Rendering =
                [
                    new ExternalResource("Sound")
                    {
                        Id = $"{mediaServerBaseUrl}audio/{escapedLocal}",
                        Format = file.ContentType,
                        Behavior = ["original"],
                        Label = canvas.Label
                    }
                ];
            }
            else
            {
                canvas.Behavior = ["placeholder"];
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
                        Id = $"{mediaServerBaseUrl}file/{escapedLocal}",
                        Format = file.ContentType,
                        Behavior = ["original"],
                        Label = canvas.Label
                    }
                ];
            }

            // Collect all transcript annotations into a single AnnotationPage
            var transcriptItems = new List<IAnnotation>();
            foreach (var fileLink in file.Links)
            {
                var role = fileLink.Role?.ToString();
                if (!role.HasText() || !role.EndsWith("transcript")) continue;
                var target = wrapper.Files.SingleOrDefault(f => f.LocalPath == fileLink.To);
                if (target == null) continue;
                transcriptItems.Add(new GeneralAnnotation("supplementing")
                {
                    Body =
                    [
                        new ExternalResource("Text")
                        {
                            Id = $"{mediaServerBaseUrl}file/{target.LocalPath.EscapePathElements()}",
                            Label = new LanguageMap("en", "Transcript"),
                            Format = target.ContentType
                        }
                    ],
                    Target = new Canvas { Id = canvas.Id }
                });
            }
            if (transcriptItems.Count > 0)
            {
                canvas.Annotations =
                [
                    new AnnotationPage
                    {
                        Id = $"{canvas.Id}/annotations",
                        Items = transcriptItems
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
