using System.Text.Json.Serialization;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Combined;

namespace DigitalPreservation.Common.Model.DepositHelpers;

public class MinimalItem
{
    [JsonPropertyName("path")]
    [JsonPropertyOrder(1)]
    public required string RelativePath { get; set; }
    
    [JsonPropertyName("isDir")]
    [JsonPropertyOrder(2)]
    public bool IsDirectory { get; set; }
    
    [JsonPropertyName("where")]
    [JsonPropertyOrder(3)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Whereabouts Whereabouts { get; set; }

    /// <summary>
    /// Find the minimalItem in a CombinedDirectory content root that may or may not be in BagIt layout
    /// </summary>
    /// <param name="contentRoot"></param>
    /// <returns></returns>
    public WorkingBase? ResolveFromContentRoot(CombinedDirectory contentRoot)
    {
        WorkingBase? wb = IsDirectory
            ? contentRoot.FindDirectory(RelativePath)?.DirectoryInDeposit?.ToRootLayout()
            : contentRoot.FindFile(RelativePath)?.FileInDeposit?.ToRootLayout();
        return wb;
    }
}