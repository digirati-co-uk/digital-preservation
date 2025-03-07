

namespace DigitalPreservation.CommonApiClient;

public interface ITokenScope
{
    string? ScopeUri { get; set; }
}

public class TokenScope(string? scopeUri) : ITokenScope
{
    public string? ScopeUri { get; set; } = scopeUri;
}
