using DigitalPreservation.Common.Model.DepositHelpers;

namespace DigitalPreservation.Workspace;

public class ItemsAffected
{
    public List<MinimalItem> Items { get; set; } = [];

    /// <summary>
    /// Things the caller should know about items that were added all the same, such as a file
    /// whose content type could not be settled.
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}