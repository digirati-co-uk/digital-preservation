namespace DigitalPreservation.Common.Model.Import;

public static class ImportJobStates
{
    public const string Waiting = "waiting";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string CompletedWithErrors = "completedWithErrors";

    public static bool IsComplete(string status)
    {
        return status is Completed or CompletedWithErrors;
    }
}