using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DigitalPreservation.Common.Model.Transit.Extensions;

public partial class RecordInfo
{
    [JsonPropertyName("recordIdentifiers")]
    [JsonPropertyOrder(1)]
    public List<RecordIdentifier> RecordIdentifiers { get; set; } = [];

    public const string CompactDelimiter = "-|-";
    public string? ToCompactString()
    {
        if (RecordIdentifiers.Count > 0)
        {
            var sb = new StringBuilder();

            for (var index = 0; index < RecordIdentifiers.Count; index++)
            {
                if (index > 0)
                {
                    sb.Append(CompactDelimiter);
                }
                var identifier = RecordIdentifiers[index];
                sb.Append(identifier.Value);
                sb.Append('(');
                sb.Append(identifier.Source);
                sb.Append(')');
            }

            return sb.ToString();
        }

        return null;
    }

    /// <summary>
    /// "value(source)", "value(with parens)(source)", "AA/BB/CC (ac) Z(EMu)"
    /// The latter yields value="AA/BB/CC (ac) Z", source="Emu"
    /// </summary>
    /// <returns></returns>
    [GeneratedRegex(@"^(.*)\(([^)]*)\)$")]
    private static partial Regex RecordIdentifierStringForm();
    
    public static RecordInfo? FromCompactString(string compactString)
    {
        if (string.IsNullOrWhiteSpace(compactString))
        {
            return null;
        }
    
        var recordInfo = new RecordInfo();
        var parts = compactString.Split(CompactDelimiter);
        foreach (var part in parts)
        {
            var match = RecordIdentifierStringForm().Match(part);
            if (match.Success)
            {
                recordInfo.RecordIdentifiers.Add(new RecordIdentifier
                {
                    Value =  match.Groups[2].Value,
                    Source = match.Groups[1].Value
                });
            }
        }
        
        return recordInfo;
    }
}

public class RecordIdentifier
{
    [JsonPropertyName("source")]
    [JsonPropertyOrder(1)]
    public required string Source  { get; set; }
    
    [JsonPropertyName("value")]
    [JsonPropertyOrder(2)]
    public required string Value { get; set; }
}