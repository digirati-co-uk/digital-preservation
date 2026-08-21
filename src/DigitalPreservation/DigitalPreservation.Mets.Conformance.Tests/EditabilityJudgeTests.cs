using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace DigitalPreservation.Mets.Conformance.Tests;

/// <summary>
/// CONTRACT.md's acceptance table over the real sample corpus, plus the native rules. The
/// Python judge (src/mets-editability/tests.py) enforces the same table over the same files;
/// a divergence between the two suites is a contract violation, and CONTRACT.md decides which
/// implementation is wrong.
/// </summary>
public class EditabilityJudgeTests
{
    private static Judgement JudgeSample(string name) =>
        EditabilityJudge.JudgeFile(Path.Combine(AppContext.BaseDirectory, "Samples", name));

    private static IEnumerable<string> Codes(IEnumerable<Finding> findings) =>
        findings.Select(finding => finding.Code);

    [Fact]
    public void Simple_Image_Is_Editable()
    {
        var judgement = JudgeSample("simple-image.mets.xml");
        judgement.Verdict.Should().Be(Verdicts.Editable);
        judgement.Reasons.Should().BeEmpty();
    }

    [Fact]
    public void Wow_Is_Editable()
    {
        JudgeSample("wow.mets.xml").Verdict.Should().Be(Verdicts.Editable);
    }

    [Fact]
    public void Legacy_Platform_Mets_Is_Editable_With_A_Legacy_Ids_Note()
    {
        var judgement = JudgeSample("path-fixture-spaces.xml");
        judgement.Verdict.Should().Be(Verdicts.Editable);
        Codes(judgement.Notes).Should().Contain("LEGACY_IDS");
    }

    [Fact]
    public void EPrints_Is_Editable_With_Normalisation()
    {
        var judgement = JudgeSample("EPrints.10315.METS.xml");
        judgement.Verdict.Should().Be(Verdicts.EditableWithNormalisation);
        judgement.FileCount.Should().Be(4);
        Codes(judgement.Assumptions).Should().BeEquivalentTo(
            "UNTYPED_STRUCTMAP_ASSUMED_PHYSICAL", "UNTYPED_DIV_ASSUMED_ITEM",
            "IMPLIED_OBJECTS_DIV");
        Codes(judgement.Notes).Should().Contain("METS_NAMESPACE_RECORD_INFO");
    }

    [Fact]
    public void EPrints_Mutations_Are_The_02e_Contract_In_Order()
    {
        JudgeSample("EPrints.10315.METS.xml").Mutations.Should().Equal(
            "set TYPE=\"PHYSICAL\" on the structMap",
            "set TYPE=\"Directory\" on the root div",
            "set TYPE=\"Item\" on 4 file div(s)",
            "materialise the objects Directory div (amdSec/techMD with premis:originalName) " +
            "and re-parent 4 file div(s) under it",
            "consolidate 4 fileGrp(s) into one USE=\"OBJECTS\" group",
            "append the platform agent to metsHdr");
    }

    [Fact]
    public void Archivematica_Is_Navigable_Read_Only()
    {
        var judgement = JudgeSample("archivematica-wc-METS.299eb16f-1e62-4bf6-b259-c82146153711.xml");
        judgement.Verdict.Should().Be(Verdicts.NavigableReadOnly);
        judgement.FileCount.Should().Be(38);
        Codes(judgement.Notes).Should().Contain("DIRECTORY_DIV_NO_ADMID");
        Codes(judgement.Assumptions).Should().Contain("CASE_INSENSITIVE_STRUCTMAP_TYPE");
    }

    [Fact]
    public void Goobi_Wellcome_Is_Navigable_Read_Only()
    {
        // Relative paths (a bagged Wellcome item), but typed page divs, ALTO outside objects/,
        // no SHA256 - neither tier. The living-editor policy that keeps Goobi read-only
        // regardless is the platform's overlay; the judge reports what the document shows.
        JudgeSample("goobi-wc-b29356350.xml").Verdict.Should().Be(Verdicts.NavigableReadOnly);
    }

    [Fact]
    public void Goobi_2026_Is_Not_Editable()
    {
        var judgement = JudgeSample("goobi-2026.xml");
        judgement.Verdict.Should().Be(Verdicts.NotEditable);
        Codes(judgement.Reasons).Should().Contain("HREF_NOT_DEPOSIT_RELATIVE");
    }

    private static Judgement Build(
        string structMap = "", string fileSec = "", string amdSec = "", string header = "")
    {
        var xml = $"""
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                xmlns:xlink="http://www.w3.org/1999/xlink"
                xmlns:premis="http://www.loc.gov/premis/v3">
              {header}{amdSec}{fileSec}{structMap}
            </mets:mets>
            """;
        return EditabilityJudge.Judge(XDocument.Parse(xml));
    }

    private static Judgement EPrintsLike(
        string href = "objects/a.jpg", string algorithm = "SHA256",
        string fileId = "eprint_1_1", string? fptrId = null)
    {
        return Build(
            header: """
                <mets:metsHdr><mets:agent ROLE="CREATOR" TYPE="OTHER" OTHERTYPE="SOFTWARE">
                <mets:name>EPrints 3.3.15</mets:name></mets:agent></mets:metsHdr>
                """,
            amdSec: $"""
                <mets:amdSec ID="AMD_0"><mets:techMD ID="AMD_1">
                <mets:mdWrap MDTYPE="OTHER" MIMETYPE="text/xml"><premis:object>
                <premis:objectCharacteristics><premis:fixity>
                <premis:messageDigestAlgorithm>{algorithm}</premis:messageDigestAlgorithm>
                <premis:messageDigest>abc</premis:messageDigest>
                </premis:fixity></premis:objectCharacteristics>
                </premis:object></mets:mdWrap></mets:techMD></mets:amdSec>
                """,
            fileSec: $"""
                <mets:fileSec><mets:fileGrp USE="reference">
                <mets:file ID="{fileId}" ADMID="AMD_1">
                <mets:FLocat LOCTYPE="URL" xlink:type="simple" xlink:href="{href}"/>
                </mets:file></mets:fileGrp></mets:fileSec>
                """,
            structMap: $"""
                <mets:structMap><mets:div>
                <mets:div><mets:fptr FILEID="{fptrId ?? fileId}"/></mets:div>
                </mets:div></mets:structMap>
                """);
    }

    [Fact]
    public void The_Smallest_EPrints_Document_Reaches_The_Tier()
    {
        EPrintsLike().Verdict.Should().Be(Verdicts.EditableWithNormalisation);
    }

    [Fact]
    public void An_Unresolved_Fileid_Is_Not_Editable()
    {
        var judgement = EPrintsLike(fptrId: "nothing_declares_this");
        judgement.Verdict.Should().Be(Verdicts.NotEditable);
        Codes(judgement.Reasons).Should().Contain("FILEID_UNRESOLVED");
    }

    [Theory]
    [InlineData("https://example.org/a.jpg")]
    [InlineData("file:///usr/share/eprints/a.jpg")]
    [InlineData("objects/../secrets.txt")]
    [InlineData("/objects/a.jpg")]
    public void A_Non_Deposit_Relative_Href_Fails_The_Guard(string href)
    {
        var judgement = EPrintsLike(href: href);
        judgement.Verdict.Should().Be(Verdicts.NotEditable);
        Codes(judgement.Reasons).Should().Contain("HREF_NOT_DEPOSIT_RELATIVE");
    }

    [Fact]
    public void Missing_Sha256_Fails_The_EPrints_Tier()
    {
        var judgement = EPrintsLike(algorithm: "MD5");
        judgement.Verdict.Should().Be(Verdicts.NavigableReadOnly);
        Codes(judgement.Reasons).Should().Contain("E_SHA256");
    }

    [Theory]
    [InlineData("SHA-256")]
    [InlineData("sha256")]
    [InlineData("sha-256")]
    public void Sha256_Spellings_Are_Accepted(string spelling)
    {
        EPrintsLike(algorithm: spelling).Verdict.Should().Be(Verdicts.EditableWithNormalisation);
    }

    [Fact]
    public void An_Illegal_Id_Demotes_The_EPrints_Tier_To_Read_Only()
    {
        // An EPrints-shaped document whose IDs need the #188 normalisation is not this tier's
        // to restructure: normalisation is a different, prior operation.
        var judgement = EPrintsLike(fileId: "eprint 1 1", fptrId: "eprint 1 1");
        judgement.Verdict.Should().Be(Verdicts.NavigableReadOnly);
        Codes(judgement.Reasons).Should().Contain("INVALID_IDS");
    }

    [Fact]
    public void A_Logical_Only_Document_Has_No_Physical_StructMap()
    {
        var judgement = Build(
            structMap: """<mets:structMap TYPE="LOGICAL"><mets:div/></mets:structMap>""");
        judgement.Verdict.Should().Be(Verdicts.NotEditable);
        Codes(judgement.Reasons).Should().Contain("NO_PHYSICAL_STRUCTMAP");
    }

    [Fact]
    public void A_Duplicate_Declared_Id_Is_A_Blocker()
    {
        var judgement = Build(
            fileSec: """
                <mets:fileSec><mets:fileGrp USE="reference">
                <mets:file ID="f1"><mets:FLocat xlink:href="objects/a.jpg"/></mets:file>
                <mets:file ID="f1"><mets:FLocat xlink:href="objects/b.jpg"/></mets:file>
                </mets:fileGrp></mets:fileSec>
                """,
            structMap: """
                <mets:structMap><mets:div>
                <mets:div><mets:fptr FILEID="f1"/></mets:div>
                </mets:div></mets:structMap>
                """);
        judgement.Verdict.Should().Be(Verdicts.NotEditable);
        Codes(judgement.Reasons).Should().Contain("DUPLICATE_ID");
    }

    [Fact]
    public void Two_Files_Claiming_One_Path_Is_A_Blocker()
    {
        var judgement = Build(
            fileSec: """
                <mets:fileSec><mets:fileGrp USE="reference">
                <mets:file ID="f1"><mets:FLocat xlink:href="objects/a.jpg"/></mets:file>
                <mets:file ID="f2"><mets:FLocat xlink:href="objects/./a.jpg"/></mets:file>
                </mets:fileGrp></mets:fileSec>
                """,
            structMap: """
                <mets:structMap><mets:div>
                <mets:div><mets:fptr FILEID="f1"/></mets:div>
                <mets:div><mets:fptr FILEID="f2"/></mets:div>
                </mets:div></mets:structMap>
                """);
        Codes(judgement.Reasons).Should().Contain("DUPLICATE_PATH");
    }

    [Fact]
    public void A_Foreign_Storage_Assertion_Is_Noted_Not_Read()
    {
        var judgement = Build(
            header: """
                <mets:metsHdr><mets:agent><mets:name>EPrints 3.3.15</mets:name></mets:agent>
                </mets:metsHdr>
                """,
            amdSec: """
                <mets:amdSec ID="AMD_0"><mets:techMD ID="AMD_1">
                <mets:mdWrap MDTYPE="OTHER" MIMETYPE="text/xml"><premis:object>
                <premis:storage><premis:contentLocation>
                <premis:contentLocationType>URL</premis:contentLocationType>
                <premis:contentLocationValue>file:///usr/share/eprints/a.jpg</premis:contentLocationValue>
                </premis:contentLocation></premis:storage>
                <premis:objectCharacteristics><premis:fixity>
                <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                <premis:messageDigest>abc</premis:messageDigest>
                </premis:fixity></premis:objectCharacteristics>
                </premis:object></mets:mdWrap></mets:techMD></mets:amdSec>
                """,
            fileSec: """
                <mets:fileSec><mets:fileGrp USE="reference">
                <mets:file ID="f1" ADMID="AMD_1">
                <mets:FLocat xlink:href="objects/a.jpg"/></mets:file>
                </mets:fileGrp></mets:fileSec>
                """,
            structMap: """
                <mets:structMap><mets:div>
                <mets:div><mets:fptr FILEID="f1"/></mets:div>
                </mets:div></mets:structMap>
                """);
        judgement.Verdict.Should().Be(Verdicts.EditableWithNormalisation);
        Codes(judgement.Notes).Should().Contain("FOREIGN_STORAGE_LOCATION");
    }
}
