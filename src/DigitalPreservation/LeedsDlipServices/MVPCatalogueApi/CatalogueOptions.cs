namespace LeedsDlipServices.MVPCatalogueApi;

public class CatalogueOptions
{
    public const string CatalogueOptionsName = "MvpCatalogueApi";
    
    public required Uri Root { get; set; }
    public required string QueryTemplate { get; set; }

    public int TimeoutMs { get; set; } = 5000;

    public required string ApiKeyHeader { get; set; } = "X-API-KEY";
    public required string ApiKeyValue { get; set; }
}