
namespace DigitalPreservation.Common.Model.PipelineApi;
public class PipelineDeposit
{
    public string Id { get; set; }
    public string? DepositId { get; set; }
    public string Status { get; set; }
    public string? RunUser { get; set; }
    public string? Errors { get; set; }
}
