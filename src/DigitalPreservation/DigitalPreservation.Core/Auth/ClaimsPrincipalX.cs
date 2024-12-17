using System.Security.Claims;

namespace DigitalPreservation.Core.Auth;

public static class ClaimsPrincipalX
{
    public static string GetCallerIdentity(this ClaimsPrincipal? principal, bool allowAnonymous = false)
    {
        if (principal == null)
        {
            if (allowAnonymous)
            {
                return string.Empty;
            }
            // Comment this out for testing
            // throw new UnauthorizedAccessException("ClaimsPrincipal is null");
        }
        
        // Our fake implementation, for now
        return "dev4_" + DateTime.Now.ToString("dddd").ToLowerInvariant();
    }
}