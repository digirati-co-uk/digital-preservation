namespace DigitalPreservation.Common.Model.PipelineApi;
public class PipelineOptions
{
    public required string PipelineJobTopicArn { get; set; }
    public required string PipelineJobQueue { get; set; }
    public double PipelineJobsCleanupMinutes { get; set; }
}
