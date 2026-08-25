using DigitalPreservation.Mets;
using DigitalPreservation.Common.Model.PreservationApi;
using MediatR;

namespace DigitalPreservation.Workspace;

public class WorkspaceManagerFactory(IMediator mediator, IMetsParser metsParser)
{
    public async Task<WorkspaceManager> CreateAsync(Deposit deposit, bool refresh = false)
    {
        var workspaceManager = new WorkspaceManager(deposit, mediator, metsParser);
        await workspaceManager.InitialiseAsync(refresh);
        return workspaceManager;
    }

    /// <summary>
    /// A manager that has not read anything yet - for the operations that work from the deposit's
    /// own fields rather than from its combined directory.
    /// </summary>
    /// <remarks>
    /// <see cref="CreateAsync"/> eagerly builds the CombinedDirectory, which lists the deposit's
    /// files in S3 and parses the whole METS. That is right for almost everything here, and pure
    /// waste for an operation that goes on to fetch and parse the METS itself: NormaliseMetsIds did
    /// exactly that, reading and parsing the same document twice, once per Archival Group for the
    /// whole of the ID migration campaign. Use this only where the directory is genuinely unused -
    /// a manager built this way has no combined directory, and the methods that need one say so by
    /// failing.
    /// </remarks>
    public WorkspaceManager CreateUninitialised(Deposit deposit) =>
        new(deposit, mediator, metsParser);
}