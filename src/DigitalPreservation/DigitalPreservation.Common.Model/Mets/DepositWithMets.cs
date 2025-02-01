using DigitalPreservation.Common.Model.PreservationApi;

namespace DigitalPreservation.Common.Model.Mets;

public class DepositWithMets
{
    public required Deposit Deposit { get; set; }
    public required MetsFileWrapper MetsFileWrapper { get; set; }
}