using DigitalPreservation.Common.Model.DepositHelpers;

namespace DigitalPreservation.UI.Workspace;

public class DeleteItemsResult
{
    public List<MinimalItem> DeletedItems { get; set; } = [];
}