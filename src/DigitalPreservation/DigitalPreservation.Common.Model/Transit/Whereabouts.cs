namespace DigitalPreservation.Common.Model.Transit;

public enum Whereabouts
{
    Both,
    Mets,
    Deposit,
    Neither,
    Extra
}

public static class FolderNames
{
    public const string Objects = "objects";
    public const string Metadata = "metadata";
    public const string BagItData = "data";
}
