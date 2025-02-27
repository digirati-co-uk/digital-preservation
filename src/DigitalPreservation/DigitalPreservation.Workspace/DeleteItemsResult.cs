using DigitalPreservation.Common.Model.DepositHelpers;

namespace DigitalPreservation.Workspace;

public class DeleteItemsResult
{
    public List<MinimalItem> DeletedItems { get; set; } = [];
}