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
}
