namespace Preservation.Client;

public class PreservationOptions
{
    public const string Preservation = "Preservation";
    
    /// <summary>
    /// Root URI for PreservationAPI
    /// </summary>
    public required Uri Root { get; set; }

    /// <summary>
    /// Timeout, in MINUTES, for requests made to preservation client 
    public double TimeoutMinutes { get; set; } = 1440;
    
    public string? ManifestHost { get; set; }

    public string[] RecordInfoSources { get; set; } = [];

    /// <summary>
    /// Top-level container slugs under which an Archival Group's path can be turned into a IIIF
    /// manifest URI (issue #276 item 2). Where platform IIIF support should go generally is #261's
    /// question; this only makes the existing hard-coded "cc"/"cc-test" check configurable rather
    /// than redesigning it.
    /// </summary>
    public string[] IiifPredictableParents { get; set; } = ["cc", "cc-test"];
}