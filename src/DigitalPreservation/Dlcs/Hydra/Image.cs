using System.Text.Json.Serialization;

namespace Dlcs.Hydra;

public class Image : JSONLDBase
{
    [JsonIgnore]
    public string? StorageIdentifier { get; set; }

    [JsonPropertyName("id")]
    [JsonPropertyOrder(10)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ModelId { get; set; }

    [JsonPropertyName("space")]
    [JsonPropertyOrder(10)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Space { get; set; }

    [JsonPropertyName("imageService")]
    [JsonPropertyOrder(11)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ImageService { get; set; }

    [JsonPropertyName("thumbnailImageService")]
    [JsonPropertyOrder(13)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThumbnailImageService { get; set; }

    [JsonPropertyName("thumbnail400")]
    [JsonPropertyOrder(13)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Thumbnail400 { get; set; }

    [JsonPropertyName("created")]
    [JsonPropertyOrder(14)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? Created { get; set; }

    [JsonPropertyName("origin")]
    [JsonPropertyOrder(15)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Origin { get; set; }

    [JsonPropertyName("initialOrigin")]
    [JsonPropertyOrder(16)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InitialOrigin { get; set; }

    [JsonPropertyName("maxUnauthorised")]
    [JsonPropertyOrder(17)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxUnauthorised { get; set; }

    [JsonPropertyName("queued")]
    [JsonPropertyOrder(30)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? Queued { get; set; }

    [JsonPropertyName("dequeued")]
    [JsonPropertyOrder(31)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? Dequeued { get; set; }

    [JsonPropertyName("finished")]
    [JsonPropertyOrder(32)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? Finished { get; set; }

    [JsonPropertyName("ingesting")]
    [JsonPropertyOrder(33)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Ingesting { get; set; }

    [JsonPropertyName("error")]
    [JsonPropertyOrder(34)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }

    // metadata

    [JsonPropertyName("tags")]
    [JsonPropertyOrder(40)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Tags { get; set; }

    [JsonPropertyName("string1")]
    [JsonPropertyOrder(41)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? String1 { get; set; }

    [JsonPropertyName("string2")]
    [JsonPropertyOrder(42)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? String2 { get; set; }

    [JsonPropertyName("string3")]
    [JsonPropertyOrder(43)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? String3 { get; set; }

    [JsonPropertyName("number1")]
    [JsonPropertyOrder(51)]
    public long Number1 { get; set; }

    [JsonPropertyName("number2")]
    [JsonPropertyOrder(52)]
    public long Number2 { get; set; }

    [JsonPropertyName("number3")]
    [JsonPropertyOrder(53)]
    public long Number3 { get; set; }

    // Additional properties for time-based media, files etc
    // Metadata?


    [JsonPropertyName("width")]
    [JsonPropertyOrder(21)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    [JsonPropertyOrder(22)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Height { get; set; }

    [JsonPropertyName("duration")]
    [JsonPropertyOrder(23)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Duration { get; set; }

    [JsonPropertyName("metadata")]
    [JsonPropertyOrder(110)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Metadata { get; set; } // herein duration, other stuff learnt during transcoding

    [JsonPropertyName("mediaType")]
    [JsonPropertyOrder(120)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MediaType { get; set; }

    [JsonPropertyName("family")]
    [JsonPropertyOrder(130)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public char? Family { get; set; } // i, t, f

    [JsonPropertyName("text")]
    [JsonPropertyOrder(140)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("textType")]
    [JsonPropertyOrder(150)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TextType { get; set; } // e.g., METS-ALTO, hOCR, TEI, text/plain etc



    // Hydra Link properties

    [JsonPropertyName("roles")]
    [JsonPropertyOrder(70)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Roles { get; set; }

    [JsonPropertyName("batch")]
    [JsonPropertyOrder(71)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Batch { get; set; }

    [JsonPropertyName("imageOptimisationPolicy")]
    [JsonPropertyOrder(80)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ImageOptimisationPolicy { get; set; }

    [JsonPropertyName("thumbnailPolicy")]
    [JsonPropertyOrder(81)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThumbnailPolicy { get; set; }

    [JsonPropertyName("wcDeliveryChannels")]
    [JsonPropertyOrder(82)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? DeliveryChannels { get; set; }
}