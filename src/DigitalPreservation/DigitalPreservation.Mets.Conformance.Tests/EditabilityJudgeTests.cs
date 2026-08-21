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
        string href = "objects/a.jpg", string algorithm = "SHA256", string digest = "abc",
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
                <premis:messageDigest>{digest}</premis:messageDigest>
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
                <mets:file ID="f2"><mets:FLocat xlink:href="objects/a.jpg"/></mets:file>
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

    private static Judgement PlatformLike(string algorithm = "SHA256", string digest = "abc")
    {
        return Build(
            header: """
                <mets:metsHdr><mets:agent ROLE="CREATOR" TYPE="OTHER" OTHERTYPE="SOFTWARE">
                <mets:name>University of Leeds Digital Library Infrastructure Project</mets:name>
                </mets:agent></mets:metsHdr>
                """,
            amdSec: $"""
                <mets:amdSec ID="ADM_objects_x002F_a.jpg">
                <mets:techMD ID="TECH_objects_x002F_a.jpg">
                <mets:mdWrap MDTYPE="PREMIS:OBJECT"><mets:xmlData><premis:object>
                <premis:objectCharacteristics><premis:fixity>
                <premis:messageDigestAlgorithm>{algorithm}</premis:messageDigestAlgorithm>
                <premis:messageDigest>{digest}</premis:messageDigest>
                </premis:fixity></premis:objectCharacteristics>
                <premis:originalName>objects/a.jpg</premis:originalName>
                </premis:object></mets:xmlData></mets:mdWrap></mets:techMD></mets:amdSec>
                """,
            fileSec: """
                <mets:fileSec><mets:fileGrp USE="OBJECTS">
                <mets:file ID="FILE_objects_x002F_a.jpg" ADMID="ADM_objects_x002F_a.jpg">
                <mets:FLocat LOCTYPE="URL" xlink:type="simple" xlink:href="objects/a.jpg"/>
                </mets:file></mets:fileGrp></mets:fileSec>
                """,
            structMap: """
                <mets:structMap TYPE="PHYSICAL">
                <mets:div ID="PHYS_ROOT" LABEL="__ROOT" TYPE="Directory">
                <mets:div ID="PHYS_objects_x002F_a.jpg" LABEL="a.jpg" TYPE="Item">
                <mets:fptr FILEID="FILE_objects_x002F_a.jpg"/></mets:div>
                </mets:div></mets:structMap>
                """);
    }

    [Fact]
    public void The_Smallest_Platform_Document_Is_Editable()
    {
        PlatformLike().Verdict.Should().Be(Verdicts.Editable);
    }

    [Fact]
    public void A_Platform_Document_Without_Fixity_Is_Read_Only()
    {
        // Every edit ends in an import job, and import jobs require SHA256 - a platform-shape
        // document that has lost its digests cannot complete an edit-and-preserve.
        var judgement = PlatformLike(algorithm: "MD5");
        judgement.Verdict.Should().Be(Verdicts.NavigableReadOnly);
        Codes(judgement.Reasons).Should().Contain("P_SHA256");
    }

    [Fact]
    public void A_Logical_StructMap_Without_A_Root_Id_Demotes_To_Read_Only()
    {
        // Logical structMaps are edited by address: replaced, reordered, removed by root div
        // ID. An ID-less one is present but unchangeable - which editable must not mean.
        var judgement = EPrintsLike(extra: """
            <mets:structMap TYPE="LOGICAL">
            <mets:div TYPE="Item" LABEL="Unaddressable"/>
            </mets:structMap>
            """);
        judgement.Verdict.Should().Be(Verdicts.NavigableReadOnly);
        Codes(judgement.Reasons).Should().Contain("C_LOGICAL_ROOT_HAS_ID");
    }

    // One test per fixed finding from the 2026-08-21 adversarial review of PR #238.

    [Fact]
    public void A_Physical_Fptr_Without_Fileid_Demotes()
    {
        // The platform's physical walk calls GetRequiredFileId on every fptr; an area-only
        // fptr (legal in a logical map, where the platform itself writes them) crashes it.
        var judgement = Build(
            fileSec: """
                <mets:fileSec><mets:fileGrp USE="reference">
                <mets:file ID="f1"><mets:FLocat xlink:href="objects/a.jpg"/></mets:file>
                </mets:fileGrp></mets:fileSec>
                """,
            structMap: """
                <mets:structMap><mets:div>
                <mets:div><mets:fptr><mets:area FILEID="f1" BETYPE="TIME"
                    BEGIN="00:00:01" END="00:00:02"/></mets:fptr></mets:div>
                </mets:div></mets:structMap>
                """);
        judgement.Verdict.Should().Be(Verdicts.NavigableReadOnly);
        Codes(judgement.Reasons).Should().Contain("C_PHYSICAL_FPTR_HAS_FILEID");
    }

    [Fact]
    public void A_File_With_Two_FLocats_Demotes()
    {
        // The platform's parser takes .Single() FLocat per file; two locations throw it.
        var judgement = Build(
            fileSec: """
                <mets:fileSec><mets:fileGrp USE="reference">
                <mets:file ID="f1">
                <mets:FLocat xlink:href="objects/a.jpg"/>
                <mets:FLocat xlink:href="objects/mirror/a.jpg"/>
                </mets:file></mets:fileGrp></mets:fileSec>
                """,
            structMap: """
                <mets:structMap><mets:div>
                <mets:div><mets:fptr FILEID="f1"/></mets:div>
                </mets:div></mets:structMap>
                """);
        judgement.Verdict.Should().Be(Verdicts.NavigableReadOnly);
        Codes(judgement.Reasons).Should().Contain("C_ONE_FLOCAT");
    }

    [Fact]
    public void An_Empty_Digest_Fails_The_Fixity_Rules()
    {
        // An algorithm label with no digest value is a record of having lost the checksum.
        var eprints = EPrintsLike(digest: "");
        eprints.Verdict.Should().Be(Verdicts.NavigableReadOnly);
        Codes(eprints.Reasons).Should().Contain("E_SHA256");

        var platform = PlatformLike(digest: "");
        platform.Verdict.Should().Be(Verdicts.NavigableReadOnly);
        Codes(platform.Reasons).Should().Contain("P_SHA256");
    }

    [Fact]
    public void A_Useless_First_FileGrp_Still_Counts_As_Mixed()
    {
        // XPath != against a missing attribute is silently false; the second clause of
        // E_MIXED_FILEGRP_USE catches presence-mixing.
        var judgement = Build(
            header: """
                <mets:metsHdr><mets:agent><mets:name>EPrints</mets:name></mets:agent>
                </mets:metsHdr>
                """,
            fileSec: """
                <mets:fileSec>
                <mets:fileGrp>
                <mets:file ID="f1"><mets:FLocat xlink:href="objects/a.jpg"/></mets:file>
                </mets:fileGrp>
                <mets:fileGrp USE="original">
                <mets:file ID="f2"><mets:FLocat xlink:href="objects/b.jpg"/></mets:file>
                </mets:fileGrp></mets:fileSec>
                """,
            structMap: """
                <mets:structMap><mets:div>
                <mets:div><mets:fptr FILEID="f1"/></mets:div>
                <mets:div><mets:fptr FILEID="f2"/></mets:div>
                </mets:div></mets:structMap>
                """);
        judgement.Verdict.Should().Be(Verdicts.NavigableReadOnly);
        Codes(judgement.Reasons).Should().Contain("E_MIXED_FILEGRP_USE");
    }

    [Fact]
    public void An_Empty_Document_Gives_Its_Reason()
    {
        // The platform's own freshly-created deposit skeleton: structure, no files yet.
        var judgement = Build(
            header: """
                <mets:metsHdr><mets:agent><mets:name>University of Leeds Digital
                Library Infrastructure Project</mets:name></mets:agent></mets:metsHdr>
                """,
            fileSec: """<mets:fileSec><mets:fileGrp USE="OBJECTS"/></mets:fileSec>""",
            structMap: """
                <mets:structMap TYPE="PHYSICAL">
                <mets:div ID="PHYS_ROOT" LABEL="__ROOT" TYPE="Directory">
                <mets:div ID="PHYS_objects" LABEL="objects" TYPE="Directory" ADMID="ADM_objects"/>
                </mets:div></mets:structMap>
                """);
        judgement.Verdict.Should().Be(Verdicts.NotEditable);
        Codes(judgement.Reasons).Should().BeEquivalentTo("NO_FILES");
    }

    [Fact]
    public void One_File_Referenced_From_Two_Divs_Is_Not_A_Duplicate_Path()
    {
        var judgement = Build(
            header: """
                <mets:metsHdr><mets:agent><mets:name>EPrints 3.3.15</mets:name></mets:agent>
                </mets:metsHdr>
                """,
            amdSec: """
                <mets:amdSec ID="AMD_0"><mets:techMD ID="AMD_1">
                <mets:mdWrap MDTYPE="OTHER" MIMETYPE="text/xml"><premis:object>
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
                <mets:div><mets:fptr FILEID="f1"/></mets:div>
                </mets:div></mets:structMap>
                """);
        Codes(judgement.Reasons).Should().NotContain("DUPLICATE_PATH");
        judgement.Verdict.Should().Be(Verdicts.EditableWithNormalisation);
        judgement.FileCount.Should().Be(1);
    }

    [Fact]
    public void An_Unchosen_Second_StructMap_Cannot_Demote()
    {
        // CONTRACT.md: with several candidates, the first is judged. A nested sibling map is
        // preserved as parsed, never edited - it must not fail the tier for the chosen one.
        var judgement = EPrintsLike(extra: """
            <mets:structMap>
            <mets:div><mets:div><mets:div>
            <mets:fptr FILEID="eprint_1_1"/>
            </mets:div></mets:div></mets:div></mets:structMap>
            """);
        judgement.Verdict.Should().Be(Verdicts.EditableWithNormalisation);
        Codes(judgement.Notes).Should().Contain("MULTIPLE_PHYSICAL_CANDIDATES");
    }

    [Fact]
    public void A_Conformant_Unchosen_Map_Cannot_Carry_The_Platform_Tier()
    {
        // The walked map and the validated map must be the same map.
        var judgement = Build(
            header: """
                <mets:metsHdr><mets:agent><mets:name>University of Leeds Digital
                Library Infrastructure Project</mets:name></mets:agent></mets:metsHdr>
                """,
            amdSec: """
                <mets:amdSec ID="ADM_objects_x002F_a.jpg">
                <mets:techMD ID="TECH_objects_x002F_a.jpg">
                <mets:mdWrap MDTYPE="PREMIS:OBJECT"><mets:xmlData><premis:object>
                <premis:objectCharacteristics><premis:fixity>
                <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                <premis:messageDigest>abc</premis:messageDigest>
                </premis:fixity></premis:objectCharacteristics>
                </premis:object></mets:xmlData></mets:mdWrap></mets:techMD></mets:amdSec>
                """,
            fileSec: """
                <mets:fileSec><mets:fileGrp USE="OBJECTS">
                <mets:file ID="FILE_1" ADMID="ADM_objects_x002F_a.jpg">
                <mets:FLocat xlink:href="objects/a.jpg"/></mets:file>
                </mets:fileGrp></mets:fileSec>
                """,
            structMap: """
                <mets:structMap TYPE="physical"><mets:div>
                <mets:div><mets:div><mets:fptr FILEID="FILE_1"/></mets:div></mets:div>
                </mets:div></mets:structMap>
                <mets:structMap TYPE="PHYSICAL">
                <mets:div ID="PHYS_ROOT" TYPE="Directory">
                <mets:div ID="PHYS_1" TYPE="Item"><mets:fptr FILEID="FILE_1"/></mets:div>
                </mets:div></mets:structMap>
                """);
        judgement.Verdict.Should().Be(Verdicts.NavigableReadOnly);
        Codes(judgement.Reasons).Should().Contain("P_PHYSICAL_STRUCTMAP");
    }

    // Open findings from the METS Identifier Audit that belong to the judge's contract.

    [Fact]
    public void A_Labelless_Directory_Div_Fails_The_Platform_Tier()
    {
        // Audit finding P7: adding into a LABEL-less parent crashes the platform.
        var judgement = Build(
            header: """
                <mets:metsHdr><mets:agent><mets:name>University of Leeds Digital
                Library Infrastructure Project</mets:name></mets:agent></mets:metsHdr>
                """,
            amdSec: """
                <mets:amdSec ID="ADM_1"><mets:techMD ID="TECH_1">
                <mets:mdWrap MDTYPE="PREMIS:OBJECT"><mets:xmlData><premis:object>
                <premis:objectCharacteristics><premis:fixity>
                <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                <premis:messageDigest>abc</premis:messageDigest>
                </premis:fixity></premis:objectCharacteristics>
                <premis:originalName>objects/a.jpg</premis:originalName>
                </premis:object></mets:xmlData></mets:mdWrap></mets:techMD></mets:amdSec>
                """,
            fileSec: """
                <mets:fileSec><mets:fileGrp USE="OBJECTS">
                <mets:file ID="FILE_1" ADMID="ADM_1">
                <mets:FLocat xlink:href="objects/a.jpg"/></mets:file>
                </mets:fileGrp></mets:fileSec>
                """,
            structMap: """
                <mets:structMap TYPE="PHYSICAL">
                <mets:div ID="PHYS_ROOT" LABEL="__ROOT" TYPE="Directory">
                <mets:div ID="PHYS_objects" TYPE="Directory" ADMID="ADM_1">
                <mets:div ID="PHYS_1" LABEL="a.jpg" TYPE="Item">
                <mets:fptr FILEID="FILE_1"/></mets:div>
                </mets:div></mets:div></mets:structMap>
                """);
        judgement.Verdict.Should().Be(Verdicts.NavigableReadOnly);
        Codes(judgement.Reasons).Should().Contain("P_DIRECTORY_LABEL");
    }

    [Theory]
    [InlineData("objects//a.jpg")]
    [InlineData("objects/./a.jpg")]
    [InlineData("objects/a.jpg/")]
    public void An_Unnormalised_Href_Is_A_Blocker(string href)
    {
        // Audit finding M3: the platform's path cache does not collapse empty segments, so
        // normalising here would make the judge more tolerant than the platform.
        var judgement = EPrintsLike(href: href);
        judgement.Verdict.Should().Be(Verdicts.NotEditable);
        Codes(judgement.Reasons).Should().Contain("HREF_NOT_NORMALISED");
    }

    [Fact]
    public void A_Shared_Editable_DmdSec_Demotes()
    {
        // Audit finding P5: editing metadata on one div rewrites the shared section in place,
        // silently changing the other div's metadata.
        var judgement = EPrintsLike(
            rootAttrs: "DMDID=\"DMD_1\"", divId: "DMDID=\"DMD_1\" ID=\"PHYS_1\"",
            extra: """
                <mets:dmdSec ID="DMD_1"><mets:mdWrap MDTYPE="MODS"><mets:xmlData>
                <mods:mods xmlns:mods="http://www.loc.gov/mods/v3">
                <mods:titleInfo><mods:title>Shared</mods:title></mods:titleInfo>
                </mods:mods></mets:xmlData></mets:mdWrap></mets:dmdSec>
                """);
        judgement.Verdict.Should().Be(Verdicts.NavigableReadOnly);
        Codes(judgement.Reasons).Should().Contain("SHARED_DMDSEC");
    }

    [Fact]
    public void A_Shared_Foreign_DmdSec_Is_Fine()
    {
        // The platform never edits a foreign dmdSec (it appends alongside), so sharing one
        // is safe - only a shared MODS-editable section demotes.
        var judgement = EPrintsLike(
            rootAttrs: "DMDID=\"DMD_eprint_1\"", divId: "DMDID=\"DMD_eprint_1\" ID=\"PHYS_1\"",
            extra: """
                <mets:dmdSec ID="DMD_eprint_1"><mets:mdWrap MDTYPE="MODS">
                <mets:xmlData><mets:recordInfo>
                <mets:recordIdentifier source="EPrints">1</mets:recordIdentifier>
                </mets:recordInfo></mets:xmlData></mets:mdWrap></mets:dmdSec>
                """);
        judgement.Verdict.Should().Be(Verdicts.EditableWithNormalisation);
        Codes(judgement.Reasons).Should().NotContain("SHARED_DMDSEC");
    }

    [Fact]
    public void A_Missing_File_Is_A_Judgement_Not_A_Crash()
    {
        var judgement = EditabilityJudge.JudgeFile(
            Path.Combine(AppContext.BaseDirectory, "does-not-exist.xml"));
        judgement.Verdict.Should().Be(Verdicts.NotEditable);
        Codes(judgement.Reasons).Should().BeEquivalentTo("PARSE_FAILED");
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
