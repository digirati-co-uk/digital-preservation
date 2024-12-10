using System.Security.Claims;
using DigitalPreservation.Core.Auth;
using Storage.API.Fedora.Http;

namespace Storage.API.Handlers;

internal class ManagedTriplesHandler(IHttpContextAccessor contextAccessor, ILogger<ManagedTriplesHandler> logger)
    : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!(request.Method == HttpMethod.Post || request.Method == HttpMethod.Put || request.Method == HttpMethod.Patch))
        {
            // Allow the request to proceed undecorated
            return base.SendAsync(request, cancellationToken);
        }
        var user = contextAccessor.HttpContext?.User;
        if(user?.Identity?.IsAuthenticated != true)
        {
            // Allow the request to proceed undecorated
            return base.SendAsync(request, cancellationToken);
        }

        var callerIdentity = user.GetCallerIdentity();
        
        // How do we know here which of these it is? 
        request.WithCreatedBy(callerIdentity);
        //request.WithLastModifiedBy(callerIdentity);
        
        return base.SendAsync(request, cancellationToken);
    }
}