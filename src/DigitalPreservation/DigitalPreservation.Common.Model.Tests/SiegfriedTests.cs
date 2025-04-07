using DigitalPreservation.Common.Model.ToolOutput.Siegfried;
using FluentAssertions;

namespace DigitalPreservation.Common.Model.Tests;

public class SiegfriedTests
{
    private readonly string input = System.IO.File.ReadAllText("Fixtures/siegfried1.yml");
    
    [Fact]
    public void Can_Load_Test_File()
    {
        var output = Output.FromString(input);
        output.Files.Should().HaveCount(6);
    }
    
    
    [Fact]
    public void Can_Read_Front_Matter()
    {
        var output = Output.FromString(input);
        output.TechnicalProvenance.Should().NotBeNull();
        output.TechnicalProvenance!.Siegfried.Should().Be("1.11.2");
        output.TechnicalProvenance.Identifiers.Should().HaveCount(1);
        output.TechnicalProvenance.Identifiers?[0].Name.Should().Be("pronom");
        output.TechnicalProvenance.Created?.Year.Should().Be(2025);
    }
    
    
    [Fact]
    public void Can_Read_File()
    {
        var output = Output.FromString(input);
        output.Files[5].Should().NotBeNull();
        output.Files[5].Filename.Should().Be("pictures/cover.png");
        output.Files[5].Filesize.Should().Be(1541124);
        output.Files[5].Modified?.Year.Should().Be(2023);
        output.Files[5].Sha256.Should().Be("a470a1cd7e622f44a2aed38b68454fe7ad7b716efb590b5db292a7e1d7b5f2df");
        output.Files[5].Errors.Should().BeNull();
    }
    
        
    [Fact]
    public void Can_Read_File_Matches()
    {
        var output = Output.FromString(input);
        output.Files[5].Matches.Should().NotBeNull();
        output.Files[5].Matches![0].Ns.Should().Be("pronom");
        output.Files[5].Matches![0].Id.Should().Be("fmt/12");
        output.Files[5].Matches![0].Format.Should().Be("Portable Network Graphics");
        output.Files[5].Matches![0].Version.Should().Be("1.1");
        output.Files[5].Matches![0].Mime.Should().Be("image/png");
        output.Files[5].Matches![0].Class.Should().Be("Image (Raster)");
        output.Files[5].Matches![0].Basis.Should().Be("extension match png; byte match at [[0 16] [37 4] [1541112 12]] (signature 3/3)");
        output.Files[5].Matches![0].Warning.Should().BeNull();


    }
}