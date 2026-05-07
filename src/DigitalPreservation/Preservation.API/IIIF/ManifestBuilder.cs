using System.Security.Cryptography;
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
using Microsoft.Extensions.Caching.Memory;
using Range = IIIF.Presentation.V3.Range;

namespace Preservation.API.IIIF;

public class ManifestBuilder(IMemoryCache memoryCache)
{    
    public static string GetSessionMediaServerBaseUrl(string depositId)
    {
        var token = GetSessionToken();
        return $"{depositId}/media/{token}/";
    }

    private static string GetSessionToken()
    {
        using var sha256 = SHA256.Create();
        var token = Checksum.HashFromString(Guid.NewGuid().ToString("N") + DateTime.Now.Ticks, sha256);
        return token;
    }

    public string GetToken(string key)
    {
        if(memoryCache.TryGetValue(key, out string? token))
        {
            if (token.HasText())
            {
                return token;
            }
        }

        token = GetSessionToken();
        var cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(56)) // assume token is valid for 1 hour
            .SetSlidingExpiration(TimeSpan.FromMinutes(55));
        memoryCache.Set(key, token, cacheEntryOptions);
        memoryCache.Set(token, key, cacheEntryOptions);
        return token;
    }

    public string? GetKey(string token)
    {
        if(memoryCache.TryGetValue(token, out string? key))
        {
            if (key.HasText())
            {
                return key;
            }
        }

        return null;
    }

    public void MakeCanvasesAndRanges(Manifest manifest, MetsFileWrapper wrapper, string plainBaseUrl, string mediaServerBaseUrl)
    {
        const string none = "none";
        var canvasMap = new Dictionary<string, Canvas>();
        
        // establish which files are targets of links before building canvases
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

        // The following can be made to work with deposits and preserved items
        manifest.Items = [];
        foreach (var file in wrapper.Files)
        {
            if (!file.LocalPath.StartsWith("objects/"))
            {
                continue;
            }
            if (file.ContentType.IsNullOrWhiteSpace())
            {
                continue;
            }
            
            if (linkTargets.TryGetValue(file.LocalPath, out _))
            {
                // This file is the target of a link from another file, so we may nopt want to make a Canvas from it.
                // Assume for now it's always an "adjunct" - it may not be in future so the logic will get more
                // complex - there could be links between canvases. We'd examine the _role_ of the link.
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
                    Items = [ paintingAnno ]
                }
            ];
            
            if (file.ContentType.StartsWith("image/") && canvas is { Width: > 0, Height: > 0 })
            {
                var size = new Size(canvas.Width.Value, canvas.Height.Value);
                var mainSize = Size.Confine(1200, size);
                var thumbSize = Size.Confine(100, size);
                paintingAnno.Body = new Image
                {
                    Id = $"{mediaServerBaseUrl}image/{escapedLocal}/full/{mainSize.Width},{mainSize.Height}/0/default.jpg",
                    Width = mainSize.Width,
                    Height = mainSize.Height,
                    Format = "image/jpeg"
                };
                canvas.Thumbnail =
                [
                    new Image
                    {
                        Id = $"{mediaServerBaseUrl}image/{escapedLocal}/full/{thumbSize.Width},{thumbSize.Height}/0/default.jpg",
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
            }
            
            else if (file.ContentType.StartsWith("audio/") && canvas is { Duration: > 0 })
            {
                paintingAnno.Body = new Sound
                {
                    Id = $"{mediaServerBaseUrl}audio/{escapedLocal}",
                    Duration = canvas.Duration,
                    Format = file.ContentType
                };
            }

            else
            {
                canvas.Behavior = ["placeholder"];
                // Not a file with renderable extent
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
                        Id = $"{mediaServerBaseUrl}placeholder/rendering.png",
                        Format = file.ContentType,
                        Behavior = [ "original" ],
                        Label = canvas.Label
                    }
                ];
            }

            foreach (var fileLink in file.Links)
            {
                var role = fileLink.Role?.ToString();
                if (role.HasText() && role.EndsWith("transcript"))
                {
                    canvas.Annotations =
                    [
                        new AnnotationPage
                        {
                            Id = $"{canvas.Id}/annotations",
                            Items = 
                            [
                                new GeneralAnnotation("supplementing")
                                {
                                    Body = 
                                    [
                                        new ExternalResource("Text")
                                        {
                                            Id = $"{mediaServerBaseUrl}file/{fileLink.To.EscapePathElements()}",
                                            Label = new LanguageMap("en", "Transcript"),
                                            Format = "text/vtt"
                                        }
                                    ],
                                    Target = new Canvas { Id = canvas.Id }
                                }
                            ]

                        }
                    ];
                }
            }
            
            manifest.Items.Add(canvas);
            canvasMap[file.LocalPath] = canvas;
        }

        // Now turn logical structMap into ranges
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
            Label = new LanguageMap("en", label)
        };
        foreach (var childRange in logicalRange.Ranges)
        {
            iiifRange.Items ??= [];
            iiifRange.Items.Add(MakeRange(childRange, canvasMap, rangeBaseUrl));
        }

        foreach (var filePointer in logicalRange.Files)
        {
            var canvas = canvasMap[filePointer.LocalPath];
            var canvasRef = new Canvas { Id = canvas.Id };
            var fragment = "";
            // TODO: use FragmentSelector once in iiif-net
            if (filePointer.BeginTime > 0)
            {
                fragment = $"t={filePointer.BeginTime}";
                if (filePointer.EndTime > filePointer.BeginTime)
                {
                    fragment += $",{filePointer.EndTime}";
                }
            }
            if (filePointer.Region != null)
            {
                if (fragment.HasText())
                {
                    fragment += "&";
                }
                fragment +=
                    $"xywh={filePointer.Region.X1},{filePointer.Region.Y1},{filePointer.Region.X2 - filePointer.Region.X1},{filePointer.Region.Y2 - filePointer.Region.Y1}";
            }

            if (fragment.HasText())
            {
                canvasRef.Id += $"#{fragment}";
            }
            iiifRange.Items ??= [];
            iiifRange.Items.Add(canvasRef);
        }
        
        return iiifRange;
    }
}