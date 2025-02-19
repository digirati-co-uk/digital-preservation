namespace DigitalPreservation.Common.Model.Transit;

public class CombinedDirectory
{
    public string? LocalPath { get; set; }
    
    public WorkingDirectory? DirectoryInDeposit { get; set; }
    public WorkingDirectory? DirectoryInMets { get; set; }
    
    public List<CombinedFile> Files { get; set; } = [];
    public List<CombinedDirectory> Directories { get; set; } = [];
}