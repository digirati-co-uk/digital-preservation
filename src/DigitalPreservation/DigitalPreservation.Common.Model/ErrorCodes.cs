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
        string errorCode = statusCode switch
        {
            404 => ErrorCodes.NotFound,
            401 or 403 => ErrorCodes.Unauthorized,
            400 => ErrorCodes.BadRequest,
            409 => ErrorCodes.Conflict,
            422 => ErrorCodes.Unprocessable,
            410 => ErrorCodes.Tombstone,
            _ => ErrorCodes.UnknownError
        };
        return errorCode;
    }
}

