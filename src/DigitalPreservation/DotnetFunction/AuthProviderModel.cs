namespace DotnetFunction;

public class AuthProviderModel
{
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? ScopeUri { get; set; }
}
