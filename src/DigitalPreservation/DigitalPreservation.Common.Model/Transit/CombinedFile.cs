namespace DigitalPreservation.Common.Model.Transit;

public class CombinedFile
{
    public WorkingFile? FileInDeposit { get; set; }
    public WorkingFile? FileInMets { get; set; }

    public bool HaveSameHash()
    {
        if (FileInDeposit is null || FileInMets is null)
        {
            return false;
        }
        
        return FileInDeposit.Digest!.Equals(FileInMets.Digest!);
    }
    
}