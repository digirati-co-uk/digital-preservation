using System.Text.Json;
using FluentAssertions;

namespace DigitalPreservation.Common.Model.Tests;

public class PolymorphicSerialisationTests
{
    private readonly JsonSerializerOptions jOpts = new() { WriteIndented = true };
    [Fact]
    public void Polymorphic_Container_Serialisation()
    {
        Resource resource = new Container {Id = new Uri("https://example.com/test")};
        var json = JsonSerializer.Serialize(resource, jOpts);
        Resource? deSer = Deserializer.Parse(json);
        deSer!.GetType().Should().Be(typeof(Container));
    }
}