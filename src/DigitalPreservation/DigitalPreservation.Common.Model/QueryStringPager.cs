using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model;

// See https://www.hydra-cg.com/spec/latest/core/#templated-links
// Slightly adapted

public class QueryStringPager(Uri baseUri, int totalItems)
{
    [JsonPropertyOrder(10)]
    [JsonPropertyName("baseUri")]
    public Uri BaseUri { get; } = baseUri;
    
    [JsonPropertyOrder(20)]
    [JsonPropertyName("totalItems")]
    public int TotalItems { get; } = totalItems;
    
    [JsonPropertyOrder(30)]
    [JsonPropertyName("queryTemplate")]
    public QueryTemplate QueryTemplate { get; } = new()
    {
        Template = "?page={page}&pageSize={pageSize}",
        Mapping =
        [
            new VariableMapping("page", "Page", false, "1"),
            new VariableMapping("pageSize", "Page size", false, "500"),
        ]
    };
}

public class QueryTemplate
{
    [JsonPropertyOrder(10)]
    [JsonPropertyName("template")]
    public required string Template;
    
    [JsonPropertyOrder(20)]
    [JsonPropertyName("mapping")]
    public List<VariableMapping> Mapping = [];
}

public class VariableMapping(string variable, string property, bool required, string defaultValue)
{
    [JsonPropertyOrder(10)]
    [JsonPropertyName("variable")]
    public string Variable { get; set; } = variable;
    
    [JsonPropertyOrder(20)]
    [JsonPropertyName("property")]
    public string Property { get; set; } = property;
    
    [JsonPropertyOrder(30)]
    [JsonPropertyName("required")]
    public bool Required { get; set; } = required;
    
    [JsonPropertyOrder(40)]
    [JsonPropertyName("defaultValue")]
    public string? DefaultValue { get; set; } = defaultValue;
}