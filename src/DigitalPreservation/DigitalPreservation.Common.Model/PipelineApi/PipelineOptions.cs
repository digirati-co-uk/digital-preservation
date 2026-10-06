namespace DigitalPreservation.Common.Model.PipelineApi;
public class PipelineOptions
{
    public required string PipelineJobTopicArn { get; set; }
    public required string PipelineJobQueue { get; set; }
    /// <summary>
    /// How long a pipeline job may sit waiting or running before the Preservation API's background
    /// sweep (issue #301) closes it out as completedWithErrors. Read by the Preservation API only -
    /// moved there from the UI, which used to do this cleanup as a side effect of rendering the
    /// Deposit page.
    /// </summary>
    public double PipelineJobsCleanupMinutes { get; set; } = 1440;
}
