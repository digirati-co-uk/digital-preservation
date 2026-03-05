namespace Preservation.API.Data.Entities;

public class DepositArchiveJob
{
    public required string Id { get; set; }

    public required string DepositUri { get; set; }

    public required string DepositId { get; set; }
    public required DateTime StartTime { get; set; }
    public required DateTime? EndTime { get; set; }

    public int DeletedCount { get; set; }
    public string Errors { get; set; }

    public string BatchNumber { get; set; }
}
