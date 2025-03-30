using System.Text.Json.Serialization;

namespace LeedsDlipServices.Identity;

public class IdentityRecord
{
    [JsonPropertyOrder(10)]
    [JsonPropertyName("id")]
    public required string Id { get; set; }
    
    [JsonPropertyOrder(11)]
    [JsonPropertyName("epid")]
    public string? EPid { get; set; }
    
    [JsonPropertyOrder(20)]
    [JsonPropertyName("created")]
    public DateTime? Created { get; set; }
    
    [JsonPropertyOrder(25)]
    [JsonPropertyName("updated")]
    public DateTime? Updated { get; set; }

    [JsonPropertyOrder(30)]
    [JsonPropertyName("catirn")]
    public required string CatIrn { get; set; }

    [JsonPropertyOrder(40)]
    [JsonPropertyName("desc")]
    public string? Desc { get; set; }

    [JsonPropertyOrder(50)]
    [JsonPropertyName("status")]
    public string? Status { get; set; }
    
    [JsonPropertyOrder(60)]
    [JsonPropertyName("title")]
    public string? Title { get; set; }
    
    [JsonPropertyOrder(110)]
    [JsonPropertyName("catalogueapiuri")]    
    public Uri? CatalogueApiUri { get; set; }
    
    [JsonPropertyOrder(120)]
    [JsonPropertyName("manifesturi")]    
    public Uri? ManifestUri { get; set; }
    
    [JsonPropertyOrder(130)]
    [JsonPropertyName("repositoryuri")]    
    public Uri? RepositoryUri { get; set; }
}