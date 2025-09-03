using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace DigitalPreservation.Common.Model.Identity;
/// <summary>
/// Create for Simple search results from Identity API
/// </summary>
public class Identifier
{
    public required string Id { get; set; }
    public string? EPid { get; set; }
    public DateTime? Created { get; set; }
    public DateTime? Updated { get; set; }
    public required string CatIrn { get; set; }
    public string? Desc { get; set; }
    public string? Status { get; set; }
    public string? Title { get; set; }
    public Uri? CatalogueApiUri { get; set; }
    public Uri? ManifestUri { get; set; }
    public Uri? RepositoryUri { get; set; }
}
