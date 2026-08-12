using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using FluentAssertions;

namespace XmlGen.Tests.Parsing;

/// <summary>
/// Parser-side IDREFS handling: ADMID/DMDID attribute values that are genuine
/// whitespace-separated reference lists (schema-valid IDREFS) must resolve per token when the
/// whole-value lookup misses. Legacy space-containing IDs keep working because the whole raw
/// attribute value is tried first. See docs/issues/188/issue-188-idrefs-plan.md.
/// </summary>
public class IdRefsParsingTests : MetsParserTestBase
{
    [Fact]
    public void Multi_token_ADMID_resolves_per_token_including_the_virus_event_key()
    {
        // ADMID lists two references; only ADM_real exists. The parser must resolve the
        // techMD through the second token, and the ClamAV digiprov key must be derived from
        // the RESOLVED amdSec's ID (digiprovMD_ClamAV_ADM_real), not from the raw attribute
        // value ("digiprovMD_ClamAV_ADM_missing ADM_real" matches nothing).
        var xml = """
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                       xmlns:xlink="http://www.w3.org/1999/xlink"
                       xmlns:premis="http://www.loc.gov/premis/v3">
              <mets:amdSec ID="ADM_real">
                <mets:techMD ID="TECH_real">
                  <mets:mdWrap MDTYPE="PREMIS:OBJECT">
                    <mets:xmlData>
                      <premis:object>
                        <premis:objectCharacteristics>
                          <premis:fixity>
                            <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                            <premis:messageDigest>multiRefDigest</premis:messageDigest>
                          </premis:fixity>
                        </premis:objectCharacteristics>
                      </premis:object>
                    </mets:xmlData>
                  </mets:mdWrap>
                </mets:techMD>
                <mets:digiprovMD ID="digiprovMD_ClamAV_ADM_real">
                  <mets:mdWrap MDTYPE="PREMIS:EVENT">
                    <mets:xmlData>
                      <premis:event>
                        <premis:eventDateTime>2026-03-18T12:00:00Z</premis:eventDateTime>
                        <premis:eventDetailInformation>
                          <premis:eventDetail>ClamAV 1.4.3/27944</premis:eventDetail>
                        </premis:eventDetailInformation>
                        <premis:eventOutcomeInformation>
                          <premis:eventOutcome>pass</premis:eventOutcome>
                          <premis:eventOutcomeDetail>
                            <premis:eventOutcomeDetailNote></premis:eventOutcomeDetailNote>
                          </premis:eventOutcomeDetail>
                        </premis:eventOutcomeInformation>
                      </premis:event>
                    </mets:xmlData>
                  </mets:mdWrap>
                </mets:digiprovMD>
              </mets:amdSec>
              <mets:fileSec>
                <mets:fileGrp>
                  <mets:file ID="FILE_1" MIMETYPE="application/pdf">
                    <mets:FLocat xlink:href="objects/multi.pdf"/>
                  </mets:file>
                </mets:fileGrp>
              </mets:fileSec>
              <mets:structMap TYPE="PHYSICAL">
                <mets:div ADMID="ADM_missing ADM_real">
                  <mets:fptr FILEID="FILE_1"/>
                </mets:div>
              </mets:structMap>
            </mets:mets>
            """;

        var mets = Parse(xml);

        var file = mets.Files.Single(f => f.LocalPath == "objects/multi.pdf");
        file.Digest.Should().Be("multiRefDigest");

        var virus = file.Metadata.OfType<VirusScanMetadata>().Single();
        virus.HasVirus.Should().BeFalse();
        virus.VirusDefinition.Should().Be("ClamAV 1.4.3/27944");
    }

    [Fact]
    public void Multi_token_DMDID_resolves_descriptive_metadata_per_token()
    {
        var xml = """
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                       xmlns:xlink="http://www.w3.org/1999/xlink"
                       xmlns:mods="http://www.loc.gov/mods/v3"
                       xmlns:premis="http://www.loc.gov/premis/v3">
              <mets:dmdSec ID="DMD_real">
                <mets:mdWrap MDTYPE="MODS">
                  <mets:xmlData>
                    <mods:mods>
                      <mods:accessCondition type="restriction on access">Closed</mods:accessCondition>
                    </mods:mods>
                  </mets:xmlData>
                </mets:mdWrap>
              </mets:dmdSec>
              <mets:amdSec ID="ADM_dir">
                <mets:techMD ID="TECH_dir">
                  <mets:mdWrap MDTYPE="PREMIS:OBJECT">
                    <mets:xmlData>
                      <premis:object>
                        <premis:objectCharacteristics/>
                        <premis:originalName>objects/restricted</premis:originalName>
                      </premis:object>
                    </mets:xmlData>
                  </mets:mdWrap>
                </mets:techMD>
              </mets:amdSec>
              <mets:fileSec>
                <mets:fileGrp/>
              </mets:fileSec>
              <mets:structMap TYPE="PHYSICAL">
                <mets:div TYPE="Directory" LABEL="root">
                  <mets:div TYPE="Directory" LABEL="restricted" ADMID="ADM_dir"
                            DMDID="DMD_missing DMD_real"/>
                </mets:div>
              </mets:structMap>
            </mets:mets>
            """;

        var mets = Parse(xml);

        var restricted = mets.PhysicalStructure!
            .FindDirectory("objects/restricted", false);
        restricted.Should().NotBeNull();
        restricted!.AccessRestrictions.Should().ContainSingle().Which.Should().Be("Closed");
    }
}
