namespace LeedsDlipServices.Identity;

public class IdentityOptions
{
    public const string IdentityOptionsName = "IdentityService";
    
    public required Uri Root { get; set; }

    public int TimeoutMs { get; set; } = 5000;
    
    public required string ApiKeyHeader { get; set; }
    public required string ApiKeyValue { get; set; }
    
    public required Uri PreservationRoot { get; set; }
    public Uri? IIIFCSInternalRoot { get; set; }
    public int? IIIFCSCustomer { get; set; }
    public string? AlternativeCollectionsContainer { get; set; }
}