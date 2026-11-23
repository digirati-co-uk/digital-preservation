namespace DigitalPreservation.Common.Model;

public static class ErrorCodes
{
    public const string NotFound = nameof(NotFound);
    public const string Unauthorized = nameof(Unauthorized);
    public const string Conflict = nameof(Conflict);
    public const string PreconditionFailed = nameof(PreconditionFailed);
    public const string BadRequest = nameof(BadRequest);
    public const string Unprocessable = nameof(Unprocessable);
    public const string UnknownError = nameof(UnknownError);
    public const string Tombstone = nameof(Tombstone);

    /// <summary>
    /// A 403 from something WE called - S3, Fedora, or a peer platform API - refusing OUR OWN
    /// credentials, not the end caller's (review feedback on #394/issue #388 item 2.1). Every
    /// current caller of <see cref="GetErrorCode"/> is translating the result of an outbound call
    /// into a <c>Result</c> for this service's own response; none of them decide whether to accept
    /// the inbound caller in the first place - that happens earlier, in the auth middleware, and
    /// never reaches this method. Collapsing a downstream 403 into the same <see cref="Unauthorized"/>
    /// an inbound caller gets for its own bad token told the caller its own credentials were the
    /// problem, when the actual fault is ours: a misconfigured IAM role, service account, or
    /// machine-identity header somewhere in our own infrastructure.
    /// </summary>
    public const string UpstreamUnauthorized = nameof(UpstreamUnauthorized);

    /// <summary>
    /// Something WE depend on could not be reached, so the operation was refused rather than done
    /// half-right - the UI's METS write when the Preservation API's access condition profile cannot
    /// be fetched (issue #433). A 503 to the caller: retry later, nothing was changed.
    /// </summary>
    public const string UpstreamUnavailable = nameof(UpstreamUnavailable);

    public static string GetErrorCode(int? statusCode)
    {
        var errorCode = statusCode switch
        {
            404 => NotFound,
            401 => Unauthorized,
            403 => UpstreamUnauthorized,
            400 => BadRequest,
            409 => Conflict,
            422 => Unprocessable,
            410 => Tombstone,
            412 => PreconditionFailed,
            _ => UnknownError
        };
        return errorCode;
    }
}

