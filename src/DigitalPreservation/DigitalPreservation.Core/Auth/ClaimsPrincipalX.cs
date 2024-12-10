using System.Security.Claims;

namespace DigitalPreservation.Core.Auth;

public static class ClaimsPrincipalX
{
    public static string GetCallerIdentity(this ClaimsPrincipal principal)
    {
        return "dlipdev";
        
    }
}