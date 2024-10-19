using System.Text.Json.Serialization;
using Microsoft.VisualBasic.CompilerServices;

namespace DigitalPreservation.Common.Model;

public class Container : PreservedResource
{
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
    public override string StringIcon => "📁";

    public (List<Container>, List<Binary>) Flatten()
    {
        var allExistingContainers = new List<Container>();
        var allExistingBinaries = new List<Binary>();
        FlatternInternal(allExistingContainers, allExistingBinaries, this);
        return (allExistingContainers, allExistingBinaries);
    }

    private static void FlatternInternal(
        List<Container> allExistingContainers,
        List<Binary> allExistingBinaries,
        Container traverseContainer)
    {
        foreach (var container in traverseContainer.Containers)
        {
            allExistingContainers.Add(container.CloneForFlatten());
            FlatternInternal(allExistingContainers, allExistingBinaries, container);
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
}