using MediatR;
using Microsoft.Extensions.Options;
using Preservation.Client;
using Storage.Repository.Common;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

/// <summary>
/// Call Presrvation API and verify communication. This is temporary only and will be removed once we have 'real'
/// requests implemented. It's a means to verify deployment only.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public class VerifyPreservationRunningNoAuth : IRequest<ConnectivityCheckResult>
{
}

public class VerifyPreservationRunningNoAuthHandler(
    IOptions<PreservationOptions> preservationOptions)
    : IRequestHandler<VerifyPreservationRunningNoAuth, ConnectivityCheckResult?>
{
    public async Task<ConnectivityCheckResult?> Handle(VerifyPreservationRunningNoAuth request,
        CancellationToken cancellationToken)
    {
        var preservation = preservationOptions.Value.Root + "health";
        var storage = preservation.Replace("preservation", "storage");
        if (storage == preservation)
        {
            storage = preservation.Replace("7228", "7000");
        }

        if (storage == preservation)
        {
            return new ConnectivityCheckResult
            {
                Name = ConnectivityCheckResult.ApiHealthChecks,
                Success = false,
                Error = "Can't deduce storage API endpoint"
            };
        }
        
        var client = new HttpClient();
        string? preservationError = null;
        string? storageError = null;
        try
        {
            var resp = await client.GetAsync(preservation, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                preservationError = resp.ReasonPhrase ?? preservation + " returned " + resp.StatusCode;
            }
        }
        catch (Exception e)
        {
            preservationError = preservation + " threw error: " + e.Message;
        }
        try
        {
            var resp = await client.GetAsync(storage, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                storageError = resp.ReasonPhrase ?? storage + " returned " + resp.StatusCode;
            }
        }
        catch (Exception e)
        {
            storageError = preservation + " threw error: " + e.Message;
        }

        string? error = null;
        if (preservationError is not null || storageError is not null)
        {
            error = preservationError + "; " + storageError;
        }
        
        return new ConnectivityCheckResult
        {
            Name = ConnectivityCheckResult.ApiHealthChecks,
            Success = error is null,
            Error = error
        };
    }
}