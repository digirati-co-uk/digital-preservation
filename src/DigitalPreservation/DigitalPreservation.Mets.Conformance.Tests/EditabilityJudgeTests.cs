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
            "wrap the payload of 4 mdWrap(s) in the mets:xmlData element the schema requires",
            "append the platform agent to metsHdr");
    }

    [Fact]
    public void EPrints_Quirks_Are_Noted()
    {
        var judgement = JudgeSample("EPrints.10315.METS.xml");
        Codes(judgement.Notes).Should().Contain("FOREIGN_DMDSEC");
        Codes(judgement.Notes).Should().Contain("NO_XMLDATA_WRAPPER");
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
        string fileId = "eprint_1_1", string? fptrId = null,
        bool wrapped = false, string rootAttrs = "", string divId = "", string extra = "")
    {
        // Faithful to EPrints by default: no mets:xmlData wrapper around the premis:object.
        var openWrap = wrapped ? "<mets:xmlData>" : "";
        var closeWrap = wrapped ? "</mets:xmlData>" : "";
        return Build(
            header: """
                <mets:metsHdr><mets:agent ROLE="CREATOR" TYPE="OTHER" OTHERTYPE="SOFTWARE">
                <mets:name>EPrints 3.3.15</mets:name></mets:agent></mets:metsHdr>
                """,
            amdSec: $"""
                <mets:amdSec ID="AMD_0"><mets:techMD ID="AMD_1">
                <mets:mdWrap MDTYPE="OTHER" MIMETYPE="text/xml">{openWrap}<premis:object>
                <premis:objectCharacteristics><premis:fixity>
                <premis:messageDigestAlgorithm>{algorithm}</premis:messageDigestAlgorithm>
                <premis:messageDigest>abc</premis:messageDigest>
                </premis:fixity></premis:objectCharacteristics>
                </premis:object>{closeWrap}</mets:mdWrap></mets:techMD></mets:amdSec>
                """,
            fileSec: $"""
                <mets:fileSec><mets:fileGrp USE="reference">
                <mets:file ID="{fileId}" ADMID="AMD_1">
                <mets:FLocat LOCTYPE="URL" xlink:type="simple" xlink:href="{href}"/>
                </mets:file></mets:fileGrp></mets:fileSec>
                """,
            structMap: $"""
                <mets:structMap><mets:div {rootAttrs}>
                <mets:div {divId}><mets:fptr FILEID="{fptrId ?? fileId}"/></mets:div>
                </mets:div></mets:structMap>{extra}
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

    // Editability covers everything the platform can edit - logical structMaps (with time and
    // region parts), file links, descriptive metadata - not just the physical tree. Resolvable
    // linkage keeps the tier ("I understand it and I can change it"); dangling linkage loses it.

    [Fact]
    public void A_Platform_Style_File_Link_With_A_Role_Keeps_The_Tier()
    {
        var judgement = EPrintsLike(extra: """
            <mets:structLink>
            <mets:smLink xlink:from="eprint_1_1" xlink:to="eprint_1_1"
                xlink:arcrole="http://iiif.io/api/presentation/3#transcript"/>
            </mets:structLink>
            """);
        judgement.Verdict.Should().Be(Verdicts.EditableWithNormalisation);
    }

    [Fact]
    public void A_Goobi_Style_Div_Link_That_Resolves_Keeps_The_Tier()
    {
        var judgement = EPrintsLike(
            divId: "ID=\"PHYS_1\"",
            extra: """
                <mets:structMap TYPE="LOGICAL">
                <mets:div ID="LOG_1" TYPE="Item" LABEL="The item"/>
                </mets:structMap>
                <mets:structLink>
                <mets:smLink xlink:from="LOG_1" xlink:to="PHYS_1"/>
                </mets:structLink>
                """);
        judgement.Verdict.Should().Be(Verdicts.EditableWithNormalisation);
    }

    [Fact]
    public void A_Dangling_Link_End_Demotes_To_Read_Only()
    {
        var judgement = EPrintsLike(extra: """
            <mets:structLink>
            <mets:smLink xlink:from="eprint_1_1" xlink:to="nothing_declares_this"
                xlink:arcrole="http://iiif.io/api/presentation/3#transcript"/>
            </mets:structLink>
            """);
        judgement.Verdict.Should().Be(Verdicts.NavigableReadOnly);
        Codes(judgement.Reasons).Should().Contain("C_SMLINK_TO_RESOLVES");
    }

    [Fact]
    public void A_Logical_StructMap_With_Time_And_Region_Parts_Keeps_The_Tier()
    {
        var judgement = EPrintsLike(extra: """
            <mets:structMap TYPE="LOGICAL">
            <mets:div ID="LOG_0" TYPE="Collection" LABEL="All of it">
              <mets:div ID="LOG_1" TYPE="Item" LABEL="Whole file">
                <mets:fptr FILEID="eprint_1_1"/>
              </mets:div>
              <mets:div ID="LOG_2" TYPE="Item" LABEL="A time segment">
                <mets:fptr><mets:area FILEID="eprint_1_1" BETYPE="TIME"
                    BEGIN="00:00:10" END="00:01:00"/></mets:fptr>
              </mets:div>
              <mets:div ID="LOG_3" TYPE="Item" LABEL="An image region">
                <mets:fptr><mets:area FILEID="eprint_1_1" SHAPE="RECT"
                    COORDS="0,0,100,100"/></mets:fptr>
              </mets:div>
            </mets:div></mets:structMap>
            """);
        judgement.Verdict.Should().Be(Verdicts.EditableWithNormalisation);
    }

    [Fact]
    public void A_Logical_Area_Pointing_At_Nothing_Demotes_To_Read_Only()
    {
        var judgement = EPrintsLike(extra: """
            <mets:structMap TYPE="LOGICAL">
            <mets:div ID="LOG_1" TYPE="Item" LABEL="Broken segment">
              <mets:fptr><mets:area FILEID="nothing_declares_this" BETYPE="TIME"
                  BEGIN="00:00:10" END="00:01:00"/></mets:fptr>
            </mets:div></mets:structMap>
            """);
        judgement.Verdict.Should().Be(Verdicts.NavigableReadOnly);
        Codes(judgement.Reasons).Should().Contain("C_AREA_FILEID_RESOLVES");
    }

    [Fact]
    public void A_Foreign_DmdSec_Is_Noted_And_Never_Edited()
    {
        // The EPrints root dmdSec: claims MODS, holds no mods:mods. The note carries the rule -
        // an edit appends a platform dmdSec ID to the div's DMDID, theirs stays untouched.
        var judgement = EPrintsLike(
            rootAttrs: "DMDID=\"DMD_eprint_1\"",
            extra: """
                <mets:dmdSec ID="DMD_eprint_1"><mets:mdWrap MDTYPE="MODS">
                <mets:xmlData><mets:recordInfo>
                <mets:recordIdentifier source="EPrints">1</mets:recordIdentifier>
                </mets:recordInfo></mets:xmlData>
                </mets:mdWrap></mets:dmdSec>
                """);
        judgement.Verdict.Should().Be(Verdicts.EditableWithNormalisation);
        Codes(judgement.Notes).Should().Contain("FOREIGN_DMDSEC");
    }

    [Fact]
    public void An_Unwrapped_MdWrap_Payload_Is_Noted_And_Repaired_On_Save()
    {
        var judgement = EPrintsLike();
        Codes(judgement.Notes).Should().Contain("NO_XMLDATA_WRAPPER");
        judgement.Mutations.Should().Contain(
            "wrap the payload of 1 mdWrap(s) in the mets:xmlData element the schema requires");
    }

    [Fact]
    public void A_Wrapped_Payload_Needs_No_Repair()
    {
        var judgement = EPrintsLike(wrapped: true);
        judgement.Verdict.Should().Be(Verdicts.EditableWithNormalisation);
        Codes(judgement.Notes).Should().NotContain("NO_XMLDATA_WRAPPER");
        judgement.Mutations.Should().NotContain(m => m.StartsWith("wrap the payload"));
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
