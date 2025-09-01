
namespace DigitalPreservation.Common.Model.Search;
public class SearchResultFedora
{
    public string FedoraId { get; set;  }
    public DateTime Created { get; set; }
    public DateTime LastModified { get; set; }
    public long ContentSize { get; set; }
    public string Mime_Type { get; set; }
   

}
