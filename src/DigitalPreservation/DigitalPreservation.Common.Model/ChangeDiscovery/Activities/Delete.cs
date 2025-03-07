using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.ChangeDiscovery.Activities;

public class Delete : Activity
{
    [JsonPropertyOrder(2)]
    [JsonPropertyName("type")]
    public override string Type => nameof(Delete);
}