using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.ChangeDiscovery;

public class OrderedCollectionPage : ResourceWithContext
{
    [JsonPropertyOrder(1)]
    [JsonPropertyName("id")]
    public required Uri Id { get; set; }
    
    [JsonPropertyOrder(2)]
    [JsonPropertyName("type")]
    public string Type => nameof(OrderedCollectionPage);
    
    [JsonPropertyOrder(5)]
    [JsonPropertyName("startIndex")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? StartIndex { get; set; }
    
    [JsonPropertyOrder(10)]
    [JsonPropertyName("partOf")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OrderedCollection? PartOf { get; set; }
    
    [JsonPropertyOrder(20)]
    [JsonPropertyName("prev")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OrderedCollectionPage? Prev { get; set; }
    
    [JsonPropertyOrder(30)]
    [JsonPropertyName("next")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OrderedCollectionPage? Next { get; set; }
    
    [JsonPropertyOrder(50)]
    [JsonPropertyName("orderedItems")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<Activity>? OrderedItems { get; set; }

    // You don't have to use this size, obviously.
    public const int DefaultPageSize = 100;

    /// <summary>
    /// Builds one page of either API's Activity Stream (issue #388 item 2.8): its own id, the
    /// parent collection link, startIndex, the items, and prev/next links, with @context set.
    /// Callers must reject <paramref name="page"/> &lt; 1 themselves before calling this - the two
    /// callers answer different HTTP status codes for that, which is a Result/controller concern,
    /// not something this plain model factory should decide.
    /// </summary>
    /// <param name="collectionId">The id of the collection this page belongs to.</param>
    /// <param name="pageUri">Builds a page's id from its 1-based page number.</param>
    /// <param name="page">This page's own 1-based page number.</param>
    /// <param name="totalItems">Total items across every page of the stream.</param>
    /// <param name="items">This page's items, in order.</param>
    public static OrderedCollectionPage For(
        Uri collectionId, Func<int, Uri> pageUri, int page, int totalItems, List<Activity>? items)
    {
        var startIndex = (page - 1) * DefaultPageSize;
        var result = new OrderedCollectionPage
        {
            Id = pageUri(page),
            PartOf = new OrderedCollection { Id = collectionId },
            StartIndex = startIndex,
            OrderedItems = items
        };
        if (page > 1)
        {
            result.Prev = new OrderedCollectionPage { Id = pageUri(page - 1) };
        }
        if (totalItems > startIndex + DefaultPageSize)
        {
            result.Next = new OrderedCollectionPage { Id = pageUri(page + 1) };
        }
        result.WithContext();
        return result;
    }
}