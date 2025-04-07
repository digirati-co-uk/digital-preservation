using System.Text.Json.Serialization;
using DigitalPreservation.Utils;

namespace LeedsDlipServices.MVPCatalogueApi;

public class CatalogueRecord
{
    [JsonPropertyOrder(10)]
    [JsonPropertyName("Title")]
    public required string Title { get; set; }
    
    [JsonPropertyOrder(20)]
    [JsonPropertyName("Shelfmark")]
    public string? Shelfmark { get; set; }
    
    [JsonPropertyOrder(25)]
    [JsonPropertyName("Object Number")]
    public string? ObjectNumber { get; set; }
    
    [JsonPropertyOrder(30)]
    [JsonPropertyName("Date")]
    public string? DateField { get; set; }

    public DateTime? GetDate()
    {
        if (!DateField.HasText() || DateField == "no date") return null;
        
        if (DateTime.TryParse(DateField, out var date))
        {
            return date;
        }

        return null;
    }
    
    [JsonPropertyOrder(35)]
    [JsonPropertyName("Description")]
    public string? Description { get; set; }
    
    [JsonPropertyOrder(40)]
    [JsonPropertyName("Dimensions")]
    public string? Dimensions { get; set; }
    
    [JsonPropertyOrder(45)]
    [JsonPropertyName("Notes")]
    public string? Notes { get; set; }
    
    [JsonPropertyOrder(50)]
    [JsonPropertyName("Collections")]
    public string?[]? Collections { get; set; }
    
    [JsonPropertyOrder(55)]
    [JsonPropertyName("Credit Line")]
    public string? CreditLine { get; set; }
    
    [JsonPropertyOrder(60)]
    [JsonPropertyName("Attribution")]
    public string? Attribution { get; set; }
    
    [JsonPropertyOrder(70)]
    [JsonPropertyName("Medium")]
    public string? Medium { get; set; }
    
    [JsonPropertyOrder(75)]
    [JsonPropertyName("Technique")]
    public string? Technique { get; set; }
    
    [JsonPropertyOrder(80)]
    [JsonPropertyName("Support")]
    public string? Support { get; set; }
    
    [JsonPropertyOrder(90)]
    [JsonPropertyName("Creators")]
    public string?[]? Creators { get; set; }
    
    [JsonPropertyOrder(100)]
    [JsonPropertyName("Rights")]
    public string?[]? Rights { get; set; }
    
    [JsonPropertyOrder(110)]
    [JsonPropertyName("Homepage")]
    public Uri? Homepage { get; set; }
    
}