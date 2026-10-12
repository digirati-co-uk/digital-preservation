using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DigitalPreservation.Deposit.Archiver;

public class AuthProviderModel
{
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? ScopeUri { get; set; }

    /// <summary>
    /// Optional App ID URI of the target API (RFC-0001 Phase 2). Lives as a key in the same
    /// oauth_azure secret as the credentials; without this property, repointing the Archiver
    /// would need a code change - every other caller's repoint is config-only, and the landing
    /// sequence promises the same for this one.
    /// </summary>
    public string? ResourceUri { get; set; }
}
