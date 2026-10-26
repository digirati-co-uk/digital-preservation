using FluentAssertions;

namespace XmlGen.Tests.Parsing;

/// <summary>
/// Focused tests for descriptive metadata extraction: access restrictions, rights statements,
/// and record identifiers from MODS dmdSec elements linked via DMDID on physical structMap divs.
/// </summary>
public class DescriptiveMetadataTests : MetsParserTestBase
{
    private const string FileAmdSec = """
        <mets:amdSec ID="ADM_objects/file.txt">
          <mets:techMD ID="TECH_objects/file.txt">
            <mets:mdWrap MDTYPE="PREMIS:OBJECT">
              <mets:xmlData>
                <premis:object>
                  <premis:objectCharacteristics>
                    <premis:fixity>
                      <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                      <premis:messageDigest>abc</premis:messageDigest>
                    </premis:fixity>
                  </premis:objectCharacteristics>
                </premis:object>
              </mets:xmlData>
            </mets:mdWrap>
          </mets:techMD>
        </mets:amdSec>
        """;

    private static string WithDmdSec(string dmdId, string modsContent, string divDmdIdAttr = "") => $"""
        <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                   xmlns:xlink="http://www.w3.org/1999/xlink"
                   xmlns:premis="http://www.loc.gov/premis/v3"
                   xmlns:mods="http://www.loc.gov/mods/v3">
          <mets:dmdSec ID="{dmdId}">
            <mets:mdWrap MDTYPE="MODS">
              <mets:xmlData>
                <mods:mods>
                  {modsContent}
                </mods:mods>
              </mets:xmlData>
            </mets:mdWrap>
          </mets:dmdSec>
          {FileAmdSec}
          <mets:fileSec>
            <mets:fileGrp>
              <mets:file ID="FILE_1" MIMETYPE="text/plain">
                <mets:FLocat xlink:href="objects/file.txt"/>
              </mets:file>
            </mets:fileGrp>
          </mets:fileSec>
          <mets:structMap TYPE="PHYSICAL">
            <mets:div {divDmdIdAttr} ADMID="ADM_objects/file.txt">
              <mets:fptr FILEID="FILE_1"/>
            </mets:div>
          </mets:structMap>
        </mets:mets>
        """;

    /// <summary>
    /// The real EPrints corpus shape (issue #237): mdWrap/@MDTYPE="MODS", but xmlData holds its
    /// content directly in the METS namespace - no mods:mods wrapper at all - alongside whatever
    /// mods: elements EPrints did prefix correctly.
    /// </summary>
    private static string WithRawDmdSec(
        string dmdId, string xmlDataContent, string divDmdIdAttr = "", string mdType = "MODS") => $"""
        <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                   xmlns:xlink="http://www.w3.org/1999/xlink"
                   xmlns:premis="http://www.loc.gov/premis/v3"
                   xmlns:mods="http://www.loc.gov/mods/v3">
          <mets:dmdSec ID="{dmdId}">
            <mets:mdWrap MDTYPE="{mdType}">
              <mets:xmlData>
                {xmlDataContent}
              </mets:xmlData>
            </mets:mdWrap>
          </mets:dmdSec>
          {FileAmdSec}
          <mets:fileSec>
            <mets:fileGrp>
              <mets:file ID="FILE_1" MIMETYPE="text/plain">
                <mets:FLocat xlink:href="objects/file.txt"/>
              </mets:file>
            </mets:fileGrp>
          </mets:fileSec>
          <mets:structMap TYPE="PHYSICAL">
            <mets:div {divDmdIdAttr} ADMID="ADM_objects/file.txt">
              <mets:fptr FILEID="FILE_1"/>
            </mets:div>
          </mets:structMap>
        </mets:mets>
        """;

    [Fact]
    public void EPrints_style_mets_namespaced_record_identifiers_are_read_when_MDTYPE_is_MODS()
    {
        // EPrints writes its record identifiers - including the id.library.leeds.ac.uk PID - as
        // mets:recordIdentifier rather than mods:recordIdentifier, even though mdWrap/@MDTYPE
        // claims the content is MODS.
        var xml = WithRawDmdSec(
            "DMD_file",
            """
            <mods:titleInfo><mods:title>Growing</mods:title></mods:titleInfo>
            <mets:recordInfo>
              <mets:recordIdentifier source="EPrints">36675</mets:recordIdentifier>
              <mets:recordIdentifier source="id.library.leeds.ac.uk">xvc3d1gg</mets:recordIdentifier>
              <mets:recordIdentifier source="EMu">441458</mets:recordIdentifier>
            </mets:recordInfo>
            """,
            "DMDID=\"DMD_file\"");

        var mets = Parse(xml);

        var ids = mets.Files[0].RecordInfo!.RecordIdentifiers;
        ids.Should().HaveCount(3);
        ids.Select(i => i.Source).Should().Contain(["EPrints", "id.library.leeds.ac.uk", "EMu"]);
        ids.Single(i => i.Source == "id.library.leeds.ac.uk").Value.Should().Be("xvc3d1gg");
    }

    [Fact]
    public void Mets_namespaced_record_identifier_is_ignored_when_MDTYPE_is_not_MODS()
    {
        // The fallback is scoped to the document's own MODS claim - it must not scavenge
        // arbitrary METS-namespace elements out of metadata that was never claimed to be MODS.
        var xml = WithRawDmdSec(
            "DMD_file",
            """
            <mets:recordInfo>
              <mets:recordIdentifier source="EPrints">36675</mets:recordIdentifier>
            </mets:recordInfo>
            """,
            "DMDID=\"DMD_file\"",
            mdType: "OTHER");

        var mets = Parse(xml);

        mets.Files[0].RecordInfo.Should().BeNull();
    }

    [Fact]
    public void Mods_record_identifier_wins_over_mets_namespaced_one_for_the_same_source()
    {
        // Once #223 makes an EPrints document editable, a write adds a genuine
        // mods:recordIdentifier alongside the untouched EPrints original - the mods: value must
        // win so the document does not report the same identifier twice.
        var xml = WithRawDmdSec(
            "DMD_file",
            """
            <mods:recordInfo>
              <mods:recordIdentifier source="id.library.leeds.ac.uk">new-pid</mods:recordIdentifier>
            </mods:recordInfo>
            <mets:recordInfo>
              <mets:recordIdentifier source="id.library.leeds.ac.uk">xvc3d1gg</mets:recordIdentifier>
              <mets:recordIdentifier source="EMu">441458</mets:recordIdentifier>
            </mets:recordInfo>
            """,
            "DMDID=\"DMD_file\"");

        var mets = Parse(xml);

        var ids = mets.Files[0].RecordInfo!.RecordIdentifiers;
        ids.Should().HaveCount(2);
        ids.Single(i => i.Source == "id.library.leeds.ac.uk").Value.Should().Be("new-pid");
        ids.Single(i => i.Source == "EMu").Value.Should().Be("441458");
    }

    [Fact]
    public void Access_restriction_set_on_file_when_div_has_DMDID_with_restriction_on_access()
    {
        var xml = WithDmdSec(
            "DMD_file",
            """<mods:accessCondition type="restriction on access">Level1</mods:accessCondition>""",
            "DMDID=\"DMD_file\"");

        var mets = Parse(xml);

        var file = mets.Files[0];
        file.AccessRestrictions.Should().NotBeNull();
        file.AccessRestrictions.Should().HaveCount(1);
        file.AccessRestrictions![0].Should().Be("Level1");
    }

    [Fact]
    public void Rights_statement_set_on_file_when_div_has_DMDID_with_use_and_reproduction()
    {
        var xml = WithDmdSec(
            "DMD_file",
            """<mods:accessCondition type="use and reproduction">http://rightsstatements.org/vocab/InC/1.0/</mods:accessCondition>""",
            "DMDID=\"DMD_file\"");

        var mets = Parse(xml);

        var file = mets.Files[0];
        file.RightsStatement.Should().Be(new Uri("http://rightsstatements.org/vocab/InC/1.0/"));
    }

    [Fact]
    public void Record_identifier_set_on_file_with_source_attribute()
    {
        var xml = WithDmdSec(
            "DMD_file",
            """
            <mods:recordInfo>
              <mods:recordIdentifier source="identity-service">abc123xyz</mods:recordIdentifier>
            </mods:recordInfo>
            """,
            "DMDID=\"DMD_file\"");

        var mets = Parse(xml);

        var file = mets.Files[0];
        file.RecordInfo.Should().NotBeNull();
        file.RecordInfo!.RecordIdentifiers.Should().HaveCount(1);
        file.RecordInfo.RecordIdentifiers[0].Source.Should().Be("identity-service");
        file.RecordInfo.RecordIdentifiers[0].Value.Should().Be("abc123xyz");
    }

    [Fact]
    public void Multiple_record_identifiers_are_all_extracted()
    {
        var xml = WithDmdSec(
            "DMD_file",
            """
            <mods:recordInfo>
              <mods:recordIdentifier source="identity-service">pid001</mods:recordIdentifier>
              <mods:recordIdentifier source="EMu">MS 1234/5</mods:recordIdentifier>
            </mods:recordInfo>
            """,
            "DMDID=\"DMD_file\"");

        var mets = Parse(xml);

        var ids = mets.Files[0].RecordInfo!.RecordIdentifiers;
        ids.Should().HaveCount(2);
        ids[0].Source.Should().Be("identity-service");
        ids[0].Value.Should().Be("pid001");
        ids[1].Source.Should().Be("EMu");
        ids[1].Value.Should().Be("MS 1234/5");
    }

    [Fact]
    public void All_three_mods_metadata_types_extracted_together_from_same_dmdSec()
    {
        var xml = WithDmdSec(
            "DMD_file",
            """
            <mods:accessCondition type="restriction on access">Closed</mods:accessCondition>
            <mods:accessCondition type="use and reproduction">http://rightsstatements.org/vocab/CNE/1.0/</mods:accessCondition>
            <mods:recordInfo>
              <mods:recordIdentifier source="identity-service">xyz789</mods:recordIdentifier>
            </mods:recordInfo>
            """,
            "DMDID=\"DMD_file\"");

        var mets = Parse(xml);

        var file = mets.Files[0];
        file.AccessRestrictions![0].Should().Be("Closed");
        file.RightsStatement.Should().Be(new Uri("http://rightsstatements.org/vocab/CNE/1.0/"));
        file.RecordInfo!.RecordIdentifiers[0].Value.Should().Be("xyz789");
    }

    [Fact]
    public void Goobi_status_access_condition_type_is_treated_as_access_restriction()
    {
        // Goobi uses type="status" instead of type="restriction on access"
        var xml = WithDmdSec(
            "DMD_file",
            """<mods:accessCondition type="status">Restricted</mods:accessCondition>""",
            "DMDID=\"DMD_file\"");

        var mets = Parse(xml);

        var file = mets.Files[0];
        file.AccessRestrictions.Should().NotBeNull();
        file.AccessRestrictions![0].Should().Be("Restricted");
    }

    [Fact]
    public void Div_with_no_DMDID_has_null_access_rights_and_record_info()
    {
        // dmdSec exists but the div does not reference it
        var xml = WithDmdSec(
            "DMD_orphan",
            """<mods:accessCondition type="restriction on access">Level1</mods:accessCondition>""",
            // No DMDID attribute on the div
            "");

        var mets = Parse(xml);

        var file = mets.Files[0];
        file.AccessRestrictions.Should().BeNull();
        file.RightsStatement.Should().BeNull();
        file.RecordInfo.Should().BeNull();
    }

    [Fact]
    public void DMD_on_directory_div_sets_access_restriction_on_directory()
    {
        var xml = """
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                       xmlns:xlink="http://www.w3.org/1999/xlink"
                       xmlns:premis="http://www.loc.gov/premis/v3"
                       xmlns:mods="http://www.loc.gov/mods/v3">
              <mets:dmdSec ID="DMD_objects">
                <mets:mdWrap MDTYPE="MODS">
                  <mets:xmlData>
                    <mods:mods>
                      <mods:accessCondition type="restriction on access">Level2</mods:accessCondition>
                      <mods:accessCondition type="use and reproduction">http://rightsstatements.org/vocab/InC/1.0/</mods:accessCondition>
                      <mods:recordInfo>
                        <mods:recordIdentifier source="EMu">COLL/123</mods:recordIdentifier>
                      </mods:recordInfo>
                    </mods:mods>
                  </mets:xmlData>
                </mets:mdWrap>
              </mets:dmdSec>
              <mets:amdSec ID="ADM_objects">
                <mets:techMD ID="TECH_objects">
                  <mets:mdWrap MDTYPE="PREMIS:OBJECT">
                    <mets:xmlData>
                      <premis:object>
                        <premis:objectCharacteristics/>
                        <premis:originalName>objects</premis:originalName>
                      </premis:object>
                    </mets:xmlData>
                  </mets:mdWrap>
                </mets:techMD>
              </mets:amdSec>
              <mets:amdSec ID="ADM_objects/file.txt">
                <mets:techMD ID="TECH_objects/file.txt">
                  <mets:mdWrap MDTYPE="PREMIS:OBJECT">
                    <mets:xmlData>
                      <premis:object>
                        <premis:objectCharacteristics>
                          <premis:fixity>
                            <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                            <premis:messageDigest>abc</premis:messageDigest>
                          </premis:fixity>
                        </premis:objectCharacteristics>
                      </premis:object>
                    </mets:xmlData>
                  </mets:mdWrap>
                </mets:techMD>
              </mets:amdSec>
              <mets:fileSec>
                <mets:fileGrp>
                  <mets:file ID="FILE_1" MIMETYPE="text/plain">
                    <mets:FLocat xlink:href="objects/file.txt"/>
                  </mets:file>
                </mets:fileGrp>
              </mets:fileSec>
              <mets:structMap TYPE="PHYSICAL">
                <mets:div ID="PHYS_objects" TYPE="Directory" LABEL="objects"
                          DMDID="DMD_objects" ADMID="ADM_objects">
                  <mets:div ADMID="ADM_objects/file.txt">
                    <mets:fptr FILEID="FILE_1"/>
                  </mets:div>
                </mets:div>
              </mets:structMap>
            </mets:mets>
            """;

        var mets = Parse(xml);

        var dir = mets.PhysicalStructure!.Directories[0];
        dir.AccessRestrictions.Should().NotBeNull();
        dir.AccessRestrictions![0].Should().Be("Level2");
        dir.RightsStatement.Should().Be(new Uri("http://rightsstatements.org/vocab/InC/1.0/"));
        dir.RecordInfo!.RecordIdentifiers[0].Source.Should().Be("EMu");
        dir.RecordInfo.RecordIdentifiers[0].Value.Should().Be("COLL/123");

        // File div has no DMDID — file itself has no explicit metadata
        mets.Files[0].AccessRestrictions.Should().BeNull();
        mets.Files[0].RightsStatement.Should().BeNull();
        mets.Files[0].RecordInfo.Should().BeNull();
    }

    [Fact]
    public void Untyped_root_div_with_no_direct_file_pointers_anchors_record_info_at_physical_root()
    {
        // Real EPrints METS (issue #237's second review pass): DMDID sits directly on the
        // structMap's root div, which carries no TYPE at all and no mets:fptr of its own - the
        // actual files are one level further down, on divs with no DMDID of their own. Neither
        // existing branch applied to a div shaped like this (not type="Directory", so it never
        // reaches the WorkingDirectory-creation path or the untyped-structural-with-no-ADMID root
        // fallback), so its own record info had nowhere to land until this div was recognised as
        // its own third case: untyped, and holding no file pointer of its own.
        var xml = """
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                       xmlns:xlink="http://www.w3.org/1999/xlink"
                       xmlns:premis="http://www.loc.gov/premis/v3"
                       xmlns:mods="http://www.loc.gov/mods/v3">
              <mets:dmdSec ID="DMD_eprint">
                <mets:mdWrap MDTYPE="MODS">
                  <mets:xmlData>
                    <mods:recordInfo>
                      <mods:recordIdentifier source="EPrints">12345</mods:recordIdentifier>
                    </mods:recordInfo>
                  </mets:xmlData>
                </mets:mdWrap>
              </mets:dmdSec>
              <mets:amdSec ID="ADM_objects/file.txt">
                <mets:techMD ID="TECH_objects/file.txt">
                  <mets:mdWrap MDTYPE="PREMIS:OBJECT">
                    <mets:xmlData>
                      <premis:object>
                        <premis:objectCharacteristics>
                          <premis:fixity>
                            <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                            <premis:messageDigest>abc</premis:messageDigest>
                          </premis:fixity>
                        </premis:objectCharacteristics>
                      </premis:object>
                    </mets:xmlData>
                  </mets:mdWrap>
                </mets:techMD>
              </mets:amdSec>
              <mets:fileSec>
                <mets:fileGrp>
                  <mets:file ID="FILE_1" MIMETYPE="text/plain">
                    <mets:FLocat xlink:href="objects/file.txt"/>
                  </mets:file>
                </mets:fileGrp>
              </mets:fileSec>
              <mets:structMap>
                <mets:div DMDID="DMD_eprint" ADMID="ADM_eprint">
                  <mets:div ADMID="ADM_objects/file.txt">
                    <mets:fptr FILEID="FILE_1"/>
                  </mets:div>
                </mets:div>
              </mets:structMap>
            </mets:mets>
            """;

        var mets = Parse(xml);

        mets.Files[0].RecordInfo.Should().BeNull("the file's own div carries no DMDID");
        mets.Files[0].EffectiveRecordInfo.Should().NotBeNull(
            "it should inherit from the untyped root div's own DMD, now anchored at the physical root");
        mets.Files[0].EffectiveRecordInfo!.RecordIdentifiers.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Source = "EPrints", Value = "12345" });
    }
}
