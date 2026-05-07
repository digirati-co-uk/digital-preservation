using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using FluentAssertions;

namespace XmlGen.Tests.Parsing;

/// <summary>
/// Focused tests for file-level metadata extracted from PREMIS within the amdSec:
/// digest, size, file format, EXIF, and virus scan metadata.
/// </summary>
public class FileMetadataTests : MetsParserTestBase
{
    // Minimal reusable inline XML template pieces

    // Wraps significant properties at the correct premis:object level (siblings of objectCharacteristics)
    private static string SigProp(string type, string value) => $"""
        <premis:significantProperties>
          <premis:significantPropertiesType>{type}</premis:significantPropertiesType>
          <premis:significantPropertiesValue>{value}</premis:significantPropertiesValue>
        </premis:significantProperties>
        """;

    private static string FullMetsWithSigProps(string sigPropsXml) => $"""
        <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                   xmlns:xlink="http://www.w3.org/1999/xlink"
                   xmlns:premis="http://www.loc.gov/premis/v3">
          <mets:amdSec ID="ADM_objects/video.mov">
            <mets:techMD ID="TECH_objects/video.mov">
              <mets:mdWrap MDTYPE="PREMIS:OBJECT">
                <mets:xmlData>
                  <premis:object>
                    {sigPropsXml}
                    <premis:objectCharacteristics/>
                  </premis:object>
                </mets:xmlData>
              </mets:mdWrap>
            </mets:techMD>
          </mets:amdSec>
          <mets:fileSec>
            <mets:fileGrp>
              <mets:file ID="FILE_1" MIMETYPE="video/quicktime">
                <mets:FLocat xlink:href="objects/video.mov"/>
              </mets:file>
            </mets:fileGrp>
          </mets:fileSec>
          <mets:structMap TYPE="PHYSICAL">
            <mets:div ADMID="ADM_objects/video.mov">
              <mets:fptr FILEID="FILE_1"/>
            </mets:div>
          </mets:structMap>
        </mets:mets>
        """;

    private static string AmdSecWithPremis(string admId, string premisBody) => $"""
        <mets:amdSec ID="{admId}">
          <mets:techMD ID="TECH_{admId.Substring(4)}">
            <mets:mdWrap MDTYPE="PREMIS:OBJECT">
              <mets:xmlData>
                <premis:object>
                  <premis:objectCharacteristics>
                    {premisBody}
                  </premis:objectCharacteristics>
                </premis:object>
              </mets:xmlData>
            </mets:mdWrap>
          </mets:techMD>
        </mets:amdSec>
        """;

    private static string FullMets(string admId, string localPath, string mimeType, string premisBody,
        string extraAmdSecs = "") => $"""
        <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                   xmlns:xlink="http://www.w3.org/1999/xlink"
                   xmlns:premis="http://www.loc.gov/premis/v3">
          {AmdSecWithPremis(admId, premisBody)}
          {extraAmdSecs}
          <mets:fileSec>
            <mets:fileGrp>
              <mets:file ID="FILE_1" MIMETYPE="{mimeType}">
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
    public void SHA256_digest_is_extracted_from_premis_fixity()
    {
        var xml = FullMets("ADM_objects/file.txt", "objects/file.txt", "text/plain", """
            <premis:fixity>
              <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
              <premis:messageDigest>aabbccdd11223344</premis:messageDigest>
            </premis:fixity>
            """);

        var mets = Parse(xml);

        mets.Files[0].Digest.Should().Be("aabbccdd11223344");
    }

    [Fact]
    public void Non_SHA256_digest_algorithm_is_not_extracted()
    {
        // Only SHA256 is used; MD5 should be ignored
        var xml = FullMets("ADM_objects/file.txt", "objects/file.txt", "text/plain", """
            <premis:fixity>
              <premis:messageDigestAlgorithm>MD5</premis:messageDigestAlgorithm>
              <premis:messageDigest>d41d8cd98f00b204e9800998ecf8427e</premis:messageDigest>
            </premis:fixity>
            """);

        var mets = Parse(xml);

        mets.Files[0].Digest.Should().BeNull();
    }

    [Fact]
    public void File_size_is_extracted_from_premis_size()
    {
        var xml = FullMets("ADM_objects/file.bin", "objects/file.bin", "application/octet-stream", """
            <premis:fixity>
              <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
              <premis:messageDigest>cafebabe</premis:messageDigest>
            </premis:fixity>
            <premis:size>72000000</premis:size>
            """);

        var mets = Parse(xml);

        mets.Files[0].Size.Should().Be(72000000);
    }

    [Fact]
    public void PRONOM_key_and_format_name_are_extracted_into_FileFormatMetadata()
    {
        var xml = FullMets("ADM_objects/img.tif", "objects/img.tif", "image/tiff", """
            <premis:fixity>
              <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
              <premis:messageDigest>tiffdigest</premis:messageDigest>
            </premis:fixity>
            <premis:size>48000000</premis:size>
            <premis:format>
              <premis:formatDesignation>
                <premis:formatName>Tagged Image File Format</premis:formatName>
              </premis:formatDesignation>
              <premis:formatRegistry>
                <premis:formatRegistryKey>fmt/10</premis:formatRegistryKey>
              </premis:formatRegistry>
            </premis:format>
            """);

        var mets = Parse(xml);

        var fmt = mets.Files[0].Metadata.OfType<FileFormatMetadata>().Single();
        fmt.PronomKey.Should().Be("fmt/10");
        fmt.FormatName.Should().Be("Tagged Image File Format");
        fmt.ContentType.Should().Be("image/tiff");
        fmt.Digest.Should().Be("tiffdigest");
        fmt.Size.Should().Be(48000000);
    }

    [Fact]
    public void EXIF_metadata_tags_are_extracted_from_objectCharacteristicsExtension()
    {
        var xml = FullMets("ADM_objects/photo.jpg", "objects/photo.jpg", "image/jpeg", """
            <premis:fixity>
              <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
              <premis:messageDigest>jpegdigest</premis:messageDigest>
            </premis:fixity>
            <premis:objectCharacteristicsExtension>
              <ExifMetadata>
                <FileType>JPEG</FileType>
                <MIMEType>image/jpeg</MIMEType>
                <ImageWidth>3000</ImageWidth>
                <ImageHeight>2000</ImageHeight>
              </ExifMetadata>
            </premis:objectCharacteristicsExtension>
            """);

        var mets = Parse(xml);

        var exif = mets.Files[0].Metadata.OfType<ExifMetadata>().Single();
        exif.Tags.Should().HaveCount(4);
        exif.Tags.Should().Contain(t => t.TagName == "FileType" && t.TagValue == "JPEG");
        exif.Tags.Should().Contain(t => t.TagName == "MIMEType" && t.TagValue == "image/jpeg");
        exif.Tags.Should().Contain(t => t.TagName == "ImageWidth" && t.TagValue == "3000");
        exif.Tags.Should().Contain(t => t.TagName == "ImageHeight" && t.TagValue == "2000");
    }

    [Fact]
    public void Virus_scan_pass_event_produces_metadata_with_HasVirus_false()
    {
        var clamavId = "digiprovMD_ClamAV_ADM_objects/safe.docx";
        var extraAmdSecs = $"""
            <mets:amdSec ID="ADM_EXTRA">
              <mets:digiprovMD ID="{clamavId}">
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
            """;

        var xml = FullMets("ADM_objects/safe.docx", "objects/safe.docx", "application/msword", """
            <premis:fixity>
              <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
              <premis:messageDigest>safeFile</premis:messageDigest>
            </premis:fixity>
            """, extraAmdSecs);

        var mets = Parse(xml);

        var virus = mets.Files[0].Metadata.OfType<VirusScanMetadata>().Single();
        virus.HasVirus.Should().BeFalse();
        virus.VirusDefinition.Should().Be("ClamAV 1.4.3/27944");
    }

    [Fact]
    public void Virus_scan_fail_event_produces_metadata_with_HasVirus_true()
    {
        var clamavId = "digiprovMD_ClamAV_ADM_objects/infected.zip";
        var extraAmdSecs = $"""
            <mets:amdSec ID="ADM_EXTRA">
              <mets:digiprovMD ID="{clamavId}">
                <mets:mdWrap MDTYPE="PREMIS:EVENT">
                  <mets:xmlData>
                    <premis:event>
                      <premis:eventDateTime>2026-03-18T12:00:00Z</premis:eventDateTime>
                      <premis:eventDetailInformation>
                        <premis:eventDetail>ClamAV 1.4.3/27944</premis:eventDetail>
                      </premis:eventDetailInformation>
                      <premis:eventOutcomeInformation>
                        <premis:eventOutcome>fail</premis:eventOutcome>
                        <premis:eventOutcomeDetail>
                          <premis:eventOutcomeDetailNote>Eicar.Zip.ExceededRecursion FOUND</premis:eventOutcomeDetailNote>
                        </premis:eventOutcomeDetail>
                      </premis:eventOutcomeInformation>
                    </premis:event>
                  </mets:xmlData>
                </mets:mdWrap>
              </mets:digiprovMD>
            </mets:amdSec>
            """;

        var xml = FullMets("ADM_objects/infected.zip", "objects/infected.zip", "application/zip", """
            <premis:fixity>
              <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
              <premis:messageDigest>badFile</premis:messageDigest>
            </premis:fixity>
            """, extraAmdSecs);

        var mets = Parse(xml);

        var virus = mets.Files[0].Metadata.OfType<VirusScanMetadata>().Single();
        virus.HasVirus.Should().BeTrue();
        virus.VirusFound.Should().Be("Eicar.Zip.ExceededRecursion FOUND");
        virus.VirusDefinition.Should().Be("ClamAV 1.4.3/27944");
    }

    [Fact]
    public void ADMID_on_mets_file_element_is_used_when_div_has_no_ADMID()
    {
        // EPrints and Archivematica style: ADMID on mets:file, not mets:div
        var xml = """
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                       xmlns:xlink="http://www.w3.org/1999/xlink"
                       xmlns:premis="http://www.loc.gov/premis/v3">
              <mets:amdSec ID="ADM_eprints_file">
                <mets:techMD ID="TECH_eprints_file">
                  <mets:mdWrap MDTYPE="PREMIS:OBJECT">
                    <mets:xmlData>
                      <premis:object>
                        <premis:objectCharacteristics>
                          <premis:fixity>
                            <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                            <premis:messageDigest>eprints99</premis:messageDigest>
                          </premis:fixity>
                          <premis:size>9999</premis:size>
                          <premis:format>
                            <premis:formatDesignation>
                              <premis:formatName>PDF</premis:formatName>
                            </premis:formatDesignation>
                            <premis:formatRegistry>
                              <premis:formatRegistryKey>fmt/18</premis:formatRegistryKey>
                            </premis:formatRegistry>
                          </premis:format>
                        </premis:objectCharacteristics>
                      </premis:object>
                    </mets:xmlData>
                  </mets:mdWrap>
                </mets:techMD>
              </mets:amdSec>
              <mets:fileSec>
                <mets:fileGrp>
                  <mets:file ID="FILE_1" MIMETYPE="application/pdf" ADMID="ADM_eprints_file">
                    <mets:FLocat xlink:href="data/thesis.pdf"/>
                  </mets:file>
                </mets:fileGrp>
              </mets:fileSec>
              <mets:structMap>
                <mets:div>
                  <mets:fptr FILEID="FILE_1"/>
                </mets:div>
              </mets:structMap>
            </mets:mets>
            """;

        var mets = Parse(xml);

        var file = mets.Files[0];
        file.Digest.Should().Be("eprints99");
        file.Size.Should().Be(9999);
        file.Metadata.OfType<FileFormatMetadata>().Single().PronomKey.Should().Be("fmt/18");
    }

    [Fact]
    public void Goobi_style_multiple_fptrs_in_one_div_only_first_gets_digest_and_format()
    {
        // Goobi METS places image and ALTO XML as siblings in the same mets:div.
        // The ADMID on the div applies only to the first fptr; the second file gets no format metadata.
        var xml = """
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                       xmlns:xlink="http://www.w3.org/1999/xlink"
                       xmlns:premis="http://www.loc.gov/premis/v3">
              <mets:amdSec ID="ADM_objects/page1.tif">
                <mets:techMD ID="TECH_objects/page1.tif">
                  <mets:mdWrap MDTYPE="PREMIS:OBJECT">
                    <mets:xmlData>
                      <premis:object>
                        <premis:objectCharacteristics>
                          <premis:fixity>
                            <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                            <premis:messageDigest>page1digest</premis:messageDigest>
                          </premis:fixity>
                          <premis:size>5000000</premis:size>
                          <premis:format>
                            <premis:formatDesignation>
                              <premis:formatName>TIFF</premis:formatName>
                            </premis:formatDesignation>
                            <premis:formatRegistry>
                              <premis:formatRegistryKey>fmt/10</premis:formatRegistryKey>
                            </premis:formatRegistry>
                          </premis:format>
                        </premis:objectCharacteristics>
                      </premis:object>
                    </mets:xmlData>
                  </mets:mdWrap>
                </mets:techMD>
              </mets:amdSec>
              <mets:fileSec>
                <mets:fileGrp>
                  <mets:file ID="FILE_image" MIMETYPE="image/tiff">
                    <mets:FLocat xlink:href="objects/page1.tif"/>
                  </mets:file>
                  <mets:file ID="FILE_alto" MIMETYPE="application/xml">
                    <mets:FLocat xlink:href="objects/alto/page1.xml"/>
                  </mets:file>
                </mets:fileGrp>
              </mets:fileSec>
              <mets:structMap TYPE="PHYSICAL">
                <mets:div ADMID="ADM_objects/page1.tif">
                  <mets:fptr FILEID="FILE_image"/>
                  <mets:fptr FILEID="FILE_alto"/>
                </mets:div>
              </mets:structMap>
            </mets:mets>
            """;

        var mets = Parse(xml);

        var imageFile = mets.Files.Single(f => f.LocalPath == "objects/page1.tif");
        var altoFile = mets.Files.Single(f => f.LocalPath == "objects/alto/page1.xml");

        // First fptr in the div gets the ADMID metadata
        imageFile.Digest.Should().Be("page1digest");
        imageFile.Metadata.OfType<FileFormatMetadata>().Should().HaveCount(1);
        imageFile.Metadata.OfType<FileFormatMetadata>().Single().PronomKey.Should().Be("fmt/10");

        // Second fptr in the same div does not get format metadata (Goobi behaviour)
        altoFile.Digest.Should().BeNull();
        altoFile.Metadata.OfType<FileFormatMetadata>().Should().BeEmpty();
    }

    [Fact]
    public void Duration_as_plain_seconds_is_parsed_from_significantProperties()
    {
        var xml = FullMetsWithSigProps(SigProp("Duration", "596.0"));

        var mets = Parse(xml);

        var extent = mets.Files[0].Metadata.OfType<ExtentMetadata>().Single();
        extent.Duration.Should().BeApproximately(596.0, precision: 0.0001);
    }

    [Fact]
    public void Duration_as_hh_mm_ss_timecode_is_converted_to_seconds()
    {
        // Exif stores duration in h:mm:ss format; the parser must convert to seconds
        var xml = FullMetsWithSigProps(SigProp("Duration", "0:09:56"));

        var mets = Parse(xml);

        var extent = mets.Files[0].Metadata.OfType<ExtentMetadata>().Single();
        extent.Duration.Should().BeApproximately(596.0, precision: 0.0001);
    }

    [Fact]
    public void Duration_with_unrecognised_format_is_silently_ignored()
    {
        var xml = FullMetsWithSigProps(SigProp("Duration", "not-a-duration"));

        var mets = Parse(xml);

        mets.Files[0].Metadata.OfType<ExtentMetadata>().Should().BeEmpty();
    }
}
