namespace Preservation.API.Features.Deposits.PipelineDelivery;

/// <summary>
/// Where to reach the Pipeline API for local HTTP delivery of pipeline jobs (issue #352). Only
/// meaningful, and only bound/registered, when FeatureFlags:UseLocalPipelineDelivery is on - see
/// ServiceCollectionX.AddPipelineJobDeliveryClient.
/// </summary>
public class PipelineApiClientOptions
{
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
}
