namespace DigitalPreservation.Common.Model.ChangeDiscovery.Reader;

public class ActivityStreamReader(HttpClient httpClient)
{
    public async Task<IEnumerable<Activity>> ReadActivityStream(Uri orderedCollection, DateTime activitiesAfter)
    {
        // fetch the ordered collection
        // follow the algorithm

    }
}