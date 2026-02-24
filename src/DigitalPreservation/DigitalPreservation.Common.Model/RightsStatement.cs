namespace DigitalPreservation.Common.Model;

public class RightsStatement
{
    public static List<RightsStatement> All { get; }
    public static List<RightsStatement> CreativeCommons { get; }

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
                ShortLabel = "InC-OW-EU",
                Value = new Uri("http://rightsstatements.org/vocab/InC-OW-EU/1.0/") 
            },
            new RightsStatement
            {
                Label = "In Copyright - Educational Use Permitted", 
                ShortLabel = "InC-EDU",
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

        CreativeCommons =
        [
            new RightsStatement
            {
                ShortLabel = "CC0",
                Label = "Creative Commons Zero",
                Value = new Uri("https://creativecommons.org/publicdomain/zero/1.0/")
            },
            new RightsStatement
            {
                ShortLabel = "CC-BY",
                Label = "Attribution",
                Value = new Uri("https://creativecommons.org/licenses/by/4.0/")
            },
            new RightsStatement
            {
                ShortLabel = "CC-BY-SA",
                Label = "Attribution-ShareAlike",
                Value = new Uri("https://creativecommons.org/licenses/by-sa/4.0/")
            },
            new RightsStatement
            {
                ShortLabel = "CC-BY-ND",
                Label = "Attribution-NoDerivatives",
                Value = new Uri("https://creativecommons.org/licenses/by-nd/4.0/")
            },
            new RightsStatement
            {
                ShortLabel = "CC-BY-NC",
                Label = "Attribution-NonCommercial",
                Value = new Uri("https://creativecommons.org/licenses/by-nc/4.0/")
            },
            new RightsStatement
            {
                ShortLabel = "CC-BY-NC-SA",
                Label = "Attribution-NonCommercial-ShareAlike",
                Value = new Uri("https://creativecommons.org/licenses/by-nc-sa/4.0/")
            },
            new RightsStatement
            {
                ShortLabel = "CC-BY-NC-ND",
                Label = "Attribution-NonCommercial-NoDerivatives",
                Value = new Uri("https://creativecommons.org/licenses/by-nc-nd/4.0/")
            },
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