namespace Preservation.API.Data.Entities;

/// <summary>
/// Represents a Deposit made to Preservation service
/// </summary>
public class Deposit
{
    /// <summary>
    /// Auto incrementing PK for deposit
    /// </summary>
    public int Id { get; set; }
    
    /// <summary>
    /// Created timestamp
    /// </summary>
    public DateTime CreatedOn { get; set; }
}