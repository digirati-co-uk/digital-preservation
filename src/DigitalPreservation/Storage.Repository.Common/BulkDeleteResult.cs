namespace Storage.Repository.Common;

public class BulkDeleteResult
{
    public Uri? Location { get; set; }
    public int ObjectsToDelete { get; set; }
    public int ObjectsAttempted { get; set; }
    public int ObjectsDeleted { get; set; }
}