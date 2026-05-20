using FluentAssertions;

namespace XmlGen.Tests.Parsing;

/// <summary>
/// Focused tests for METS header information: the item name derived from MODS,
/// and the creator agent that determines whether the METS is editable by this platform.
/// </summary>
public class MetsHeaderTests : MetsParserTestBase
{
    private static string MetsWithMods(string modsContent) => $"""
        <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                   xmlns:xlink="http://www.w3.org/1999/xlink"
                   xmlns:premis="http://www.loc.gov/premis/v3"
                   xmlns:mods="http://www.loc.gov/mods/v3">
          <mets:dmdSec ID="DMD_ROOT">
            <mets:mdWrap MDTYPE="MODS">
              <mets:xmlData>
                <mods:mods>
                  {modsContent}
                </mods:mods>
              </mets:xmlData>
            </mets:mdWrap>
          </mets:dmdSec>
          <mets:amdSec ID="ADM_objects/x.txt">
            <mets:techMD ID="TECH_objects/x.txt">
              <mets:mdWrap MDTYPE="PREMIS:OBJECT">
                <mets:xmlData>
                  <premis:object>
                    <premis:objectCharacteristics>
                      <premis:fixity>
                        <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                        <premis:messageDigest>x</premis:messageDigest>
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
                <mets:FLocat xlink:href="objects/x.txt"/>
              </mets:file>
            </mets:fileGrp>
          </mets:fileSec>
          <mets:structMap TYPE="PHYSICAL">
            <mets:div ADMID="ADM_objects/x.txt">
              <mets:fptr FILEID="FILE_1"/>
            </mets:div>
          </mets:structMap>
        </mets:mets>
        """;

    private static string MetsWithAgent(string agentName) => $"""
        <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                   xmlns:xlink="http://www.w3.org/1999/xlink"
                   xmlns:premis="http://www.loc.gov/premis/v3">
          <mets:metsHdr>
            <mets:agent ROLE="CREATOR" TYPE="OTHER" OTHERTYPE="SOFTWARE">
              <mets:name>{agentName}</mets:name>
            </mets:agent>
          </mets:metsHdr>
          <mets:amdSec ID="ADM_objects/x.txt">
            <mets:techMD ID="TECH_objects/x.txt">
              <mets:mdWrap MDTYPE="PREMIS:OBJECT">
                <mets:xmlData>
                  <premis:object>
                    <premis:objectCharacteristics>
                      <premis:fixity>
                        <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                        <premis:messageDigest>x</premis:messageDigest>
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
                <mets:FLocat xlink:href="objects/x.txt"/>
              </mets:file>
            </mets:fileGrp>
          </mets:fileSec>
          <mets:structMap TYPE="PHYSICAL">
            <mets:div ADMID="ADM_objects/x.txt">
              <mets:fptr FILEID="FILE_1"/>
            </mets:div>
          </mets:structMap>
        </mets:mets>
        """;

    [Fact]
    public void Name_is_extracted_from_mods_title()
    {
        var xml = MetsWithMods("""
            <mods:titleInfo>
              <mods:title>My Digitised Collection</mods:title>
            </mods:titleInfo>
            """);

        var mets = Parse(xml);

        mets.Name.Should().Be("My Digitised Collection");
    }

    [Fact]
    public void Name_falls_back_to_mods_name_when_no_title_element()
    {
        var xml = MetsWithMods("""
            <mods:name>
              <mods:namePart>John Smith</mods:namePart>
            </mods:name>
            """);

        var mets = Parse(xml);

        // mods:name element value is the concatenation of child text — just the namePart
        mets.Name.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Name_is_null_when_no_mods_title_or_name()
    {
        var xml = MetsWithMods("");

        var mets = Parse(xml);

        mets.Name.Should().BeNull();
    }

    [Fact]
    public void Agent_is_extracted_from_mets_agent_element()
    {
        var xml = MetsWithAgent("Some Third-Party Tool");

        var mets = Parse(xml);

        mets.Agent.Should().Be("Some Third-Party Tool");
    }

    [Fact]
    public void Editable_is_true_when_agent_matches_DLIP_creator_constant()
    {
        // Only METS files created by this platform are editable
        var xml = MetsWithAgent("University of Leeds Digital Library Infrastructure Project");

        var mets = Parse(xml);

        mets.Agent.Should().Be("University of Leeds Digital Library Infrastructure Project");
        mets.Editable.Should().BeTrue();
    }

    [Fact]
    public void Editable_is_false_for_third_party_agent()
    {
        var xml = MetsWithAgent("Archivematica");

        var mets = Parse(xml);

        mets.Editable.Should().BeFalse();
    }

    [Fact]
    public void Editable_is_false_when_no_agent_in_mets_header()
    {
        // No metsHdr/agent at all
        var xml = MetsWithMods("");

        var mets = Parse(xml);

        mets.Agent.Should().BeNull();
        mets.Editable.Should().BeFalse();
    }
}
