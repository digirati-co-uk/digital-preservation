using System.Net.Http.Json;

namespace DigitalPreservation.Common.Model.ChangeDiscovery.Reader;

/// <summary>
/// https://iiif.io/api/discovery/1.0/#activity-streams-processing-algorithm
/// </summary>
/// <param name="httpClient"></param>
public class ActivityStreamReader(HttpClient httpClient)
{
    public async IAsyncEnumerable<Activity> ReadActivityStream(Uri orderedCollection, DateTime activitiesAfter)
    {
        var collection = await httpClient.GetFromJsonAsync<OrderedCollection>(orderedCollection);
        var pageUri = collection!.Last?.Id;
        while (pageUri != null)
        {
            var page = await httpClient.GetFromJsonAsync<OrderedCollectionPage>(pageUri);
            if (page?.OrderedItems == null || page.OrderedItems.Count == 0)
            {
                throw new Exception("Activity Stream Page has no OrderedItems");
            }
            foreach (var activity in page.OrderedItems.AsEnumerable().Reverse())
            {
                if (activity.EndTime > activitiesAfter)
                {
                    yield return activity;
                }
                else
                {
                    yield break;
                }
            }
            pageUri = page.Prev?.Id;
        }
    }
}