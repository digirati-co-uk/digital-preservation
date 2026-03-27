using Amazon.Lambda.Core;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace DotnetFunction;

public class Function
{
    /// <summary>
    /// A simple function that takes a string and returns both the upper and lower case version of the string.
    /// </summary>
    /// <param name="input"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public Dictionary<string, dynamic> FunctionHandler(string input, ILambdaContext context)
    {
        string name = input;
        string message = string.Format("Hello, {0}!", name);
        //
        return new Dictionary<string, dynamic>
        {
            { "statusCode", 200 },
            { "body", message }
        };
    }

    ///// <summary>
    ///// A simple function that takes a string and returns both the upper and lower case version of the string.
    ///// </summary>
    ///// <param name="input">The event for the Lambda function handler to process.</param>
    ///// <param name="context">The ILambdaContext that provides methods for logging and describing the Lambda environment.</param>
    ///// <returns></returns>
    //public Casing FunctionHandler(string input, ILambdaContext context)
    //{
    //    return new Casing(input.ToLower(), input.ToUpper());
    //}
}

public record Casing(string Lower, string Upper);