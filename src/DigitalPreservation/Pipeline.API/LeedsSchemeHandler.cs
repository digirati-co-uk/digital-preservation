using System.Net;
using Microsoft.AspNetCore.Authentication;
namespace Pipeline.API;


/// <summary>
/// This class provides a default forbid/challenge scheme.
/// </summary>
public class LeedsSchemeHandler : IAuthenticationHandler
{
    private HttpContext _context;

    public Task InitializeAsync(AuthenticationScheme scheme, HttpContext context)
    {
        _context = context;
        return Task.CompletedTask;
    }

    public Task<AuthenticateResult> AuthenticateAsync()
        => Task.FromResult(AuthenticateResult.NoResult());

    public Task ChallengeAsync(AuthenticationProperties properties)
    {
        _context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
        return Task.CompletedTask;
    }

    public Task ForbidAsync(AuthenticationProperties properties)
    {
        _context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
        return Task.CompletedTask;
    }
}
