namespace DigitalPreservation.UI.Features.Preservation;

public class NewDepositModel
{
    // When parent and child slug are provided separately by user
    public string? ParentPathUnderRoot { get; set; }
    public string? ArchivalGroupSlug { get; set; }
    
    // When ArchivalGroup is provided in full
    public string? ArchivalGroupPathUnderRoot { get; set; }
    
    public string? ArchivalGroupProposedName { get; set; }
    
    public string? SubmissionText { get; set; }
    public bool ExpectedToBeNewArchivalGroup { get; set; }
}