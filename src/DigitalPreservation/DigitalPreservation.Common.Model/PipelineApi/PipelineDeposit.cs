
namespace DigitalPreservation.Common.Model.PipelineApi;
public class PipelineDeposit
{
    public required string Id { get; set; }
    public string? DepositId { get; set; }
    public string? Status { get; set; }
    public string? RunUser { get; set; }
    public string? VirusDefinition { get; set; }
    public string? Errors { get; set; }
}
