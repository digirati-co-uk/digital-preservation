namespace DigitalPreservation.Common.Model.Identity;

/// <summary>
/// Create for Simple search results from Identity API
/// 
/// This avoids having a direct reference in the UI csproj to the Leeds-specific csproj LeedsDlipServices...
/// ...but it's still a Leeds-specific class conceptually. So we need a way of mixing in custom features into
/// a generic preservation platform.
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
