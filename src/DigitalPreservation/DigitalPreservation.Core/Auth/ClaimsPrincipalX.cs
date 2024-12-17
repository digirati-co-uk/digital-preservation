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
            throw new UnauthorizedAccessException("ClaimsPrincipal is null");
        }
        
        // Our fake implementation, for now
        return "dev_" + DateTime.Now.ToString("dddd").ToLowerInvariant();
    }
}