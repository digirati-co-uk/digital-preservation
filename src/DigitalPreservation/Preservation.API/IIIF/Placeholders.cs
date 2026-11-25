using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using NetVips;

namespace Preservation.API.IIIF;

/// <summary>
/// The images painted on canvases in the vanilla IIIF projection (issue #261). The platform holds
/// only originals, so every canvas is a placeholder: an image that says what the file is (its media
/// type and PRONOM key), and nothing about which file it is. That is what lets the endpoint serving
/// them be anonymous - a viewer can't send a token - and the images be cached.
/// </summary>
/// <remarks>
/// Modelled on the born-digital placeholders of Wellcome's iiif-builder
/// (BornDigitalExtensionsController), but set in Public Sans (SIL OFL 1.1, bundled under
/// Resources/placeholders), not Wellcome's typeface.
/// </remarks>
public static partial class Placeholders
{
    /// <summary>The size of a placeholder for a file with no recorded extents.</summary>
    public static readonly PlaceholderSize DefaultSize = new(1000, 800);

    public static readonly PlaceholderSize ThumbnailSize = new(100, 80);

    private const int LongSide = 1000;
    private const int Step = 50;
    private const double MaxRatio = 4.0;
    private const int ShortestSide = (int)(LongSide / MaxRatio);

    /// <summary>
    /// The placeholder for a canvas of this size: the same aspect ratio, so that a thumbnail made
    /// later can take its place without the canvas changing, but confined to a 1000 pixel long side,
    /// clamped to between 1:4 and 4:1, and rounded to multiples of 50 so there are few distinct
    /// images to render and cache. Without both dimensions, <see cref="DefaultSize"/>.
    /// </summary>
    public static PlaceholderSize SizeFor(int? width, int? height)
    {
        if (width is not > 0 || height is not > 0)
        {
            return DefaultSize;
        }
        var ratio = Math.Clamp((double)width.Value / height.Value, 1 / MaxRatio, MaxRatio);
        var shortSide = (int)Math.Round(LongSide * (ratio >= 1 ? 1 / ratio : ratio) / Step) * Step;
        shortSide = Math.Clamp(shortSide, ShortestSide, LongSide);
        return ratio >= 1 ? new PlaceholderSize(LongSide, shortSide) : new PlaceholderSize(shortSide, LongSide);
    }

    /// <summary>Whether a size could have come from <see cref="SizeFor"/>; anything else is refused.</summary>
    public static bool IsValidSize(PlaceholderSize size) =>
        IsValidSide(size.Width) && IsValidSide(size.Height)
        && (size.Width == LongSide || size.Height == LongSide)
        && size != new PlaceholderSize(0, 0);

    private static bool IsValidSide(int side) => side is >= ShortestSide and <= LongSide && side % Step == 0;

    /// <summary>The path, below the placeholder endpoint, of a canvas placeholder.</summary>
    public static string CanvasPath(PlaceholderSize size, string? pronomKey, string? mediaType)
    {
        var (prefix, number) = PronomParts(pronomKey);
        var (main, sub) = MediaTypeParts(mediaType);
        return $"canvas/{size.Width}x{size.Height}/{prefix}/{number}/{main}/{sub}";
    }

    /// <summary>The path, below the placeholder endpoint, of a thumbnail placeholder.</summary>
    public static string ThumbnailPath(string? mediaType) => $"thumb/{ThumbnailKind(mediaType)}";

    /// <summary>The four thumbnails there are, by the main part of the media type.</summary>
    public static string ThumbnailKind(string? mediaType)
    {
        var main = MediaTypeParts(mediaType).Main;
        return main is "image" or "video" or "audio" ? main : "file";
    }

    // "fmt/0" stands for "no PRONOM key": the image says so rather than printing a made-up key.
    private static (string Prefix, int Number) PronomParts(string? pronomKey)
    {
        var parts = pronomKey?.Split('/');
        if (parts is [var prefix, var n] && prefix is "fmt" or "x-fmt" && int.TryParse(n, out var number) && number > 0)
        {
            return (prefix, number);
        }
        return ("fmt", 0);
    }

    private static (string Main, string Sub) MediaTypeParts(string? mediaType)
    {
        // Parameters (";charset=...") are not part of what the image shows.
        var bare = mediaType?.Split(';')[0].Trim().ToLowerInvariant();
        var parts = bare?.Split('/');
        if (parts is [var main, var sub] && IsValidMediaTypePart(main) && IsValidMediaTypePart(sub))
        {
            return (main, sub);
        }
        return ("application", "octet-stream");
    }

    public static bool IsValidPronom(string prefix, int number) => prefix is "fmt" or "x-fmt" && number >= 0;

    public static bool IsValidMediaTypePart(string part) => MediaTypePart().IsMatch(part);

    // RFC 6838 restricted-name characters, which is all a registered type uses.
    [GeneratedRegex("^[a-z0-9][a-z0-9!#$&^_.+-]{0,126}$")]
    private static partial Regex MediaTypePart();

    // --- rendering ---

    private static readonly string FontFolder = Path.Combine(AppContext.BaseDirectory, "Resources", "placeholders");
    private static readonly string BoldFont = Path.Combine(FontFolder, "PublicSans-SemiBold.ttf");
    private static readonly string RegularFont = Path.Combine(FontFolder, "PublicSans-Regular.ttf");

    private static readonly double[] Background = [240, 241, 243];
    private static readonly double[] Ink = [52, 58, 64];
    private static readonly double[] Quiet = [108, 117, 125];

    // The inputs are validated and few, so the cache is small; the cap only stops a client asking
    // for every combination from growing it without limit.
    private const int CacheCap = 2048;
    private static readonly ConcurrentDictionary<string, byte[]> Cache = new();

    public static byte[] RenderCanvas(PlaceholderSize size, string prefix, int number, string main, string sub)
    {
        var key = $"canvas/{size.Width}x{size.Height}/{prefix}/{number}/{main}/{sub}";
        return Cached(key, () =>
        {
            var shortSide = Math.Min(size.Width, size.Height);
            var pronom = number > 0 ? $"{prefix}/{number}" : "Unknown format";
            return Render(size, [
                new Line($"{main}/{sub}", BoldFont, "Public Sans SemiBold", shortSide / 14, Ink),
                new Line(pronom, RegularFont, "Public Sans", shortSide / 22, Ink),
                new Line("Placeholder: the original file is preserved, not shown", RegularFont, "Public Sans", shortSide / 40, Quiet)
            ]);
        });
    }

    public static byte[] RenderThumbnail(string kind) =>
        Cached($"thumb/{kind}", () => Render(ThumbnailSize, [
            new Line(kind.ToUpperInvariant(), BoldFont, "Public Sans SemiBold", 14, Ink)
        ]));

    private static byte[] Cached(string key, Func<byte[]> render)
    {
        if (Cache.TryGetValue(key, out var png))
        {
            return png;
        }
        png = render();
        if (Cache.Count < CacheCap)
        {
            Cache.TryAdd(key, png);
        }
        return png;
    }

    private readonly record struct Line(string Text, string FontFile, string Family, int PointSize, double[] Colour);

    private static byte[] Render(PlaceholderSize size, IReadOnlyList<Line> lines)
    {
        using var blank = Image.Black(size.Width, size.Height, bands: 3);
        var image = blank.NewFromImage(Background);
        try
        {
            var maxWidth = (int)(size.Width * 0.85);
            var masks = lines.Select(l => RenderLine(l, maxWidth)).ToList();

            var gap = Math.Max(size.Height / 40, 2);
            var total = masks.Sum(m => m.Height) + gap * (masks.Count - 1);
            var top = Math.Max((size.Height - total) / 2, 0);
            for (var i = 0; i < masks.Count; i++)
            {
                using var mask = masks[i];
                var left = Math.Max((size.Width - mask.Width) / 2, 0);
                using var placed = mask.Embed(left, top, size.Width, size.Height);
                var next = placed.Ifthenelse(lines[i].Colour, image, blend: true);
                image.Dispose();
                image = next;
                top += mask.Height + gap;
            }
            using var output = image.Cast(Enums.BandFormat.Uchar);
            return output.PngsaveBuffer();
        }
        finally
        {
            image.Dispose();
        }
    }

    // Wraps at words and, failing that, inside one (a long vnd.* media type has no spaces), and
    // steps the size down while the text is still too wide or runs past three lines.
    private static Image RenderLine(Line line, int maxWidth)
    {
        var pointSize = Math.Max(line.PointSize, 6);
        while (true)
        {
            var mask = Image.Text(
                WebUtility.HtmlEncode(line.Text), // Pango markup; the inputs are validated, but escape anyway
                font: $"{line.Family} {pointSize}",
                fontfile: line.FontFile,
                width: maxWidth,
                align: Enums.Align.Centre,
                wrap: Enums.TextWrap.WordChar,
                dpi: 72);
            var tooBig = mask.Width > maxWidth || mask.Height > pointSize * 4;
            if (!tooBig || pointSize <= 6)
            {
                return mask;
            }
            mask.Dispose();
            pointSize = Math.Max(pointSize * 4 / 5, 6);
        }
    }
}

public readonly record struct PlaceholderSize(int Width, int Height);
