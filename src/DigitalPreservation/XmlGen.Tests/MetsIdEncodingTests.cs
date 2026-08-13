using System.Xml;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Mets;
using DigitalPreservation.Utils;
using FluentAssertions;

namespace XmlGen.Tests;

/// <summary>
/// The path→ID encoding introduced by issue #188 step 2. Two properties matter: the result is
/// always usable as an xs:ID once a prefix is added, and nothing about the path is lost.
/// </summary>
public class MetsIdEncodingTests
{
    // Paths chosen to cover every class of NCName-invalid character the platform can meet in a
    // real deposit, plus the ones that are already legal and must pass through untouched.
    public static TheoryData<string> Paths => new()
    {
        "objects/simple.pdf",
        "objects/my file.pdf",
        "objects/my great file.pdf",
        "objects/AT&T guide.pdf",
        "objects/résumé.pdf",
        "objects/2020 minutes.pdf",
        "objects/report (final), v2.pdf",
        "objects/a+b=c#d?e!f@g$h%i^j~k.txt",
        "objects/deep/nested/path/file.tif",
        "objects/quote'and\"quote.txt",
        "objects/semi;colon<gt>lt.txt",
        "metadata/ad-hoc",
        // A path that already looks like an encoding: proves _x is self-escaped rather than
        // being mistaken for an escape sequence on the way back.
        "objects/_x0020_not-a-space.txt",
        "9-leading-digit.txt"
    };

    // ---------------------------------------------------------------------
    // The anchor. Everything else in this file, and every expectation built through MetsIds,
    // computes its expected value with the production encoder - which catches a mint site that
    // FORGETS to encode, but cannot catch the encoding itself changing. These literals are the
    // only thing pinning the actual on-disk format, which is permanent: a step 3 bulk migration
    // will be written against exactly these strings. If one of these fails, the format has
    // changed and every METS file already written is affected.
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("objects/simple.pdf", "objects_x002F_simple.pdf")]
    [InlineData("objects/my file.pdf", "objects_x002F_my_x0020_file.pdf")]
    [InlineData("objects/AT&T guide.pdf", "objects_x002F_AT_x0026_T_x0020_guide.pdf")]
    [InlineData("metadata/ad-hoc", "metadata_x002F_ad-hoc")]
    [InlineData("objects", "objects")]
    public void The_Encoded_Form_Is_Exactly_This(string path, string expected)
    {
        path.ToMetsId().Should().Be(expected);
    }

    [Fact]
    public void A_Minted_Id_Is_Exactly_This()
    {
        MetsIds.Phys("objects/my file.pdf").Should().Be("PHYS_objects_x002F_my_x0020_file.pdf");
        MetsIds.File("objects/my file.pdf").Should().Be("FILE_objects_x002F_my_x0020_file.pdf");
        MetsIds.Adm("objects/my file.pdf").Should().Be("ADM_objects_x002F_my_x0020_file.pdf");
        MetsIds.Tech("objects/my file.pdf").Should().Be("TECH_objects_x002F_my_x0020_file.pdf");
        MetsIds.Dmd("objects/my file.pdf").Should().Be("DMD_objects_x002F_my_x0020_file.pdf");
        Constants.MetadataAdHocDivId.Should().Be("PHYS_metadata_x002F_ad-hoc");
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void Encoded_Path_Is_A_Valid_Ncname(string path)
    {
        var act = () => XmlConvert.VerifyNCName(path.ToMetsId());
        act.Should().NotThrow();
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void Every_Minted_Id_Is_A_Valid_Ncname(string path)
    {
        // What actually goes into the document is prefix + encoded path.
        foreach (var id in new[] { MetsIds.Phys(path), MetsIds.File(path), MetsIds.Adm(path), MetsIds.Tech(path), MetsIds.Dmd(path) })
        {
            var act = () => XmlConvert.VerifyNCName(id);
            act.Should().NotThrow($"'{id}' is written into an xs:ID attribute");
        }
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void Encoding_Is_Reversible(string path)
    {
        path.ToMetsId().DecodeMetsId().Should().Be(path);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void Encoded_Id_Contains_No_Whitespace(string path)
    {
        // The whole point for IDREFS: an ID with a space in it is silently split into several
        // tokens by any schema-aware processor (issue #188 / the IdRefs work).
        path.ToMetsId().Should().NotContainAny(" ", "\t", "\n", "\r");
    }

    [Fact]
    public void A_Path_Needing_No_Encoding_Is_Left_Alone()
    {
        // Keeps the built-in template IDs (PHYS_objects, ADM_metadata …) exactly as they were
        // before step 2, so no existing METS becomes unnavigable by the ID convention.
        FolderNames.Objects.ToMetsId().Should().Be(FolderNames.Objects);
        FolderNames.Metadata.ToMetsId().Should().Be(FolderNames.Metadata);
        MetsIds.Phys(FolderNames.Objects).Should().Be(Constants.ObjectsDivId);
        MetsIds.Phys(FolderNames.Metadata).Should().Be(Constants.MetadataDivId);
    }

    [Fact]
    public void The_Ad_Hoc_Div_Id_Constant_Is_A_Usable_Id()
    {
        // Its exact value is pinned by A_Minted_Id_Is_Exactly_This; comparing it against
        // MetsIds.Phys(...) here would only restate its own definition.
        Constants.MetadataAdHocDivId.Should().NotContain("/");
        var act = () => XmlConvert.VerifyNCName(Constants.MetadataAdHocDivId);
        act.Should().NotThrow();
    }

    [Fact]
    public void Distinct_Paths_Encode_To_Distinct_Ids()
    {
        // Encoding must not collapse two paths onto one ID - that would make two divs collide
        // on a single xs:ID. These pairs differ only in characters that get escaped.
        MetsIds.Phys("objects/a b").Should().NotBe(MetsIds.Phys("objects/a/b"));
        MetsIds.Phys("objects/a b").Should().NotBe(MetsIds.Phys("objects/a_x0020_b"));
    }
}
