using FluentAssertions;

namespace XmlGen.Tests.Parsing;

/// <summary>
/// Tests for EffectiveAccessRestrictions, EffectiveRightsStatement, and EffectiveRecordInfo —
/// the inherited/computed values that walk up the physical and logical structure trees.
///
/// NOT YET IMPLEMENTED: These tests are written test-first and will fail until the inheritance
/// computation is added to the parser. Each test documents the specific rule being tested.
///
/// Inheritance rules (from comments in XmlGen.Tests.Experimental.Parsing):
///
///   Access / Rights (physical files and directories):
///     - Use the resource's own explicit value if present.
///     - Otherwise walk up the physical directory tree until a parent has the value.
///     - Logical structure does not affect access or rights for physical files.
///
///   RecordInfo (physical files):
///     - Use the resource's own explicit value if present.
///     - Otherwise, if the file is referenced as a WHOLE-FILE fptr (no mets:area) in exactly
///       one logical range, inherit that range's RecordInfo.
///     - If referenced only via mets:area (time segment or image region), or in multiple ranges,
///       fall back to the physical tree.
///     - Otherwise walk up the physical directory tree.
///
///   Logical ranges (EffectiveAccessRestrictions / EffectiveRightsStatement):
///     - Use the range's own explicit value if present.
///     - Otherwise inherit only from the physical structMap root div (DMDID="DMD_PHYS_ROOT").
///     - They do NOT inherit from the objects/ directory or other physical divs.
/// </summary>
public class EffectiveMetadataInheritanceTests : MetsParserTestBase
{
    // ─────────────────────────────────────────────────────────────────────────
    // PHYSICAL ACCESS / RIGHTS INHERITANCE
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void File_with_no_explicit_access_inherits_EffectiveAccessRestrictions_from_physical_parent_directory()
    {
        // objects/ has "Level1". File div has no DMDID. File should inherit.
        var xml = """
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                       xmlns:xlink="http://www.w3.org/1999/xlink"
                       xmlns:premis="http://www.loc.gov/premis/v3"
                       xmlns:mods="http://www.loc.gov/mods/v3">
              <mets:dmdSec ID="DMD_objects">
                <mets:mdWrap MDTYPE="MODS">
                  <mets:xmlData>
                    <mods:mods>
                      <mods:accessCondition type="restriction on access">Level1</mods:accessCondition>
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
              <mets:amdSec ID="ADM_objects/tape.wav">
                <mets:techMD ID="TECH_objects/tape.wav">
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
                  <mets:file ID="FILE_tape" MIMETYPE="audio/x-wav">
                    <mets:FLocat xlink:href="objects/tape.wav"/>
                  </mets:file>
                </mets:fileGrp>
              </mets:fileSec>
              <mets:structMap TYPE="PHYSICAL">
                <mets:div ID="PHYS_objects" TYPE="Directory" LABEL="objects"
                          DMDID="DMD_objects" ADMID="ADM_objects">
                  <mets:div ID="PHYS_objects/tape.wav" TYPE="Item">
                    <mets:fptr FILEID="FILE_tape"/>
                  </mets:div>
                </mets:div>
              </mets:structMap>
            </mets:mets>
            """;

        var mets = Parse(xml);

        var file = mets.Files.Single(f => f.LocalPath == "objects/tape.wav");

        // No explicit access on the file itself
        file.AccessRestrictions.Should().BeNull();

        // Inherits from physical objects/ directory
        file.EffectiveAccessRestrictions.Should().HaveCount(1);
        file.EffectiveAccessRestrictions[0].Should().Be("Level1");
    }

    [Fact]
    public void File_with_no_explicit_rights_inherits_EffectiveRightsStatement_from_physical_parent_directory()
    {
        var inCopyright = new Uri("http://rightsstatements.org/vocab/InC/1.0/");

        var xml = """
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                       xmlns:xlink="http://www.w3.org/1999/xlink"
                       xmlns:premis="http://www.loc.gov/premis/v3"
                       xmlns:mods="http://www.loc.gov/mods/v3">
              <mets:dmdSec ID="DMD_objects">
                <mets:mdWrap MDTYPE="MODS">
                  <mets:xmlData>
                    <mods:mods>
                      <mods:accessCondition type="use and reproduction">http://rightsstatements.org/vocab/InC/1.0/</mods:accessCondition>
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
              <mets:amdSec ID="ADM_objects/doc.pdf">
                <mets:techMD ID="TECH_objects/doc.pdf">
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
                  <mets:file ID="FILE_doc" MIMETYPE="application/pdf">
                    <mets:FLocat xlink:href="objects/doc.pdf"/>
                  </mets:file>
                </mets:fileGrp>
              </mets:fileSec>
              <mets:structMap TYPE="PHYSICAL">
                <mets:div ID="PHYS_objects" TYPE="Directory" LABEL="objects"
                          DMDID="DMD_objects" ADMID="ADM_objects">
                  <mets:div ID="PHYS_objects/doc.pdf" TYPE="Item">
                    <mets:fptr FILEID="FILE_doc"/>
                  </mets:div>
                </mets:div>
              </mets:structMap>
            </mets:mets>
            """;

        var mets = Parse(xml);

        var file = mets.Files.Single(f => f.LocalPath == "objects/doc.pdf");

        file.RightsStatement.Should().BeNull();
        file.EffectiveRightsStatement.Should().Be(inCopyright);
    }

    [Fact]
    public void File_with_explicit_access_overrides_physical_parent_and_uses_own_value()
    {
        // The "angela-eagle.m4a" pattern: file has its own DMD with "Closed",
        // while parent objects/ has "Level1". File's effective must be "Closed".
        var xml = """
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                       xmlns:xlink="http://www.w3.org/1999/xlink"
                       xmlns:premis="http://www.loc.gov/premis/v3"
                       xmlns:mods="http://www.loc.gov/mods/v3">
              <mets:dmdSec ID="DMD_objects">
                <mets:mdWrap MDTYPE="MODS">
                  <mets:xmlData>
                    <mods:mods>
                      <mods:accessCondition type="restriction on access">Level1</mods:accessCondition>
                    </mods:mods>
                  </mets:xmlData>
                </mets:mdWrap>
              </mets:dmdSec>
              <mets:dmdSec ID="DMD_objects/restricted-file.wav">
                <mets:mdWrap MDTYPE="MODS">
                  <mets:xmlData>
                    <mods:mods>
                      <mods:accessCondition type="restriction on access">Closed</mods:accessCondition>
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
              <mets:amdSec ID="ADM_objects/restricted-file.wav">
                <mets:techMD ID="TECH_objects/restricted-file.wav">
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
                  <mets:file ID="FILE_restricted" MIMETYPE="audio/x-wav">
                    <mets:FLocat xlink:href="objects/restricted-file.wav"/>
                  </mets:file>
                </mets:fileGrp>
              </mets:fileSec>
              <mets:structMap TYPE="PHYSICAL">
                <mets:div ID="PHYS_objects" TYPE="Directory" LABEL="objects"
                          DMDID="DMD_objects" ADMID="ADM_objects">
                  <mets:div ID="PHYS_objects/restricted-file.wav" TYPE="Item"
                            DMDID="DMD_objects/restricted-file.wav">
                    <mets:fptr FILEID="FILE_restricted"/>
                  </mets:div>
                </mets:div>
              </mets:structMap>
            </mets:mets>
            """;

        var mets = Parse(xml);

        var file = mets.Files.Single(f => f.LocalPath == "objects/restricted-file.wav");

        // File has explicit "Closed" — this takes precedence over parent's "Level1"
        file.AccessRestrictions.Should().ContainSingle().Which.Should().Be("Closed");
        file.EffectiveAccessRestrictions.Should().ContainSingle().Which.Should().Be("Closed");
    }

    [Fact]
    public void Directory_EffectiveAccessRestrictions_equals_its_own_direct_value()
    {
        // When a directory has an explicit access restriction, effective == direct.
        var xml = """
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                       xmlns:xlink="http://www.w3.org/1999/xlink"
                       xmlns:premis="http://www.loc.gov/premis/v3"
                       xmlns:mods="http://www.loc.gov/mods/v3">
              <mets:dmdSec ID="DMD_objects">
                <mets:mdWrap MDTYPE="MODS">
                  <mets:xmlData>
                    <mods:mods>
                      <mods:accessCondition type="restriction on access">Level1</mods:accessCondition>
                      <mods:accessCondition type="use and reproduction">http://rightsstatements.org/vocab/InC/1.0/</mods:accessCondition>
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
              <mets:amdSec ID="ADM_objects/x.txt">
                <mets:techMD ID="TECH_objects/x.txt">
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
                    <mets:FLocat xlink:href="objects/x.txt"/>
                  </mets:file>
                </mets:fileGrp>
              </mets:fileSec>
              <mets:structMap TYPE="PHYSICAL">
                <mets:div ID="PHYS_objects" TYPE="Directory" LABEL="objects"
                          DMDID="DMD_objects" ADMID="ADM_objects">
                  <mets:div ADMID="ADM_objects/x.txt">
                    <mets:fptr FILEID="FILE_1"/>
                  </mets:div>
                </mets:div>
              </mets:structMap>
            </mets:mets>
            """;

        var mets = Parse(xml);

        var objects = mets.PhysicalStructure!.Directories[0];
        objects.AccessRestrictions.Should().ContainSingle().Which.Should().Be("Level1");
        objects.EffectiveAccessRestrictions.Should().ContainSingle().Which.Should().Be("Level1");
        objects.EffectiveRightsStatement.Should().Be(new Uri("http://rightsstatements.org/vocab/InC/1.0/"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RECORD INFO INHERITANCE: PHYSICAL FALLBACK
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void File_with_no_record_info_and_no_logical_map_inherits_EffectiveRecordInfo_from_physical_parent()
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
                      <mods:recordInfo>
                        <mods:recordIdentifier source="identity-service">coll001</mods:recordIdentifier>
                        <mods:recordIdentifier source="EMu">COLL/001</mods:recordIdentifier>
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
              <mets:amdSec ID="ADM_objects/file.wav">
                <mets:techMD ID="TECH_objects/file.wav">
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
                  <mets:file ID="FILE_wav" MIMETYPE="audio/x-wav">
                    <mets:FLocat xlink:href="objects/file.wav"/>
                  </mets:file>
                </mets:fileGrp>
              </mets:fileSec>
              <mets:structMap TYPE="PHYSICAL">
                <mets:div ID="PHYS_objects" TYPE="Directory" LABEL="objects"
                          DMDID="DMD_objects" ADMID="ADM_objects">
                  <mets:div ID="PHYS_objects/file.wav" TYPE="Item">
                    <mets:fptr FILEID="FILE_wav"/>
                  </mets:div>
                </mets:div>
              </mets:structMap>
            </mets:mets>
            """;

        var mets = Parse(xml);

        var file = mets.Files.Single(f => f.LocalPath == "objects/file.wav");
        file.RecordInfo.Should().BeNull();

        file.EffectiveRecordInfo.Should().NotBeNull();
        file.EffectiveRecordInfo!.RecordIdentifiers[0].Source.Should().Be("identity-service");
        file.EffectiveRecordInfo.RecordIdentifiers[0].Value.Should().Be("coll001");
        file.EffectiveRecordInfo.RecordIdentifiers[1].Source.Should().Be("EMu");
        file.EffectiveRecordInfo.RecordIdentifiers[1].Value.Should().Be("COLL/001");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RECORD INFO INHERITANCE: LOGICAL STRUCTURE
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void File_referenced_as_whole_file_fptr_in_logical_range_inherits_EffectiveRecordInfo_from_that_range()
    {
        // The "Women of Westminster" pattern: amber-rudd.m4a and amber-rudd.docx are
        // referenced as whole-file fptrs in the Amber Rudd logical range, so they inherit
        // that range's RecordInfo (MS 2249/1), not the objects/ RecordInfo (MS 2249).
        var xml = """
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                       xmlns:xlink="http://www.w3.org/1999/xlink"
                       xmlns:premis="http://www.loc.gov/premis/v3"
                       xmlns:mods="http://www.loc.gov/mods/v3">
              <mets:dmdSec ID="DMD_PHYS_ROOT">
                <mets:mdWrap MDTYPE="MODS">
                  <mets:xmlData>
                    <mods:mods>
                      <mods:titleInfo><mods:title>Interviews</mods:title></mods:titleInfo>
                    </mods:mods>
                  </mets:xmlData>
                </mets:mdWrap>
              </mets:dmdSec>
              <mets:dmdSec ID="DMD_objects">
                <mets:mdWrap MDTYPE="MODS">
                  <mets:xmlData>
                    <mods:mods>
                      <mods:accessCondition type="restriction on access">Level1</mods:accessCondition>
                      <mods:accessCondition type="use and reproduction">http://rightsstatements.org/vocab/InC/1.0/</mods:accessCondition>
                      <mods:recordInfo>
                        <mods:recordIdentifier source="identity-service">coll-pid</mods:recordIdentifier>
                        <mods:recordIdentifier source="EMu">MS 9999</mods:recordIdentifier>
                      </mods:recordInfo>
                    </mods:mods>
                  </mets:xmlData>
                </mets:mdWrap>
              </mets:dmdSec>
              <mets:dmdSec ID="DMD_LOG_0001">
                <mets:mdWrap MDTYPE="MODS">
                  <mets:xmlData>
                    <mods:mods>
                      <mods:titleInfo><mods:title>Interview with Alice</mods:title></mods:titleInfo>
                      <mods:recordInfo>
                        <mods:recordIdentifier source="identity-service">item-pid</mods:recordIdentifier>
                        <mods:recordIdentifier source="EMu">MS 9999/1</mods:recordIdentifier>
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
              <mets:amdSec ID="ADM_objects/interview.m4a">
                <mets:techMD ID="TECH_objects/interview.m4a">
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
                  <mets:file ID="FILE_objects/interview.m4a" MIMETYPE="audio/m4a">
                    <mets:FLocat xlink:href="objects/interview.m4a"/>
                  </mets:file>
                </mets:fileGrp>
              </mets:fileSec>
              <mets:structMap TYPE="PHYSICAL">
                <mets:div ID="PHYS_ROOT" TYPE="Directory" LABEL="__ROOT" DMDID="DMD_PHYS_ROOT">
                  <mets:div ID="PHYS_objects" TYPE="Directory" LABEL="objects"
                            DMDID="DMD_objects" ADMID="ADM_objects">
                    <mets:div ID="PHYS_objects/interview.m4a" TYPE="Item">
                      <mets:fptr FILEID="FILE_objects/interview.m4a"/>
                    </mets:div>
                  </mets:div>
                </mets:div>
              </mets:structMap>
              <mets:structMap TYPE="LOGICAL">
                <mets:div TYPE="Collection" LABEL="Interviews">
                  <mets:div ID="LOG_0001" TYPE="Item" LABEL="Alice" DMDID="DMD_LOG_0001">
                    <mets:fptr FILEID="FILE_objects/interview.m4a"/>
                  </mets:div>
                </mets:div>
              </mets:structMap>
            </mets:mets>
            """;

        var mets = Parse(xml);

        var file = mets.Files.Single(f => f.LocalPath == "objects/interview.m4a");
        file.RecordInfo.Should().BeNull();

        // File is a whole-file fptr in LOG_0001, so inherits LOG_0001's RecordInfo
        file.EffectiveRecordInfo.Should().NotBeNull();
        file.EffectiveRecordInfo!.RecordIdentifiers[0].Source.Should().Be("identity-service");
        file.EffectiveRecordInfo.RecordIdentifiers[0].Value.Should().Be("item-pid");
        file.EffectiveRecordInfo.RecordIdentifiers[1].Source.Should().Be("EMu");
        file.EffectiveRecordInfo.RecordIdentifiers[1].Value.Should().Be("MS 9999/1");

        // Access and rights still come from physical objects/, not from the logical range
        file.EffectiveAccessRestrictions.Should().ContainSingle().Which.Should().Be("Level1");
        file.EffectiveRightsStatement.Should().Be(new Uri("http://rightsstatements.org/vocab/InC/1.0/"));
    }

    [Fact]
    public void File_not_referenced_in_logical_map_inherits_EffectiveRecordInfo_from_physical_parent()
    {
        // The "angela-eagle.m4a" pattern: this file is absent from the logical structMap,
        // so it cannot inherit RecordInfo from a range. It falls back to objects/.
        var xml = """
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                       xmlns:xlink="http://www.w3.org/1999/xlink"
                       xmlns:premis="http://www.loc.gov/premis/v3"
                       xmlns:mods="http://www.loc.gov/mods/v3">
              <mets:dmdSec ID="DMD_objects">
                <mets:mdWrap MDTYPE="MODS">
                  <mets:xmlData>
                    <mods:mods>
                      <mods:recordInfo>
                        <mods:recordIdentifier source="EMu">MS 9999</mods:recordIdentifier>
                      </mods:recordInfo>
                    </mods:mods>
                  </mets:xmlData>
                </mets:mdWrap>
              </mets:dmdSec>
              <mets:dmdSec ID="DMD_LOG_0001">
                <mets:mdWrap MDTYPE="MODS">
                  <mets:xmlData>
                    <mods:mods>
                      <mods:recordInfo>
                        <mods:recordIdentifier source="EMu">MS 9999/1</mods:recordIdentifier>
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
              <mets:amdSec ID="ADM_objects/in-range.m4a">
                <mets:techMD ID="TECH_objects/in-range.m4a">
                  <mets:mdWrap MDTYPE="PREMIS:OBJECT">
                    <mets:xmlData>
                      <premis:object>
                        <premis:objectCharacteristics>
                          <premis:fixity>
                            <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                            <premis:messageDigest>111</premis:messageDigest>
                          </premis:fixity>
                        </premis:objectCharacteristics>
                      </premis:object>
                    </mets:xmlData>
                  </mets:mdWrap>
                </mets:techMD>
              </mets:amdSec>
              <mets:amdSec ID="ADM_objects/not-in-range.m4a">
                <mets:techMD ID="TECH_objects/not-in-range.m4a">
                  <mets:mdWrap MDTYPE="PREMIS:OBJECT">
                    <mets:xmlData>
                      <premis:object>
                        <premis:objectCharacteristics>
                          <premis:fixity>
                            <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                            <premis:messageDigest>222</premis:messageDigest>
                          </premis:fixity>
                        </premis:objectCharacteristics>
                      </premis:object>
                    </mets:xmlData>
                  </mets:mdWrap>
                </mets:techMD>
              </mets:amdSec>
              <mets:fileSec>
                <mets:fileGrp>
                  <mets:file ID="FILE_in_range" MIMETYPE="audio/m4a">
                    <mets:FLocat xlink:href="objects/in-range.m4a"/>
                  </mets:file>
                  <mets:file ID="FILE_not_in_range" MIMETYPE="audio/m4a">
                    <mets:FLocat xlink:href="objects/not-in-range.m4a"/>
                  </mets:file>
                </mets:fileGrp>
              </mets:fileSec>
              <mets:structMap TYPE="PHYSICAL">
                <mets:div ID="PHYS_objects" TYPE="Directory" LABEL="objects"
                          DMDID="DMD_objects" ADMID="ADM_objects">
                  <mets:div ID="PHYS_objects/in-range.m4a" TYPE="Item">
                    <mets:fptr FILEID="FILE_in_range"/>
                  </mets:div>
                  <mets:div ID="PHYS_objects/not-in-range.m4a" TYPE="Item">
                    <mets:fptr FILEID="FILE_not_in_range"/>
                  </mets:div>
                </mets:div>
              </mets:structMap>
              <mets:structMap TYPE="LOGICAL">
                <mets:div TYPE="Collection" LABEL="Collection">
                  <mets:div TYPE="Item" DMDID="DMD_LOG_0001">
                    <mets:fptr FILEID="FILE_in_range"/>
                    <!-- FILE_not_in_range is deliberately absent from the logical map -->
                  </mets:div>
                </mets:div>
              </mets:structMap>
            </mets:mets>
            """;

        var mets = Parse(xml);

        var inRange = mets.Files.Single(f => f.LocalPath == "objects/in-range.m4a");
        var notInRange = mets.Files.Single(f => f.LocalPath == "objects/not-in-range.m4a");

        // In-range file inherits from the logical range
        inRange.EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("MS 9999/1");

        // Not-in-range file falls back to physical objects/
        notInRange.EffectiveRecordInfo.Should().NotBeNull();
        notInRange.EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("MS 9999");
    }

    [Fact]
    public void File_referenced_only_as_time_area_falls_back_to_physical_EffectiveRecordInfo()
    {
        // The "Liddle tapes" pattern: each WAV file spans multiple interviews, so no file
        // belongs to a single logical range. All files are referenced via mets:area with
        // BEGIN/END time markers rather than whole-file fptrs. They therefore cannot inherit
        // RecordInfo from any range and fall back to the physical objects/ RecordInfo.
        var xml = """
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                       xmlns:xlink="http://www.w3.org/1999/xlink"
                       xmlns:premis="http://www.loc.gov/premis/v3"
                       xmlns:mods="http://www.loc.gov/mods/v3">
              <mets:dmdSec ID="DMD_PHYS_ROOT">
                <mets:mdWrap MDTYPE="MODS">
                  <mets:xmlData>
                    <mods:mods>
                      <mods:titleInfo><mods:title>Tape Collection</mods:title></mods:titleInfo>
                    </mods:mods>
                  </mets:xmlData>
                </mets:mdWrap>
              </mets:dmdSec>
              <mets:dmdSec ID="DMD_objects">
                <mets:mdWrap MDTYPE="MODS">
                  <mets:xmlData>
                    <mods:mods>
                      <mods:accessCondition type="restriction on access">Level1</mods:accessCondition>
                      <mods:accessCondition type="use and reproduction">http://rightsstatements.org/vocab/InC/1.0/</mods:accessCondition>
                      <mods:recordInfo>
                        <mods:recordIdentifier source="identity-service">tapes-pid</mods:recordIdentifier>
                        <mods:recordIdentifier source="EMu">TAPES/1-2</mods:recordIdentifier>
                      </mods:recordInfo>
                    </mods:mods>
                  </mets:xmlData>
                </mets:mdWrap>
              </mets:dmdSec>
              <mets:dmdSec ID="DMD_LOG_INTERVIEW1">
                <mets:mdWrap MDTYPE="MODS">
                  <mets:xmlData>
                    <mods:mods>
                      <mods:titleInfo><mods:title>SMITH, JOHN</mods:title></mods:titleInfo>
                      <mods:recordInfo>
                        <mods:recordIdentifier source="EMu">INTERVIEW/001</mods:recordIdentifier>
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
              <mets:amdSec ID="ADM_objects/tape.wav">
                <mets:techMD ID="TECH_objects/tape.wav">
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
                  <mets:file ID="FILE_objects/tape.wav" MIMETYPE="audio/x-wav">
                    <mets:FLocat xlink:href="objects/tape.wav"/>
                  </mets:file>
                </mets:fileGrp>
              </mets:fileSec>
              <mets:structMap TYPE="PHYSICAL">
                <mets:div ID="PHYS_ROOT" TYPE="Directory" LABEL="__ROOT" DMDID="DMD_PHYS_ROOT">
                  <mets:div ID="PHYS_objects" TYPE="Directory" LABEL="objects"
                            DMDID="DMD_objects" ADMID="ADM_objects">
                    <mets:div ID="PHYS_objects/tape.wav" TYPE="Item">
                      <mets:fptr FILEID="FILE_objects/tape.wav"/>
                    </mets:div>
                  </mets:div>
                </mets:div>
              </mets:structMap>
              <mets:structMap TYPE="LOGICAL">
                <mets:div TYPE="Collection" LABEL="Tapes">
                  <mets:div TYPE="Item" DMDID="DMD_LOG_INTERVIEW1">
                    <!-- Area reference (time segment), not a whole-file fptr -->
                    <mets:fptr>
                      <mets:area FILEID="FILE_objects/tape.wav" BETYPE="TIME"
                                 BEGIN="00:00:15" END="00:35:09"/>
                    </mets:fptr>
                  </mets:div>
                </mets:div>
              </mets:structMap>
            </mets:mets>
            """;

        var mets = Parse(xml);

        var tape = mets.Files.Single(f => f.LocalPath == "objects/tape.wav");
        tape.RecordInfo.Should().BeNull();

        // Area reference: the tape spans multiple interviews, so it belongs to no single
        // interview range. RecordInfo falls back to the physical objects/ directory.
        tape.EffectiveRecordInfo.Should().NotBeNull();
        tape.EffectiveRecordInfo!.RecordIdentifiers[0].Source.Should().Be("identity-service");
        tape.EffectiveRecordInfo.RecordIdentifiers[0].Value.Should().Be("tapes-pid");
        tape.EffectiveRecordInfo.RecordIdentifiers[1].Source.Should().Be("EMu");
        tape.EffectiveRecordInfo.RecordIdentifiers[1].Value.Should().Be("TAPES/1-2");

        // Access and rights inherited from physical objects/ regardless
        tape.EffectiveAccessRestrictions.Should().ContainSingle().Which.Should().Be("Level1");
        tape.EffectiveRightsStatement.Should().Be(new Uri("http://rightsstatements.org/vocab/InC/1.0/"));
    }

    [Fact]
    public void File_referenced_as_image_region_falls_back_to_physical_EffectiveRecordInfo()
    {
        // The "Response Book page 5" pattern: page 5 is referenced via COORDS/SHAPE area
        // by two different logical ranges (Part 2 and Part 3). Because no single range
        // exclusively owns it, RecordInfo falls back to the physical objects/ directory.
        var xml = """
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                       xmlns:xlink="http://www.w3.org/1999/xlink"
                       xmlns:premis="http://www.loc.gov/premis/v3"
                       xmlns:mods="http://www.loc.gov/mods/v3">
              <mets:dmdSec ID="DMD_objects">
                <mets:mdWrap MDTYPE="MODS">
                  <mets:xmlData>
                    <mods:mods>
                      <mods:recordInfo>
                        <mods:recordIdentifier source="identity-service">item-pid</mods:recordIdentifier>
                        <mods:recordIdentifier source="EMu">PRI/2/999</mods:recordIdentifier>
                      </mods:recordInfo>
                    </mods:mods>
                  </mets:xmlData>
                </mets:mdWrap>
              </mets:dmdSec>
              <mets:dmdSec ID="DMD_part2">
                <mets:mdWrap MDTYPE="MODS">
                  <mets:xmlData>
                    <mods:mods>
                      <mods:recordInfo>
                        <mods:recordIdentifier source="EMu">PRI/2/999/b</mods:recordIdentifier>
                      </mods:recordInfo>
                    </mods:mods>
                  </mets:xmlData>
                </mets:mdWrap>
              </mets:dmdSec>
              <mets:dmdSec ID="DMD_part3">
                <mets:mdWrap MDTYPE="MODS">
                  <mets:xmlData>
                    <mods:mods>
                      <mods:recordInfo>
                        <mods:recordIdentifier source="EMu">PRI/2/999/c</mods:recordIdentifier>
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
              <mets:amdSec ID="ADM_objects/page5.tif">
                <mets:techMD ID="TECH_objects/page5.tif">
                  <mets:mdWrap MDTYPE="PREMIS:OBJECT">
                    <mets:xmlData>
                      <premis:object>
                        <premis:objectCharacteristics>
                          <premis:fixity>
                            <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                            <premis:messageDigest>p5digest</premis:messageDigest>
                          </premis:fixity>
                        </premis:objectCharacteristics>
                      </premis:object>
                    </mets:xmlData>
                  </mets:mdWrap>
                </mets:techMD>
              </mets:amdSec>
              <mets:fileSec>
                <mets:fileGrp>
                  <mets:file ID="FILE_page5" MIMETYPE="image/tiff">
                    <mets:FLocat xlink:href="objects/page5.tif"/>
                  </mets:file>
                </mets:fileGrp>
              </mets:fileSec>
              <mets:structMap TYPE="PHYSICAL">
                <mets:div ID="PHYS_objects" TYPE="Directory" LABEL="objects"
                          DMDID="DMD_objects" ADMID="ADM_objects">
                  <mets:div ID="PHYS_objects/page5.tif" TYPE="Item">
                    <mets:fptr FILEID="FILE_page5"/>
                  </mets:div>
                </mets:div>
              </mets:structMap>
              <mets:structMap TYPE="LOGICAL">
                <mets:div TYPE="Collection" LABEL="Response Book">
                  <!-- Part 2: references top half of page5 via image region -->
                  <mets:div TYPE="Part" LABEL="Part 2" DMDID="DMD_part2">
                    <mets:fptr FILEID="FILE_page5">
                      <mets:area FILEID="FILE_page5" COORDS="0,0,6000,2000" SHAPE="RECT"/>
                    </mets:fptr>
                  </mets:div>
                  <!-- Part 3: references bottom half of page5 via image region -->
                  <mets:div TYPE="Part" LABEL="Part 3" DMDID="DMD_part3">
                    <mets:fptr FILEID="FILE_page5">
                      <mets:area FILEID="FILE_page5" COORDS="0,2000,6000,4000" SHAPE="RECT"/>
                    </mets:fptr>
                  </mets:div>
                </mets:div>
              </mets:structMap>
            </mets:mets>
            """;

        var mets = Parse(xml);

        var page5 = mets.Files.Single(f => f.LocalPath == "objects/page5.tif");
        page5.RecordInfo.Should().BeNull();

        // Referenced via area (not whole-file) by two ranges — falls back to physical objects/
        page5.EffectiveRecordInfo.Should().NotBeNull();
        page5.EffectiveRecordInfo!.RecordIdentifiers[0].Source.Should().Be("identity-service");
        page5.EffectiveRecordInfo.RecordIdentifiers[0].Value.Should().Be("item-pid");
        page5.EffectiveRecordInfo.RecordIdentifiers[1].Source.Should().Be("EMu");
        page5.EffectiveRecordInfo.RecordIdentifiers[1].Value.Should().Be("PRI/2/999");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // LOGICAL RANGE EFFECTIVE VALUES
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Logical_range_EffectiveAccessRestrictions_is_empty_when_DMD_PHYS_ROOT_has_no_access()
    {
        // From WoW and Liddle: logical ranges have no explicit access, and DMD_PHYS_ROOT has
        // only a title (no access conditions). Therefore EffectiveAccessRestrictions = [].
        // Note: the objects/ access restriction ("Level1") does NOT propagate to logical ranges;
        // only DMD_PHYS_ROOT can supply access/rights to logical ranges.
        var xml = """
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                       xmlns:xlink="http://www.w3.org/1999/xlink"
                       xmlns:premis="http://www.loc.gov/premis/v3"
                       xmlns:mods="http://www.loc.gov/mods/v3">
              <mets:dmdSec ID="DMD_PHYS_ROOT">
                <mets:mdWrap MDTYPE="MODS">
                  <mets:xmlData>
                    <mods:mods>
                      <mods:titleInfo><mods:title>My Collection</mods:title></mods:titleInfo>
                      <!-- No access condition here -->
                    </mods:mods>
                  </mets:xmlData>
                </mets:mdWrap>
              </mets:dmdSec>
              <mets:dmdSec ID="DMD_objects">
                <mets:mdWrap MDTYPE="MODS">
                  <mets:xmlData>
                    <mods:mods>
                      <mods:accessCondition type="restriction on access">Level1</mods:accessCondition>
                      <mods:recordInfo>
                        <mods:recordIdentifier source="EMu">COLL/001</mods:recordIdentifier>
                      </mods:recordInfo>
                    </mods:mods>
                  </mets:xmlData>
                </mets:mdWrap>
              </mets:dmdSec>
              <mets:dmdSec ID="DMD_LOG_0001">
                <mets:mdWrap MDTYPE="MODS">
                  <mets:xmlData>
                    <mods:mods>
                      <mods:titleInfo><mods:title>Interview A</mods:title></mods:titleInfo>
                      <mods:recordInfo>
                        <mods:recordIdentifier source="EMu">ITEM/001</mods:recordIdentifier>
                      </mods:recordInfo>
                      <!-- No explicit access condition on the range -->
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
              <mets:amdSec ID="ADM_objects/tape.wav">
                <mets:techMD ID="TECH_objects/tape.wav">
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
                  <mets:file ID="FILE_tape" MIMETYPE="audio/x-wav">
                    <mets:FLocat xlink:href="objects/tape.wav"/>
                  </mets:file>
                </mets:fileGrp>
              </mets:fileSec>
              <mets:structMap TYPE="PHYSICAL">
                <mets:div ID="PHYS_ROOT" TYPE="Directory" LABEL="__ROOT" DMDID="DMD_PHYS_ROOT">
                  <mets:div ID="PHYS_objects" TYPE="Directory" LABEL="objects"
                            DMDID="DMD_objects" ADMID="ADM_objects">
                    <mets:div ID="PHYS_objects/tape.wav" TYPE="Item">
                      <mets:fptr FILEID="FILE_tape"/>
                    </mets:div>
                  </mets:div>
                </mets:div>
              </mets:structMap>
              <mets:structMap TYPE="LOGICAL">
                <mets:div TYPE="Collection" LABEL="Collection">
                  <mets:div ID="LOG_0001" TYPE="Item" LABEL="Interview A" DMDID="DMD_LOG_0001">
                    <mets:fptr>
                      <mets:area FILEID="FILE_tape" BETYPE="TIME" BEGIN="00:00:15" END="00:20:00"/>
                    </mets:fptr>
                  </mets:div>
                </mets:div>
              </mets:structMap>
            </mets:mets>
            """;

        var mets = Parse(xml);

        var range = mets.LogicalStructures[0].Ranges[0];
        range.AccessRestrictions.Should().BeNull();
        range.RightsStatement.Should().BeNull();

        // DMD_PHYS_ROOT has no access — logical range effective access/rights are empty/null
        range.EffectiveAccessRestrictions.Should().BeEmpty();
        range.EffectiveRightsStatement.Should().BeNull();

        // RecordInfo is explicitly on the range, so effective == direct
        range.EffectiveRecordInfo.Should().NotBeNull();
        range.EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("ITEM/001");
    }

    [Fact]
    public void Logical_range_inherits_EffectiveAccessRestrictions_from_DMD_PHYS_ROOT_when_present()
    {
        // If the physical structMap root div (DMDID="DMD_PHYS_ROOT") declares an access
        // condition, logical ranges without their own access should inherit it.
        var xml = """
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                       xmlns:xlink="http://www.w3.org/1999/xlink"
                       xmlns:premis="http://www.loc.gov/premis/v3"
                       xmlns:mods="http://www.loc.gov/mods/v3">
              <mets:dmdSec ID="DMD_PHYS_ROOT">
                <mets:mdWrap MDTYPE="MODS">
                  <mets:xmlData>
                    <mods:mods>
                      <mods:accessCondition type="restriction on access">ClosedCollection</mods:accessCondition>
                    </mods:mods>
                  </mets:xmlData>
                </mets:mdWrap>
              </mets:dmdSec>
              <mets:dmdSec ID="DMD_LOG_0001">
                <mets:mdWrap MDTYPE="MODS">
                  <mets:xmlData>
                    <mods:mods>
                      <mods:titleInfo><mods:title>Item A</mods:title></mods:titleInfo>
                      <mods:recordInfo>
                        <mods:recordIdentifier source="EMu">ITEM/A</mods:recordIdentifier>
                      </mods:recordInfo>
                    </mods:mods>
                  </mets:xmlData>
                </mets:mdWrap>
              </mets:dmdSec>
              <mets:amdSec ID="ADM_objects/f.txt">
                <mets:techMD ID="TECH_objects/f.txt">
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
                  <mets:file ID="FILE_f" MIMETYPE="text/plain">
                    <mets:FLocat xlink:href="objects/f.txt"/>
                  </mets:file>
                </mets:fileGrp>
              </mets:fileSec>
              <mets:structMap TYPE="PHYSICAL">
                <mets:div ID="PHYS_ROOT" TYPE="Directory" LABEL="__ROOT" DMDID="DMD_PHYS_ROOT">
                  <mets:div ID="PHYS_objects/f.txt" TYPE="Item">
                    <mets:fptr FILEID="FILE_f"/>
                  </mets:div>
                </mets:div>
              </mets:structMap>
              <mets:structMap TYPE="LOGICAL">
                <mets:div TYPE="Collection" LABEL="Coll">
                  <mets:div ID="LOG_0001" TYPE="Item" DMDID="DMD_LOG_0001">
                    <mets:fptr FILEID="FILE_f"/>
                  </mets:div>
                </mets:div>
              </mets:structMap>
            </mets:mets>
            """;

        var mets = Parse(xml);

        var range = mets.LogicalStructures[0].Ranges[0];
        range.AccessRestrictions.Should().BeNull();

        // Logical range has no explicit access, but DMD_PHYS_ROOT has "ClosedCollection"
        range.EffectiveAccessRestrictions.Should().ContainSingle().Which.Should().Be("ClosedCollection");
    }
}
