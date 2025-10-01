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
    
        
    public static string GetErrorCode(int? statusCode)
    {
        var errorCode = statusCode switch
        {
            404 => NotFound,
            401 or 403 => Unauthorized,
            400 => BadRequest,
            409 => Conflict,
            422 => Unprocessable,
            410 => Tombstone,
            _ => UnknownError
        };
        return errorCode;
    }
}

