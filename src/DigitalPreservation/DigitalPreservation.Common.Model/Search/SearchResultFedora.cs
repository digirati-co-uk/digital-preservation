
namespace DigitalPreservation.Common.Model.Search;
public class SearchResultFedora
{
    public required string FedoraId { get; set;  }
    public DateTime Created { get; set; }
    public DateTime LastModified { get; set; }
    public long ContentSize { get; set; }
    public required string MimeType { get; set; }
   

}
