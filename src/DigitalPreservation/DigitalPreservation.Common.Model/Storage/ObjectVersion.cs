using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Storage;

public class ObjectVersion
{
    [JsonPropertyName("mementoTimestamp")]
    [JsonPropertyOrder(1)]
    public required string MementoTimestamp { get; set; }

    [JsonPropertyName("mementoDateTime")]
    [JsonPropertyOrder(2)]
    public required DateTime MementoDateTime { get; set; }

    [JsonPropertyName("ocflVersion")]
    [JsonPropertyOrder(3)]
    public string? OcflVersion { get; set; }

    public override string ToString()
    {
        if(OcflVersion == null)
        {
            return MementoTimestamp;
        }
        return $"{OcflVersion} | {MementoTimestamp}";
    }

    public override bool Equals(object? obj)
    {
        var other = obj as ObjectVersion;
        if (other == null)
        {
            return false;
        }
        if(!(string.IsNullOrWhiteSpace(other.OcflVersion) || string.IsNullOrWhiteSpace(OcflVersion)))
        {
            return other.OcflVersion == OcflVersion;
        }
        if (!(string.IsNullOrWhiteSpace(other.MementoTimestamp) || string.IsNullOrWhiteSpace(MementoTimestamp)))
        {
            return other.MementoTimestamp == MementoTimestamp;
        }
        return false;
    }

    public override int GetHashCode()
    {
        if(OcflVersion == null)
        {
            throw new InvalidOperationException("Can't call GetHashCode until OcflVersion is set");
        }
        return OcflVersion.GetHashCode();
    }
}
