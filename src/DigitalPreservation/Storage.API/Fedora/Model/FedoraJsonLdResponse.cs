using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;

namespace Storage.API.Fedora.Model;

public class FedoraJsonLdResponse
{
    // Deserialized body data

    [JsonPropertyName("@id")]
    [JsonPropertyOrder(1)]
    public required Uri Id { get; set; }

    [JsonPropertyName("@type")]
    [JsonPropertyOrder(2)]
    public string?[]? Type { get; set; }

    [JsonPropertyName("created")]
    [JsonPropertyOrder(3)]
    public DateTime? Created { get; set; }

    [JsonPropertyName("createdBy")]
    [JsonPropertyOrder(4)]
    public string? CreatedBy { get; set; }

    [JsonPropertyName("lastModified")]
    [JsonPropertyOrder(5)]
    public DateTime? LastModified { get; set; }

    [JsonPropertyName("lastModifiedBy")]
    [JsonPropertyOrder(6)]
    public string? LastModifiedBy { get; set; }

    // Our custom(ish) additions

    /// <summary>
    /// We are using dc:title and that Fedora maps it to title,
    /// but we deserialise that manually - see below.
    /// </summary>
    [JsonPropertyName("titles")]
    [JsonPropertyOrder(101)]
    public List<string>? Titles { get; set; }

    [JsonIgnore]
    public string? Title
    {
        get
        {
            if (Titles == null || Titles.Count == 0) return null;
            return Titles[0];
        }
    }

    // HTTP-level data

    [JsonIgnore]
    public HttpStatusCode HttpStatusCode { get; set; }

    [JsonIgnore]
    public HttpResponseHeaders? HttpResponseHeaders { get; set; }

    [JsonIgnore]
    public string? Body { get; set; }
}