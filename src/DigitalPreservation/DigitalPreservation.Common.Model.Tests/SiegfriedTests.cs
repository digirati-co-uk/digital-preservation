using DigitalPreservation.Common.Model.ToolOutput.Siegfried;
using FluentAssertions;

namespace DigitalPreservation.Common.Model.Tests;

public class SiegfriedTests
{
    private readonly string inputYaml = System.IO.File.ReadAllText("Fixtures/siegfried.yml");
    private readonly string inputCsv = System.IO.File.ReadAllText("Fixtures/siegfried.csv");
    
    [Fact]
    public void Can_Load_Test_File_Yaml()
    {
        var output = SiegfriedOutput.FromYamlString(inputYaml);
        output.Files.Should().HaveCount(14);
    }
    
    [Fact]
    public void Can_Load_Test_File_Csv()
    {
        var output = SiegfriedOutput.FromCsvString(inputCsv);
        output.Files.Should().HaveCount(14);
    }
    
    
    [Fact]
    public void Can_Read_Front_Matter()
    {
        var output = SiegfriedOutput.FromYamlString(inputYaml);
        output.TechnicalProvenance.Should().NotBeNull();
        output.TechnicalProvenance!.Siegfried.Should().StartWith("1.11.0");
        output.TechnicalProvenance.Identifiers.Should().HaveCount(1);
        output.TechnicalProvenance.Identifiers?[0].Name.Should().Be("pronom");
        output.TechnicalProvenance.Created?.Year.Should().Be(2023);
    }
    
    
    [Fact]
    public void Can_Read_File_Yaml()
    {
        var output = SiegfriedOutput.FromYamlString(inputYaml);
        output.Files[5].Should().NotBeNull();
        output.Files[5].Filename?.EndsWith("bc-example-1/objects/nyc/DSCF0981.JPG").Should().BeTrue();
        output.Files[5].Filesize.Should().Be(8148972);
        output.Files[5].Modified?.Year.Should().Be(2025);
        output.Files[5].Sha256.Should().Be("77e3e1c6920aef1b77c35e9830fa54d274b7beef54d6e25d56f6dbe0874f29b0");
        output.Files[5].Errors.Should().BeNull();
    }
    
    
    [Fact]
    public void Can_Read_File_Csv()
    {
        var output = SiegfriedOutput.FromCsvString(inputCsv);
        output.Files[5].Should().NotBeNull();
        output.Files[5].Filename?.EndsWith("bc-example-1/objects/nyc/DSCF0981.JPG").Should().BeTrue();
        output.Files[5].Filesize.Should().Be(8148972);
        output.Files[5].Modified?.Year.Should().Be(2025);
        output.Files[5].Sha256.Should().Be("77e3e1c6920aef1b77c35e9830fa54d274b7beef54d6e25d56f6dbe0874f29b0");
        output.Files[5].Errors.Should().BeNull();
    }
        
    [Fact]
    public void Can_Read_File_Matches_Yaml()
    {
        var output = SiegfriedOutput.FromYamlString(inputYaml);
        output.Files[5].Matches.Should().HaveCount(1);
        output.Files[5].Matches[0].Ns.Should().Be("pronom");
        output.Files[5].Matches[0].Id.Should().Be("fmt/1507");
        output.Files[5].Matches[0].Format.Should().Be("Exchangeable Image File Format (Compressed)");
        output.Files[5].Matches[0].Version.Should().Be("2.3.x");
        output.Files[5].Matches[0].Mime.Should().Be("image/jpeg");
        output.Files[5].Matches[0].Class.Should().Be("Image (Raster)");
        output.Files[5].Matches[0].Basis.Should().Be("extension match jpg; byte match at [[0 16] [426 12] [8148970 2]] (signature 2/2)");
        output.Files[5].Matches[0].Warning.Should().BeNull();
    }
    
    
    [Fact]
    public void Can_Read_File_Matches_Csv()
    {
        var output = SiegfriedOutput.FromCsvString(inputCsv);
        output.Files[5].Matches.Should().HaveCount(1);
        output.Files[5].Matches[0].Ns.Should().Be("pronom");
        output.Files[5].Matches[0].Id.Should().Be("fmt/1507");
        output.Files[5].Matches[0].Format.Should().Be("Exchangeable Image File Format (Compressed)");
        output.Files[5].Matches[0].Version.Should().Be("2.3.x");
        output.Files[5].Matches[0].Mime.Should().Be("image/jpeg");
        output.Files[5].Matches[0].Class.Should().Be("Image (Raster)");
        output.Files[5].Matches[0].Basis.Should().Be("extension match jpg; byte match at [[0 16] [426 12] [8148970 2]] (signature 2/2)");
        output.Files[5].Matches[0].Warning.Should().BeNull();
    }
}