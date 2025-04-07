using System.Text.Json.Serialization;

namespace LeedsDlipServices.Identity;

public class SchemaAndValue
{
    [JsonIgnore]
    public const string SchemaCatIrn = "catirn";
    
    [JsonIgnore]
    public const string SchemaId = "id";

    [JsonIgnore] 
    public const string SchemaArchivalGroupUri = "repositoryuri";
    
    [JsonPropertyOrder(1)]
    [JsonPropertyName("type")]
    public string? Type => "SchemaAndValue";
    
    [JsonPropertyOrder(100)]
    [JsonPropertyName("schema")]
    public required string Schema { get; set; }
    
    [JsonPropertyOrder(200)]
    [JsonPropertyName("value")]
    public required string Value { get; set; }
}