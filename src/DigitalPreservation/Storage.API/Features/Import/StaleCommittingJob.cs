using DigitalPreservation.Common.Model.Import;

namespace Storage.API.Features.Import;

/// <summary>
/// A job whose importer died, or stalled, after starting the commit (issue #444): what
/// <see cref="ImportJobCommitReconciler"/> needs to decide what happened.
/// </summary>
public record StaleCommittingJob(
    string Id,
    ImportJob ImportJob,
    ImportJobResult ImportJobResult,
    DateTime CommitStarted,
    Uri CommitTransaction);
