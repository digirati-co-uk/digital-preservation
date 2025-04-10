using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using DigitalPreservation.Utils;

namespace Storage.API.Fedora.Http;

public static partial class RdfBodyBuilder
{
    private static readonly MediaTypeHeaderValue Turtle = MediaTypeHeaderValue.Parse("text/turtle");

    public static string EscapeForLiteralRdf(this string s, bool stripLineBreaks)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            switch (c)
            {
                case '\"':
                    sb.Append("\\\"");
                    break;
                case '\r' when stripLineBreaks:
                    sb.Append(" - ");
                    break;
                case '\r' when !stripLineBreaks:
                    sb.Append("\\r");
                    break;
                case '\n' when stripLineBreaks:
                    sb.Append(" - ");
                    break;
                case '\n' when !stripLineBreaks:
                    sb.Append("\\n");
                    break;
                case '\t':
                    sb.Append( "\\t" );
                    break;
                default:
                {
                    if( c < 32 || c >= 127 )
                        sb.Append($"\\u{(int)c:x4}");
                    else
                        sb.Append(c);
                    break;
                }
            }
        }
        return sb.ToString();
    }
    
    public static void AppendRdf(this HttpRequestMessage requestMessage, string prefix, string uri, string statement)
    {
        var httpContent = requestMessage.Content;
        string content;
        if (httpContent == null)
        {
            content = FormatPrefix(prefix, uri) + Environment.NewLine + statement + " .";
        }
        else
        {
            if (httpContent is not StringContent sc)
            {
                throw new InvalidOperationException("Existing HttpContent must be StringContent");
            }
            
            string existingContent = sc.ReadAsStringAsync().Result;
            if (existingContent.IsNullOrWhiteSpace())
            {
                throw new InvalidOperationException("Existing HttpContent must have length");
            }
            var sb = new StringBuilder();
            bool alreadyHasPrefix = false;
            foreach (Match m in PrefixRegex().Matches(existingContent))
            {
                sb.AppendLine(m.Value);
                if (m.Groups[1].Value == prefix)
                {
                    alreadyHasPrefix = true;
                }
            }
            if (!alreadyHasPrefix)
            {
                sb.AppendLine(FormatPrefix(prefix, uri));
            }
            foreach (var existingStatement in existingContent.Split('\r', '\n')
                         .Where(line => line.HasText() && !line.StartsWith("PREFIX")))
            {
                sb.AppendLine(existingStatement);
            }
            sb.AppendLine(statement + " .");
            content = sb.ToString().Trim();
        }
        requestMessage.Content = new StringContent(content, Turtle);
        
    }

    private static string FormatPrefix(string prefix, string uri)
    {
        return $"PREFIX {prefix}: <{uri}>";
    }

    [GeneratedRegex("PREFIX ([a-z]*): <(http.*)>", RegexOptions.Multiline)]
    private static partial Regex PrefixRegex();
}