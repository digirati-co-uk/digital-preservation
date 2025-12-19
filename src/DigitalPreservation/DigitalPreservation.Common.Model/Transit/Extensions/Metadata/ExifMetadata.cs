using System.Text.Json.Serialization;
using DigitalPreservation.Common.Model.DepositHelpers;

namespace DigitalPreservation.Common.Model.Transit.Extensions.Metadata;

public class ExifMetadata : Metadata
{
    public List<ExifTag>? Tags { get; set; } = [];
}