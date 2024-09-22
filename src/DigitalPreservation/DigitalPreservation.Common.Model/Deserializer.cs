using System.Text.Json;

namespace DigitalPreservation.Common.Model;

public class Deserializer
{
    public static PreservedResource? Parse(Stream streamJson)
    {
        using var jDoc = JsonDocument.Parse(streamJson);
        return Deserialize(jDoc.RootElement);
    }
    
    
    public static PreservedResource? Parse(string stringJson)
    {
        using var jDoc = JsonDocument.Parse(stringJson);
        return Deserialize(jDoc.RootElement);
    }

    private static PreservedResource? Deserialize(JsonElement rootElement)
    {
        if (!rootElement.TryGetProperty("type", out var typeValue))
        {
            throw new InvalidDataException("The JSON element does not contain a 'type' property.");
        }
        switch (typeValue.ToString())
        {
            case "Container":
            case "RepositoryRoot":
                return rootElement.Deserialize<Container>();
            case "Binary":
                return rootElement.Deserialize<Binary>();
            case "ArchivalGroup":
                // return rootElement.Deserialize<ArchivalGroup>();
                return null;
            default:
                return null;
        }
    }
}