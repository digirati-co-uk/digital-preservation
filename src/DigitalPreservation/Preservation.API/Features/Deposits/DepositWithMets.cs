using DigitalPreservation.Mets;
using DigitalPreservation.Common.Model.PreservationApi;

namespace Preservation.API.Features.Deposits;

public class DepositWithMets
{
    public required Deposit Deposit { get; set; }
    public required MetsFileWrapper MetsFileWrapper { get; set; }
}
