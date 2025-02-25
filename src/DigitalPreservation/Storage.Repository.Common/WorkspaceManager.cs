using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.Transit;

namespace Storage.Repository.Common;

/// <summary>
/// Usually a deposit but not necessarily
/// </summary>
public class WorkspaceManager()
{    
    private MetsFileWrapper? metsFileWrapper;
    private WorkingDirectory? files;
    private bool metsFileWrapperAttempted;
    
}