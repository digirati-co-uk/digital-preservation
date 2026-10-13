using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Utils;
using FluentAssertions;

namespace XmlGen.Tests.Parsing;

/// <summary>
/// Issue #236: every file's techMD in a recent EPrints migration carries a premis:storage
/// pointing at the EPrints server it was migrated from - historical provenance, not a live
/// location, and it never carries a storageMedium. The parser used to read *any* single
/// premis:storage's contentLocation as the file's (or directory's) storage location, recording
/// that dead path as if it were ours; and it used SingleOrDefault() over every contentLocation in
/// the techMD, which throws once a techMD legitimately holds two premis:storage elements - "theirs
/// plus ours" is exactly what an edited-and-exported EPrints item's METS contains. PremisManager.Read
/// already resolves storage correctly (only the premis:storage whose storageMedium is our own
/// agent name); MetsParser now does the same.
/// </summary>
public class StorageLocationTests : MetsParserTestBase
{
    private const string OurAgent = "University of Leeds Digital Library Infrastructure Project";

    private static string Storage(string? medium, string contentLocationValue) => $"""
        <premis:storage>
          {(medium.HasText() ? $"<premis:storageMedium>{medium}</premis:storageMedium>" : "")}
          <premis:contentLocation>
            <premis:contentLocationType>URL</premis:contentLocationType>
            <premis:contentLocationValue>{contentLocationValue}</premis:contentLocationValue>
          </premis:contentLocation>
        </premis:storage>
        """;

    private static string FullMets(string admId, string localPath, string storageXml) => $"""
        <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                   xmlns:xlink="http://www.w3.org/1999/xlink"
                   xmlns:premis="http://www.loc.gov/premis/v3">
          <mets:amdSec ID="{admId}">
            <mets:techMD ID="TECH_{admId.Substring(4)}">
              <mets:mdWrap MDTYPE="PREMIS:OBJECT">
                <mets:xmlData>
                  <premis:object>
                    <premis:objectCharacteristics>
                      <premis:fixity>
                        <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                        <premis:messageDigest>abc123</premis:messageDigest>
                      </premis:fixity>
                    </premis:objectCharacteristics>
                    {storageXml}
                  </premis:object>
                </mets:xmlData>
              </mets:mdWrap>
            </mets:techMD>
          </mets:amdSec>
          <mets:fileSec>
            <mets:fileGrp>
              <mets:file ID="FILE_1" MIMETYPE="image/tiff">
                <mets:FLocat xlink:href="{localPath}"/>
              </mets:file>
            </mets:fileGrp>
          </mets:fileSec>
          <mets:structMap TYPE="PHYSICAL">
            <mets:div ADMID="{admId}">
              <mets:fptr FILEID="FILE_1"/>
            </mets:div>
          </mets:structMap>
        </mets:mets>
        """;

    [Fact]
    public void A_Foreign_Storage_Assertion_With_No_StorageMedium_Is_Not_Read_As_The_Storage_Location()
    {
        // Every recent EPrints migration: a premis:storage pointing at the dead EPrints server,
        // with no storageMedium at all.
        var xml = FullMets("ADM_objects/img.tif", "objects/img.tif",
            Storage(medium: null, "file:///usr/share/eprints/archives/dl/documents/disk0/00/03/66/75/01/LEEUA_1959.010_01.tif"));

        var mets = Parse(xml);

        var storage = mets.Files[0].Metadata.OfType<StorageMetadata>().Single();
        storage.StorageLocation.Should().BeNull(
            "a foreign system's provenance is not our storage location, however little else is on the techMD");
    }

    [Fact]
    public void A_Storage_Assertion_Whose_Medium_Is_Our_Agent_Is_Read_As_The_Storage_Location()
    {
        var xml = FullMets("ADM_objects/img.tif", "objects/img.tif",
            Storage(OurAgent, "s3://deposits/dep-1/objects/img.tif"));

        var mets = Parse(xml);

        var storage = mets.Files[0].Metadata.OfType<StorageMetadata>().Single();
        storage.StorageLocation.Should().Be(new Uri("s3://deposits/dep-1/objects/img.tif"));
    }

    [Fact]
    public void Two_Storage_Assertions_Resolve_To_Ours_Without_Throwing()
    {
        // The exact shape #223 enabling EPrints editing produces: the platform's export adds its
        // own marked premis:storage alongside the migrated-from one already there. Re-parsing that
        // exported METS must not crash, and must prefer the one that's actually ours.
        var xml = FullMets("ADM_objects/img.tif", "objects/img.tif",
            Storage(medium: null, "file:///usr/share/eprints/archives/dl/documents/disk0/00/03/66/75/01/LEEUA_1959.010_01.tif") +
            Storage(OurAgent, "s3://deposits/dep-1/objects/img.tif"));

        var mets = Parse(xml);

        var storage = mets.Files[0].Metadata.OfType<StorageMetadata>().Single();
        storage.StorageLocation.Should().Be(new Uri("s3://deposits/dep-1/objects/img.tif"));
    }

    [Fact]
    public void A_Directory_With_A_Foreign_Only_Storage_Assertion_Has_No_Storage_Location()
    {
        // The directory-level equivalent of the file-level checks above: MetsParser resolves
        // premis:storage the same way for a directory's own premis:object (via premis:originalName)
        // as it does for a file's.
        var xml = $"""
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                       xmlns:xlink="http://www.w3.org/1999/xlink"
                       xmlns:premis="http://www.loc.gov/premis/v3">
              <mets:amdSec ID="ADM_objects">
                <mets:techMD ID="TECH_objects">
                  <mets:mdWrap MDTYPE="PREMIS:OBJECT">
                    <mets:xmlData>
                      <premis:object>
                        <premis:objectCharacteristics/>
                        <premis:originalName>objects</premis:originalName>
                        {Storage(medium: null, "file:///usr/share/eprints/archives/dl/documents/disk0")}
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
                            <premis:messageDigest>deadbeef</premis:messageDigest>
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
                <mets:div ID="PHYS_objects" TYPE="Directory" LABEL="Objects" ADMID="ADM_objects">
                  <mets:div ID="PHYS_objects/file.txt" ADMID="ADM_objects/file.txt">
                    <mets:fptr FILEID="FILE_1"/>
                  </mets:div>
                </mets:div>
              </mets:structMap>
            </mets:mets>
            """;

        var mets = Parse(xml);

        var dir = mets.PhysicalStructure!.Directories[0];
        var storage = dir.Metadata.OfType<StorageMetadata>().Single();
        storage.StorageLocation.Should().BeNull();
    }
}
