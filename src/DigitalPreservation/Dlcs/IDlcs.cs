using Dlcs.Hydra;

namespace Dlcs;

public interface IDlcs
{
    Task<Batch> RegisterImages(HydraImageCollection images);

    Task<Batch> GetBatch(string batchId);

    Task<HydraImageCollection> PatchImages(HydraImageCollection images);

    Task<Image?> GetImage(int space, string id);

    // GetImages(ImageQuery query, int defaultSpace, DlcsCallContext dlcsCallContext)
    Task<HydraImageCollection> GetFirstPageOfImages(ImageQuery query, int defaultSpace);

    // GetImages(string nextUri, DlcsCallContext dlcsCallContext) 
    // TODO - this should be a Uri
    Task<HydraImageCollection> GetPageOfImages(Uri nextUri);

    Task<IEnumerable<Image>> GetImagesFromQuery(ImageQuery query);
    Task<Dictionary<string, long>> GetDlcsQueueLevel();
    Task<bool> SpaceExists(int space);
    Task CreateSpace(int space, string? name = null);
}