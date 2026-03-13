using Microsoft.Extensions.Primitives;

namespace DigitalPreservation.Utils;

public static class CollectionUtils
{
    public static void RemoveEmptyKeys(this Dictionary<string, StringValues> queryDictionary)
    {
        var emptyKeys = queryDictionary.Keys.Where(
            k => queryDictionary[k] == StringValues.Empty || queryDictionary[k].ToString().IsNullOrWhiteSpace());
        foreach (var key in emptyKeys)
        {
            queryDictionary.Remove(key);
        }
    }

    public static IDictionary<string, string> GetValues(object obj) =>
        obj?.GetType().GetProperties()
            .ToDictionary(p => p.Name, p => p.GetValue(obj)?.ToString() ?? "")
        ?? []; // Returns empty dictionary if obj is null
}