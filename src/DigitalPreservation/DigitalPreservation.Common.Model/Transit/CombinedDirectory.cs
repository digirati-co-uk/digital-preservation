namespace DigitalPreservation.Common.Model.Transit;

public class CombinedDirectory(WorkingDirectory? directoryInDeposit, WorkingDirectory? directoryInMets)
{
    public string? LocalPath => DirectoryInDeposit?.LocalPath ?? DirectoryInMets?.LocalPath;
    public string? Name => DirectoryInDeposit?.Name ?? DirectoryInMets?.Name;

    public WorkingDirectory? DirectoryInDeposit { get; } = directoryInDeposit;
    public WorkingDirectory? DirectoryInMets { get; } = directoryInMets;

    public List<CombinedFile> Files { get; set; } = [];
    public List<CombinedDirectory> Directories { get; set; } = [];
    
    public bool? HaveSameName()
    {
        if (DirectoryInDeposit is null || DirectoryInMets is null)
        {
            return null;
        }
        
        return DirectoryInDeposit.Name!.Equals(DirectoryInMets.Name!);
    }
}