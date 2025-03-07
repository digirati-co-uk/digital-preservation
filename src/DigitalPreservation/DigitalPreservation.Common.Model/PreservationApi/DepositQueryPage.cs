namespace DigitalPreservation.Common.Model.PreservationApi;

public class DepositQueryPage
{
    public required List<Deposit> Deposits { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
}