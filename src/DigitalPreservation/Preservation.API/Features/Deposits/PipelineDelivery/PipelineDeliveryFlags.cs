namespace Preservation.API.Features.Deposits.PipelineDelivery;

/// <summary>
/// Bound from the FeatureFlags section, alongside the service's other flags (mirrors
/// DigitalPreservation.Mets.MetsManagerOptions).
/// </summary>
public class PipelineDeliveryFlags
{
    /// <summary>
    /// Deliver pipeline jobs to the Pipeline API directly over HTTP instead of publishing to SNS
    /// (issue #352) - for local development/testing only. The Pipeline API's own
    /// FeatureFlags:UseLocalHostedServiceForPipeline must also be on for anything to actually
    /// consume what this delivers; this flag alone only changes how RunPipelineHandler hands the
    /// job off. Both default off in every deployed environment - SNS/SQS is unaffected there.
    /// </summary>
    public bool UseLocalPipelineDelivery { get; set; }
}
