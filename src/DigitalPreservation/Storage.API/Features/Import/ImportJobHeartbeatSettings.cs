namespace Storage.API.Features.Import;

/// <summary>
/// Validates the relationship between the two config values that must stay in step: whichever host
/// actually runs <see cref="ImportJobRunner"/> - the Storage API in in-process mode, or the separate
/// SQS Importer in deployed environments - writes a heartbeat every
/// <c>ImportJobs:HeartbeatIntervalSeconds</c> from its own configuration; <c>ImportJobResultStore</c>
/// (always in the Storage API, which does the reaping regardless of which host executes jobs) treats
/// a heartbeat older than <c>ImportJobs:HeartbeatWindowMinutes</c> as abandoned. If the interval were
/// ever configured at or above the window, a live job's own gaps between heartbeats could be
/// mistaken for abandonment - caught here, at startup, in whichever host actually uses the interval,
/// rather than discovered later as jobs failing inexplicably mid-run (issue #354 review: the Storage
/// API checking only its own two settings missed a misconfigured importer interval in SQS mode).
/// </summary>
public static class ImportJobHeartbeatSettings
{
    public static void Validate(IConfiguration configuration)
    {
        var heartbeatIntervalSeconds = configuration.GetValue("ImportJobs:HeartbeatIntervalSeconds", 60);
        var heartbeatWindowMinutes = configuration.GetValue("ImportJobs:HeartbeatWindowMinutes", 10);
        if (heartbeatIntervalSeconds >= heartbeatWindowMinutes * 60)
        {
            throw new InvalidOperationException(
                $"ImportJobs:HeartbeatIntervalSeconds ({heartbeatIntervalSeconds}) must be smaller than " +
                $"ImportJobs:HeartbeatWindowMinutes ({heartbeatWindowMinutes}) x 60 seconds, or a live job's " +
                "own heartbeat gaps could be mistaken for abandonment.");
        }
    }
}
