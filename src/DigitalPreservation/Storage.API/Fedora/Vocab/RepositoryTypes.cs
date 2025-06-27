namespace Storage.API.Fedora.Vocab;

public static class RepositoryTypes
{
    public static string FedoraNamespace = "http://fedora.info/definitions/v4/repository#";
    public static string W3cLdpNamespace = "http://www.w3.org/ns/ldp#";
    public static string DublinCoreElementsNamespace = "http://purl.org/dc/elements/1.1/";
    
    public static readonly string ArchivalGroup = FedoraNamespace + "ArchivalGroup";
    public static readonly string BasicContainer = W3cLdpNamespace + "BasicContainer";
    public static readonly string NonRDFSource = W3cLdpNamespace + "NonRDFSource";
    
    public static readonly string Tombstone = nameof(Tombstone);
    public static readonly string Binary = nameof(Binary);
}