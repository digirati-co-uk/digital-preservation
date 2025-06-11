using System.Text.Json.Serialization;
using Microsoft.VisualBasic.CompilerServices;

namespace DigitalPreservation.Common.Model;

public class Container : PreservedResource
{
    [JsonPropertyOrder(2)]
    [JsonPropertyName("type")]
    public override string Type { get; set; } = nameof(Container);

    [JsonPropertyName("containers")]
    [JsonPropertyOrder(300)]
    public List<Container> Containers { get; set; } = [];
    
    [JsonPropertyName("containerPager")]
    [JsonPropertyOrder(301)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public QueryStringPager? ContainerPager { get; set; }
    
    [JsonPropertyName("binaries")]
    [JsonPropertyOrder(310)]
    public List<Binary> Binaries { get; set; } = [];

    [JsonIgnore]
    public override string StringIcon => Icon;
    
    [JsonIgnore]
    public static string Icon => "📁";

    public (List<Container>, List<Binary>) Flatten()
    {
        var allExistingContainers = new List<Container>();
        var allExistingBinaries = new List<Binary>();
        FlattenInternal(allExistingContainers, allExistingBinaries, this);
        return (allExistingContainers, allExistingBinaries);
    }

    private static void FlattenInternal(
        List<Container> allExistingContainers,
        List<Binary> allExistingBinaries,
        Container traverseContainer)
    {
        foreach (var container in traverseContainer.Containers)
        {
            allExistingContainers.Add(container.CloneForFlatten());
            FlattenInternal(allExistingContainers, allExistingBinaries, container);
        }
        allExistingBinaries.AddRange(traverseContainer.Binaries);
    }

    private Container CloneForFlatten()
    {
        return new Container
        {
            Id = Id,
            Name = Name,
            Created = Created,
            CreatedBy = CreatedBy,
            LastModified = LastModified,
            LastModifiedBy = LastModifiedBy,
            Origin = Origin,
            PartOf = PartOf
        };
    }
    
    
    public BinarySizeTotals GetSizeTotals()
    {
        var totals = new BinarySizeTotals();
        AddBinariesToTotals(this, totals);
        return totals;
    }

    private static void AddBinariesToTotals(Container container, BinarySizeTotals totals)
    {
        totals.TotalContainerCount++;
        
        foreach (var binary in container.Binaries)
        {
            totals.TotalBinaryCount++;
            totals.TotalSize += binary.Size;
        }

        foreach (var childContainer in container.Containers)
        {
            AddBinariesToTotals(childContainer, totals);
        }
    }
}

public class BinarySizeTotals
{
    public int TotalBinaryCount { get; set; } = 0;
    public int TotalContainerCount { get; set; } = -1;
    public long TotalSize { get; set; }
}