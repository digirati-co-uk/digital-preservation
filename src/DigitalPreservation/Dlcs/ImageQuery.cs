using System.Text.Json.Serialization;

namespace Dlcs;

public class ImageQuery
{
    [JsonPropertyName("space")]
    [JsonPropertyOrder(1)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Space { get; set; }

    [JsonPropertyName("string1")]
    [JsonPropertyOrder(11)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? String1 { get; set; }

    [JsonPropertyName("string2")]
    [JsonPropertyOrder(12)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? String2 { get; set; }

    [JsonPropertyName("string3")]
    [JsonPropertyOrder(13)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? String3 { get; set; }

    [JsonPropertyName("number1")]
    [JsonPropertyOrder(21)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Number1 { get; set; }

    [JsonPropertyName("number2")]
    [JsonPropertyOrder(22)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Number2 { get; set; }

    [JsonPropertyName("number3")]
    [JsonPropertyOrder(23)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Number3 { get; set; }

    [JsonPropertyName("tags")]
    [JsonPropertyOrder(31)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Tags { get; set; }
}