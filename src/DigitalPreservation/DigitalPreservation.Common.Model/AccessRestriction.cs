namespace DigitalPreservation.Common.Model;

public class AccessRestriction
{
    public static List<AccessRestriction> All { get; }
    
    static AccessRestriction()
    {
        All =
        [
            new AccessRestriction { Label = "Open", Value = "Open" },
            new AccessRestriction { Label = "Restricted", Value = "Restricted" },
            new AccessRestriction { Label = "Staff", Value = "Staff" },
            new AccessRestriction { Label = "Closed", Value = "Closed" }
        ];
    }
    
    public required string Label { get; set; }
    public required string Value { get; set; }
}

