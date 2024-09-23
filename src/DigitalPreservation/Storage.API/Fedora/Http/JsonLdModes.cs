namespace Storage.API.Fedora.Http;

public static class JsonLdModes
{
    /// <summary>
    /// The default Fedora JSON-LD representation
    /// </summary>
    public const string Expanded = "\"http://www.w3.org/ns/json-ld#expanded\"";


    /// <summary>
    /// Compacted JSON-LD (not the default)
    /// </summary>
    public const string Compacted = "\"http://www.w3.org/ns/json-ld#compacted\"";


    /// <summary>
    /// Flattened JSON-LD (not the default)
    /// </summary>
    public const string Flattened = "\"http://www.w3.org/ns/json-ld#flattened\"";
}