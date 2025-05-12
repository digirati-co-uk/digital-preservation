namespace DigitalPreservation.Common.Model;

public class RightsStatement
{
    public static List<RightsStatement> All { get; }
    
    static RightsStatement()
    {
        All =
        [
            // In Copyright
            new RightsStatement
            {
                Label = "In Copyright", 
                ShortLabel = "InC",
                Value = new Uri("http://rightsstatements.org/vocab/InC/1.0/")
            },
            new RightsStatement 
            { 
                Label = "In Copyright - EU Orphan Work", 
                ShortLabel = "InC-EU",
                Value = new Uri("http://rightsstatements.org/vocab/InC-OW-EU/1.0/") 
            },
            new RightsStatement
            {
                Label = "In Copyright - Educational Use Permitted", 
                ShortLabel = "InC-EU",
                Value = new Uri("http://rightsstatements.org/vocab/InC-EDU/1.0/") 
            },
            new RightsStatement
            {
                Label = "In Copyright - Non-Commercial Use Permitted", 
                ShortLabel = "InC-NC",
                Value = new Uri("http://rightsstatements.org/vocab/InC-NC/1.0/") 
            },
            new RightsStatement
            {
                Label = "In Copyright - Rights-holder(s) Unlocatable or Unidentifiable", 
                ShortLabel = "InC-RUU",
                Value = new Uri("http://rightsstatements.org/vocab/InC-RUU/1.0/") 
            },
            
            // No Copyright
            new RightsStatement
            {
                Label = "No Copyright - Contractual Restrictions", 
                ShortLabel = "NoC-CR",
                Value = new Uri("http://rightsstatements.org/vocab/NoC-CR/1.0/") 
            },
            new RightsStatement 
            { 
                Label = "No Copyright - Non-Commercial Use Only", 
                ShortLabel = "NoC-NC",
                Value = new Uri("http://rightsstatements.org/vocab/NoC-NC/1.0/") 
            },
            new RightsStatement 
            { 
                Label = "No Copyright - Other Known Legal Restrictions", 
                ShortLabel = "NoC-OKLR",
                Value = new Uri("http://rightsstatements.org/vocab/NoC-OKLR/1.0/") 
            },
            new RightsStatement
            {
                Label = "No Copyright - United States", 
                ShortLabel = "NoC-US",
                Value = new Uri("http://rightsstatements.org/vocab/NoC-US/1.0/") 
            },
            
            // Other
            new RightsStatement
            {
                Label = "Copyright Not Evaluated", 
                ShortLabel = "CNE",
                Value = new Uri("http://rightsstatements.org/vocab/CNE/1.0/") 
            },
            new RightsStatement
            {
                Label = "Copyright Undetermined", 
                ShortLabel = "UND",
                Value = new Uri("http://rightsstatements.org/vocab/UND/1.0/") 
            },
            new RightsStatement
            {
                Label = "No Known Copyright", 
                ShortLabel = "NKC",
                Value = new Uri("http://rightsstatements.org/vocab/NKC/1.0/") 
            }
        ];
    }
    
    public required string Label { get; set; }
    public required string ShortLabel { get; set; }
    public required Uri Value { get; set; }

    public static string GetShortLabel(Uri modelRootRightsStatement)
    {
        var rs = All.SingleOrDefault(x => x.Value == modelRootRightsStatement);
        return rs?.ShortLabel ?? "???";
    }
}