using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Runtime.InteropServices;
using Microsoft.Identity.Web;

namespace DigitalPreservation.Core.Auth;

public static class ClaimsPrincipalX
{
    private const string KeyValue = "upn"; // unique-name also an option
    
    
    public static string GetCallerIdentity(this ClaimsPrincipal? principal, bool allowAnonymous = false) =>
        principal?.GetDisplayName() ?? "dlipdev";


    public static string GetCallerIdentity(HttpContext? context) =>
        context == null ? "unknown" : GetCallerIdentity(context.Request);


    public static string GetCallerIdentity(HttpRequest? request)
    {
        if (request == null)
         return "unknown";
        
        var token = request.Headers["Authorization"].ToString();
        return GetCallerIdentity(token);
    }

    public static string GetCallerIdentity(string? token)
    {
        if (string.IsNullOrEmpty(token))
            return "unknown";

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token.Replace("Bearer ", ""));
        jwtToken.Payload.TryGetValue(KeyValue, out var upn);
        return upn?.ToString()?.Split("@")[0] ?? "unknown";
    }
}