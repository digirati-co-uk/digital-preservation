using System.Xml;
using Preservation.API.IIIF;

namespace Preservation.API.Tests.IIIF;

public class IiifIdsTests
{
    [Theory]
    [InlineData("library/ms-342/objects/page-001.tif", "library_x002F_ms-342_x002F_objects_x002F_page-001.tif")]
    [InlineData("objects/my file (1).tif", "objects_x002F_my_x0020_file_x0020__x0028_1_x0029_.tif")]
    [InlineData("objects/a=b,c.txt", "objects_x002F_a_x003D_b_x002C_c.txt")]
    public void A_Plain_Ascii_Path_Is_The_Mets_Encoding_Of_The_Path(string path, string expected)
    {
        IiifIds.ForPath(path).Should().Be(expected);
    }

    [Theory]
    [InlineData("objects/résumé.pdf")]
    [InlineData("objects/Fürth.tif")]
    [InlineData("objects/日本.txt")]
    [InlineData("objects/\U0001F600.png")]
    [InlineData("objects/straße.txt")]
    [InlineData("objects/a b/c,d=e.tif")]
    public void Every_Id_Is_Ascii_And_A_Valid_Short_Canvas_Id(string path)
    {
        var id = IiifIds.ForPath(path);

        id.All(char.IsAscii).Should().BeTrue();
        id.Should().NotContainAny("/", "=", ",", " ");
        XmlConvert.VerifyNCName(id); // throws if not
        id.Length.Should().BeLessThanOrEqualTo(IiifIds.MaxLength);
    }

    [Fact]
    public void Folding_Diacritics_Keeps_The_Id_Readable_And_Adds_A_Hash()
    {
        var id = IiifIds.ForPath("objects/résumé.pdf");

        id.Should().StartWith("objects_x002F_resume.pdf_h");
        id.Should().MatchRegex("_h[0-9a-f]{12}$");
    }

    [Fact]
    public void A_Folded_Path_Does_Not_Collide_With_The_Plain_Path_It_Folds_To()
    {
        IiifIds.ForPath("objects/résumé.pdf").Should().NotBe(IiifIds.ForPath("objects/resume.pdf"));
        IiifIds.ForPath("objects/résumé.pdf").Should().NotBe(IiifIds.ForPath("objects/resumé.pdf"));
    }

    [Fact]
    public void A_Character_With_No_Decomposition_Is_Escaped_Not_Dropped()
    {
        // ß does not decompose, so nothing is folded and no hash is needed: the escape loses nothing.
        IiifIds.ForPath("objects/straße.txt").Should().Be("objects_x002F_stra_x00DF_e.txt");
        IiifIds.ForPath("objects/\U0001F600.png").Should().Be("objects_x002F__x0001F600_.png");
    }

    [Fact]
    public void An_Escape_We_Add_Cannot_Be_Confused_With_Text_In_The_Path()
    {
        // The METS encoding escapes a literal "_x", so a file actually named like an escape stays distinct.
        IiifIds.ForPath("objects/stra_x00DF_e.txt").Should().NotBe(IiifIds.ForPath("objects/straße.txt"));
    }

    [Fact]
    public void A_Long_Path_Is_Truncated_With_A_Hash_And_Stays_Unique()
    {
        var folder = string.Join("/", Enumerable.Repeat("a-very-long-folder-name", 10));
        var first = IiifIds.ForPath($"{folder}/objects/file-one.tif");
        var second = IiifIds.ForPath($"{folder}/objects/file-two.tif");

        first.Length.Should().Be(IiifIds.MaxLength);
        first.Should().MatchRegex("_h[0-9a-f]{12}$");
        first.Should().NotBe(second);
    }

    [Fact]
    public void The_Id_Is_Stable()
    {
        IiifIds.ForPath("objects/résumé.pdf").Should().Be(IiifIds.ForPath("objects/résumé.pdf"));
    }
}
