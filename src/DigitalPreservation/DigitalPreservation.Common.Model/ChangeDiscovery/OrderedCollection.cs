using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.ChangeDiscovery;

public class OrderedCollection : ResourceWithContext
{
    [JsonPropertyOrder(1)]
    [JsonPropertyName("id")]
    public required Uri Id { get; set; }
    
    [JsonPropertyOrder(2)]
    [JsonPropertyName("type")]
    public string Type => nameof(OrderedCollection);
    
    [JsonPropertyOrder(5)]
    [JsonPropertyName("partOf")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OrderedCollection? PartOf { get; set; }
    
    [JsonPropertyOrder(10)]
    [JsonPropertyName("totalItems")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TotalItems { get; set; }
    
    [JsonPropertyOrder(30)]
    [JsonPropertyName("first")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OrderedCollectionPage? First { get; set; }
    
    [JsonPropertyOrder(50)]
    [JsonPropertyName("last")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OrderedCollectionPage? Last { get; set; }

    /// <summary>
    /// Builds the top-level collection both APIs' Activity Streams expose (issue #388 item 2.8):
    /// its own id, first/last page links, and totalItems, with @context set. An empty stream (
    /// <paramref name="totalItems"/> 0) still gets a real page 1 as both first and last - not page
    /// 0, which Storage API's own copy of this logic used to compute and which no page handler
    /// would ever answer.
    /// </summary>
    /// <param name="collectionId">This collection's own id.</param>
    /// <param name="pageUri">Builds a page's id from its 1-based page number.</param>
    /// <param name="totalItems">Total items across every page of the stream.</param>
    public static OrderedCollection For(Uri collectionId, Func<int, Uri> pageUri, int totalItems)
    {
        var totalPages = totalItems == 0
            ? 1
            : (totalItems + OrderedCollectionPage.DefaultPageSize - 1) / OrderedCollectionPage.DefaultPageSize;

        var collection = new OrderedCollection
        {
            Id = collectionId,
            First = new OrderedCollectionPage { Id = pageUri(1) },
            Last = new OrderedCollectionPage { Id = pageUri(totalPages) },
            TotalItems = totalItems
        };
        collection.WithContext();
        return collection;
    }
}