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
    
    // The request has come for a particular location; it MAY have a slug 
    public bool FromBrowseContext { get; set; }

    public bool UseObjectTemplate { get; set; } = true;
    public bool Export { get; set; }
}