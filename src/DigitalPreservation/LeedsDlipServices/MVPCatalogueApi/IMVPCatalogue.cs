using DigitalPreservation.Common.Model.Results;

namespace LeedsDlipServices.MVPCatalogueApi;

public interface IMvpCatalogue
{
    public Task<Result<CatalogueRecord>> GetCatalogueRecordByPid(string pid, CancellationToken cancellationToken);
}