using System.Text.Json.Serialization;

namespace LeedsDlipServices.Identity;

public class QueryResult
{
    [JsonPropertyOrder(10)]
    [JsonPropertyName("query")]
    public PageData? PageData { get; set; }
    
    [JsonPropertyOrder(20)]
    [JsonPropertyName("results")]
    public List<IdentityRecord>? Results { get; set; }
}

public class PageData
{
    [JsonPropertyOrder(1)]
    [JsonPropertyName("query")]
    public string? Query { get; set; }
    
    [JsonPropertyOrder(2)]
    [JsonPropertyName("schemas")]
    public List<string>? Schemas { get; set; }
    
    [JsonPropertyOrder(5)]
    [JsonPropertyName("status")]
    public string? Status { get; set; }
    
    [JsonPropertyOrder(10)]
    [JsonPropertyName("created_from")]
    public string? CreatedFrom { get; set; }
    
    [JsonPropertyOrder(11)]
    [JsonPropertyName("created_to")]
    public string? CreatedTo { get; set; }
    
    [JsonPropertyOrder(20)]
    [JsonPropertyName("updated_from")]
    public string? UpdatedFrom { get; set; }
    
    [JsonPropertyOrder(22)]
    [JsonPropertyName("updated_to")]
    public string? UpdatedTo { get; set; }
    
    [JsonPropertyOrder(30)]
    [JsonPropertyName("first")]
    public int First { get; set; }
    
    [JsonPropertyOrder(40)]
    [JsonPropertyName("has_next")]
    public bool HasNext { get; set; }
    
    [JsonPropertyOrder(50)]
    [JsonPropertyName("has_prev")]
    public bool HasPrev { get; set; }
    
    [JsonPropertyOrder(60)]
    [JsonPropertyName("last")]
    public int Last { get; set; }
    
    [JsonPropertyOrder(70)]
    [JsonPropertyName("next_num")]
    public int? NextNum { get; set; }
    
    [JsonPropertyOrder(71)]
    [JsonPropertyName("prev_num")]
    public int? PrevNum { get; set; }
    
    [JsonPropertyOrder(80)]
    [JsonPropertyName("next_url")]
    public Uri? NextUrl { get; set; }
    
    [JsonPropertyOrder(81)]
    [JsonPropertyName("prev_url")]
    public Uri? PrevUrl { get; set; }
    
    [JsonPropertyOrder(90)]
    [JsonPropertyName("order")]
    public string? Order { get; set; }
    
    [JsonPropertyOrder(91)]
    [JsonPropertyName("orderby")]
    public string? OrderBy { get; set; }
    
    
    [JsonPropertyOrder(100)]
    [JsonPropertyName("page")]
    public int Page { get; set; }
    
    [JsonPropertyOrder(101)]
    [JsonPropertyName("pages")]
    public int Pages { get; set; }
    
    [JsonPropertyOrder(102)]
    [JsonPropertyName("per_page")]
    public int PerPage { get; set; }
    
    [JsonPropertyOrder(103)]
    [JsonPropertyName("total")]
    public int Total { get; set; }
}
