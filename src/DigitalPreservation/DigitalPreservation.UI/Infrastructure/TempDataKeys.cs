namespace DigitalPreservation.UI.Infrastructure;

/// <summary>
/// Names every TempData key the UI pages read or write, in place of each page repeating its own
/// magic strings - only 3 of which (on the Deposit page) previously had a named constant at all
/// (issue #388 item 4.2).
/// </summary>
public static class TempDataKeys
{
    public const string Error = "Error";
    public const string Deleted = "Deleted";
    public const string Updated = "Updated";
    public const string Valid = "Valid";
    public const string AccessConditionsUpdated = "AccessConditionsUpdated";
    public const string ActiveTab = "ActiveTab";
    public const string Context = "Context";
    public const string ContainerError = "ContainerError";
    public const string ContainerSuccess = "ContainerSuccess";
    public const string CreateDepositFail = "CreateDepositFail";
}
