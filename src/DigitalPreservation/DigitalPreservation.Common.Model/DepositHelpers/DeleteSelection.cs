using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.DepositHelpers;

/// <summary>
/// This class is accepted by the API but can be used by the UI or other clients unknown
/// to manipulate the deposit and its METS outside of API interactions.
/// </summary>
public class DeleteSelection
{
    [JsonPropertyOrder(1)]
    [JsonPropertyName("deposit")]
    public Uri? Deposit { get; set; }
    
    [JsonPropertyOrder(2)]
    [JsonPropertyName("deleteFromMets")]
    public bool DeleteFromMets { get; set; }
    
    [JsonPropertyOrder(3)]
    [JsonPropertyName("deleteFromDepositFiles")]
    public bool DeleteFromDepositFiles { get; set; }
    
    [JsonPropertyOrder(4)]
    [JsonPropertyName("items")]
    public List<MinimalItem> Items { get; set; } = [];

    [JsonPropertyOrder(5)]
    [JsonPropertyName("deleteFromRoot")]
    public bool DeleteFromRoot { get; set; } = false;

    /// <summary>
    /// Paths whose deletion failures may be carried past rather than aborting the rest of the
    /// request - the caller expects these paths to sometimes not be there, or to sometimes fail to
    /// delete, and would rather the operation continue than stop. Does not cover the built-in
    /// protection guards (e.g. refusing to delete the objects directory), which always abort the
    /// request regardless of this list; see DeleteItemsHandler.
    /// </summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("continueIfFail")]
    public string[]? ContinueIfFail { get; set; } = [];

    /// <summary>
    /// True when relativePath's deletion failure should be tolerated: it is listed in
    /// ContinueIfFail exactly, or it sits under a listed path. Prefix matching is needed because
    /// callers list folder roots (e.g. "metadata/brunnhilde") while the items actually deleted are
    /// the files and subfolders inside them.
    /// </summary>
    public bool FailureIsTolerated(string relativePath)
    {
        if (ContinueIfFail is not { Length: > 0 })
        {
            return false;
        }

        return ContinueIfFail.Any(listed =>
            relativePath == listed || relativePath.StartsWith(listed + "/"));
    }
}