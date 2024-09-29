namespace DigitalPreservation.Common.Model.PreservationApi;

public class Deposit : Resource
{
    public override string Type { get; set; } = nameof(Deposit); 
}