using System.Text.Json;

namespace Storage.API.Fedora.Http;

public static class JsonLdX
{
    public static bool HasType(this JsonElement element, string type)
    {
        if (element.TryGetProperty("@type", out JsonElement typeList))
        {
            if (typeList.EnumerateArray().Any(t => t.GetString() == type))
            {
                return true;
            }
        }
        return false;
    }
}