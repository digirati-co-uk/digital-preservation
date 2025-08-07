namespace DigitalPreservation.Common.Model.PipelineApi;
public class PipelineJobStates
{
    public const string Waiting = "waiting";
    public const string Running = "processing";
    public const string Completed = "completed";
    public const string CompletedWithErrors = "completedWithErrors";

    public static bool IsComplete(string status)
    {
        return status is Completed or CompletedWithErrors;
    }

    public static bool IsNotComplete(string status)
    {
        return !IsComplete(status);
    }

    public static bool IsSuccess(string status)
    {
        return status is Completed;
    }
}
