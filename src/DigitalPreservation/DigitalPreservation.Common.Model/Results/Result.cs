using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace DigitalPreservation.Common.Model.Results;

// Adapted from https://gist.github.com/vkhorikov/7852c7606f27c52bc288
// Added Codes as well as message strings.

public class Result
{
    public bool Success { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public bool Failure => !Success;

    protected Result(bool success, string? errorCode = null, string? errorMessage = null)
    {
        Success = success;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public string? CodeAndMessage()
    {
        var sb = new StringBuilder();
        if (ErrorCode != null)
        {
            sb.Append(ErrorCode);
            if (ErrorMessage != null)
            {
                sb.Append(": ");
            }
        }
        sb.Append(ErrorMessage ?? string.Empty);
        return sb.ToString();
    }

    public static Result Fail(string code, string? message = null)
    {
        return new Result(false, code, message);
    }

    public static Result<T?> Fail<T>(string code, string? message = null)
    {
        return new Result<T?>(default, false, code, message);
    }
    
    
    public static Result<T> FailNotNull<T>(string code, string? message = null)
    {
        return new Result<T>(default, false, code, message);
    }

    public static Result Ok()
    {
        return new Result(true);
    }

    public static Result<T?> Ok<T>(T? value)
    {
        return new Result<T?>(value, true);
    }
    
    public static Result<T> OkNotNull<T>(T value)
    {
        return new Result<T>(value, true);
    }

    public static Result Combine(params Result[] results)
    {
        foreach (Result result in results)
        {
            if (result.Failure)
                return result;
        }

        return Ok();
    }
}


public class Result<T> : Result
{
    public T? Value { get; [param: AllowNull] private set; }

    protected internal Result(T? value, bool success, string? errorCode = null, string? errorMessage = null)
        : base(success, errorCode, errorMessage)
    {
        Value = value;
    }
}