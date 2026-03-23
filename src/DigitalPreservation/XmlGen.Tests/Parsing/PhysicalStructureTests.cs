using FluentAssertions;

namespace XmlGen.Tests.Parsing;

/// <summary>
/// Focused tests for the physical structMap parsing: directory and file structure extraction.
/// These use inline XML via GetMetsFileWrapperFromXDocument so each test is self-contained.
/// </summary>
public class PhysicalStructureTests : MetsParserTestBase
{
    [Fact]
    public void Single_file_creates_directory_from_flocat_path()
    {
        var xml = """
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                       xmlns:xlink="http://www.w3.org/1999/xlink"
                       xmlns:premis="http://www.loc.gov/premis/v3">
              <mets:amdSec ID="ADM_objects/img.png">
                <mets:techMD ID="TECH_objects/img.png">
                  <mets:mdWrap MDTYPE="PREMIS:OBJECT">
                    <mets:xmlData>
                      <premis:object>
                        <premis:objectCharacteristics>
                          <premis:fixity>
                            <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                            <premis:messageDigest>abc123</premis:messageDigest>
                          </premis:fixity>
                        </premis:objectCharacteristics>
                      </premis:object>
                    </mets:xmlData>
                  </mets:mdWrap>
                </mets:techMD>
              </mets:amdSec>
              <mets:fileSec>
                <mets:fileGrp>
                  <mets:file ID="FILE_1" MIMETYPE="image/png">
                    <mets:FLocat xlink:href="objects/img.png"/>
                  </mets:file>
                </mets:fileGrp>
              </mets:fileSec>
              <mets:structMap TYPE="PHYSICAL">
                <mets:div ADMID="ADM_objects/img.png">
                  <mets:fptr FILEID="FILE_1"/>
                </mets:div>
              </mets:structMap>
            </mets:mets>
            """;

        var mets = Parse(xml);

        var phys = mets.PhysicalStructure!;
        phys.Directories.Should().HaveCount(1);
        phys.Directories[0].Name.Should().Be("objects");
        phys.Directories[0].LocalPath.Should().Be("objects");
        phys.Directories[0].Files.Should().HaveCount(1);
        phys.Directories[0].Files[0].LocalPath.Should().Be("objects/img.png");
        phys.Directories[0].Files[0].ContentType.Should().Be("image/png");
    }

    [Fact]
    public void Explicit_directory_div_sets_name_from_label_and_mets_extensions()
    {
        // An explicit TYPE=Directory div with ADMID and premis:originalName sets
        // MetsExtensions on the directory.
        var xml = """
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
        dir.Name.Should().Be("Objects");
        dir.LocalPath.Should().Be("objects");
        dir.MetsExtensions.Should().NotBeNull();
        dir.MetsExtensions!.DivId.Should().Be("PHYS_objects");
        dir.MetsExtensions.AdmId.Should().Be("ADM_objects");

        var file = dir.Files[0];
        file.MetsExtensions!.DivId.Should().Be("PHYS_objects/file.txt");
        file.MetsExtensions.AdmId.Should().Be("ADM_objects/file.txt");
    }

    [Fact]
    public void Multiple_files_in_same_directory_are_all_in_directory_files_list()
    {
        var xml = """
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                       xmlns:xlink="http://www.w3.org/1999/xlink"
                       xmlns:premis="http://www.loc.gov/premis/v3">
              <mets:amdSec ID="ADM_objects/a.txt">
                <mets:techMD ID="TECH_objects/a.txt">
                  <mets:mdWrap MDTYPE="PREMIS:OBJECT">
                    <mets:xmlData>
                      <premis:object>
                        <premis:objectCharacteristics>
                          <premis:fixity>
                            <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                            <premis:messageDigest>aaa111</premis:messageDigest>
                          </premis:fixity>
                        </premis:objectCharacteristics>
                      </premis:object>
                    </mets:xmlData>
                  </mets:mdWrap>
                </mets:techMD>
              </mets:amdSec>
              <mets:amdSec ID="ADM_objects/b.txt">
                <mets:techMD ID="TECH_objects/b.txt">
                  <mets:mdWrap MDTYPE="PREMIS:OBJECT">
                    <mets:xmlData>
                      <premis:object>
                        <premis:objectCharacteristics>
                          <premis:fixity>
                            <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                            <premis:messageDigest>bbb222</premis:messageDigest>
                          </premis:fixity>
                        </premis:objectCharacteristics>
                      </premis:object>
                    </mets:xmlData>
                  </mets:mdWrap>
                </mets:techMD>
              </mets:amdSec>
              <mets:fileSec>
                <mets:fileGrp>
                  <mets:file ID="FILE_A" MIMETYPE="text/plain">
                    <mets:FLocat xlink:href="objects/a.txt"/>
                  </mets:file>
                  <mets:file ID="FILE_B" MIMETYPE="text/plain">
                    <mets:FLocat xlink:href="objects/b.txt"/>
                  </mets:file>
                </mets:fileGrp>
              </mets:fileSec>
              <mets:structMap TYPE="PHYSICAL">
                <mets:div ADMID="ADM_objects/a.txt">
                  <mets:fptr FILEID="FILE_A"/>
                </mets:div>
                <mets:div ADMID="ADM_objects/b.txt">
                  <mets:fptr FILEID="FILE_B"/>
                </mets:div>
              </mets:structMap>
            </mets:mets>
            """;

        var mets = Parse(xml);

        var objects = mets.PhysicalStructure!.Directories[0];
        objects.Name.Should().Be("objects");
        objects.Files.Should().HaveCount(2);
        objects.Files.Should().Contain(f => f.LocalPath == "objects/a.txt");
        objects.Files.Should().Contain(f => f.LocalPath == "objects/b.txt");

        mets.Files.Should().HaveCount(2);
    }

    [Fact]
    public void Nested_directory_is_inferred_from_file_path()
    {
        var xml = """
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                       xmlns:xlink="http://www.w3.org/1999/xlink"
                       xmlns:premis="http://www.loc.gov/premis/v3">
              <mets:amdSec ID="ADM_objects/subdir/file.xml">
                <mets:techMD ID="TECH_objects/subdir/file.xml">
                  <mets:mdWrap MDTYPE="PREMIS:OBJECT">
                    <mets:xmlData>
                      <premis:object>
                        <premis:objectCharacteristics>
                          <premis:fixity>
                            <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                            <premis:messageDigest>cafe1234</premis:messageDigest>
                          </premis:fixity>
                        </premis:objectCharacteristics>
                      </premis:object>
                    </mets:xmlData>
                  </mets:mdWrap>
                </mets:techMD>
              </mets:amdSec>
              <mets:fileSec>
                <mets:fileGrp>
                  <mets:file ID="FILE_1" MIMETYPE="application/xml">
                    <mets:FLocat xlink:href="objects/subdir/file.xml"/>
                  </mets:file>
                </mets:fileGrp>
              </mets:fileSec>
              <mets:structMap TYPE="PHYSICAL">
                <mets:div ADMID="ADM_objects/subdir/file.xml">
                  <mets:fptr FILEID="FILE_1"/>
                </mets:div>
              </mets:structMap>
            </mets:mets>
            """;

        var mets = Parse(xml);

        var objects = mets.PhysicalStructure!.Directories[0];
        objects.Name.Should().Be("objects");
        objects.Directories.Should().HaveCount(1);

        var subdir = objects.Directories[0];
        subdir.Name.Should().Be("subdir");
        subdir.LocalPath.Should().Be("objects/subdir");
        subdir.Files.Should().HaveCount(1);
        subdir.Files[0].LocalPath.Should().Be("objects/subdir/file.xml");
    }

    [Fact]
    public void StructMap_without_TYPE_attribute_is_treated_as_physical()
    {
        // EPrints-style: structMap has no TYPE attribute
        var xml = """
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                       xmlns:xlink="http://www.w3.org/1999/xlink"
                       xmlns:premis="http://www.loc.gov/premis/v3">
              <mets:amdSec ID="ADM_data/thesis.pdf">
                <mets:techMD ID="TECH_data/thesis.pdf">
                  <mets:mdWrap MDTYPE="PREMIS:OBJECT">
                    <mets:xmlData>
                      <premis:object>
                        <premis:objectCharacteristics>
                          <premis:fixity>
                            <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                            <premis:messageDigest>11223344</premis:messageDigest>
                          </premis:fixity>
                        </premis:objectCharacteristics>
                      </premis:object>
                    </mets:xmlData>
                  </mets:mdWrap>
                </mets:techMD>
              </mets:amdSec>
              <mets:fileSec>
                <mets:fileGrp>
                  <mets:file ID="FILE_1" MIMETYPE="application/pdf">
                    <mets:FLocat xlink:href="data/thesis.pdf"/>
                  </mets:file>
                </mets:fileGrp>
              </mets:fileSec>
              <mets:structMap>
                <mets:div ADMID="ADM_data/thesis.pdf">
                  <mets:fptr FILEID="FILE_1"/>
                </mets:div>
              </mets:structMap>
            </mets:mets>
            """;

        var mets = Parse(xml);

        mets.Files.Should().HaveCount(1);
        mets.Files[0].LocalPath.Should().Be("data/thesis.pdf");
        mets.PhysicalStructure!.Directories.Should().HaveCount(1);
        mets.PhysicalStructure.Directories[0].Name.Should().Be("data");
    }

    [Fact]
    public void File_name_is_last_segment_of_flocat_when_no_label_on_div()
    {
        var xml = """
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                       xmlns:xlink="http://www.w3.org/1999/xlink"
                       xmlns:premis="http://www.loc.gov/premis/v3">
              <mets:amdSec ID="ADM_objects/portrait.tif">
                <mets:techMD ID="TECH_objects/portrait.tif">
                  <mets:mdWrap MDTYPE="PREMIS:OBJECT">
                    <mets:xmlData>
                      <premis:object>
                        <premis:objectCharacteristics>
                          <premis:fixity>
                            <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                            <premis:messageDigest>ff00ff</premis:messageDigest>
                          </premis:fixity>
                        </premis:objectCharacteristics>
                      </premis:object>
                    </mets:xmlData>
                  </mets:mdWrap>
                </mets:techMD>
              </mets:amdSec>
              <mets:fileSec>
                <mets:fileGrp>
                  <mets:file ID="FILE_1" MIMETYPE="image/tiff">
                    <mets:FLocat xlink:href="objects/portrait.tif"/>
                  </mets:file>
                </mets:fileGrp>
              </mets:fileSec>
              <mets:structMap TYPE="PHYSICAL">
                <mets:div ADMID="ADM_objects/portrait.tif">
                  <mets:fptr FILEID="FILE_1"/>
                </mets:div>
              </mets:structMap>
            </mets:mets>
            """;

        var mets = Parse(xml);

        mets.Files[0].Name.Should().Be("portrait.tif");
    }

    [Fact]
    public void File_name_uses_LABEL_from_div_when_present()
    {
        var xml = """
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                       xmlns:xlink="http://www.w3.org/1999/xlink"
                       xmlns:premis="http://www.loc.gov/premis/v3">
              <mets:amdSec ID="ADM_objects/001.tif">
                <mets:techMD ID="TECH_objects/001.tif">
                  <mets:mdWrap MDTYPE="PREMIS:OBJECT">
                    <mets:xmlData>
                      <premis:object>
                        <premis:objectCharacteristics>
                          <premis:fixity>
                            <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                            <premis:messageDigest>001001</premis:messageDigest>
                          </premis:fixity>
                        </premis:objectCharacteristics>
                      </premis:object>
                    </mets:xmlData>
                  </mets:mdWrap>
                </mets:techMD>
              </mets:amdSec>
              <mets:fileSec>
                <mets:fileGrp>
                  <mets:file ID="FILE_1" MIMETYPE="image/tiff">
                    <mets:FLocat xlink:href="objects/001.tif"/>
                  </mets:file>
                </mets:fileGrp>
              </mets:fileSec>
              <mets:structMap TYPE="PHYSICAL">
                <mets:div LABEL="Page 1" ADMID="ADM_objects/001.tif">
                  <mets:fptr FILEID="FILE_1"/>
                </mets:div>
              </mets:structMap>
            </mets:mets>
            """;

        var mets = Parse(xml);

        mets.Files[0].Name.Should().Be("Page 1");
    }

    [Fact]
    public void Flat_files_list_contains_all_parsed_files()
    {
        var xml = """
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                       xmlns:xlink="http://www.w3.org/1999/xlink"
                       xmlns:premis="http://www.loc.gov/premis/v3">
              <mets:amdSec ID="ADM_objects/one.txt">
                <mets:techMD ID="TECH_objects/one.txt">
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
              <mets:amdSec ID="ADM_objects/two.txt">
                <mets:techMD ID="TECH_objects/two.txt">
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
              <mets:amdSec ID="ADM_objects/subdir/three.txt">
                <mets:techMD ID="TECH_objects/subdir/three.txt">
                  <mets:mdWrap MDTYPE="PREMIS:OBJECT">
                    <mets:xmlData>
                      <premis:object>
                        <premis:objectCharacteristics>
                          <premis:fixity>
                            <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                            <premis:messageDigest>333</premis:messageDigest>
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
                    <mets:FLocat xlink:href="objects/one.txt"/>
                  </mets:file>
                  <mets:file ID="FILE_2" MIMETYPE="text/plain">
                    <mets:FLocat xlink:href="objects/two.txt"/>
                  </mets:file>
                  <mets:file ID="FILE_3" MIMETYPE="text/plain">
                    <mets:FLocat xlink:href="objects/subdir/three.txt"/>
                  </mets:file>
                </mets:fileGrp>
              </mets:fileSec>
              <mets:structMap TYPE="PHYSICAL">
                <mets:div ADMID="ADM_objects/one.txt">
                  <mets:fptr FILEID="FILE_1"/>
                </mets:div>
                <mets:div ADMID="ADM_objects/two.txt">
                  <mets:fptr FILEID="FILE_2"/>
                </mets:div>
                <mets:div ADMID="ADM_objects/subdir/three.txt">
                  <mets:fptr FILEID="FILE_3"/>
                </mets:div>
              </mets:structMap>
            </mets:mets>
            """;

        var mets = Parse(xml);

        mets.Files.Should().HaveCount(3);
        mets.Files.Should().Contain(f => f.LocalPath == "objects/one.txt");
        mets.Files.Should().Contain(f => f.LocalPath == "objects/two.txt");
        mets.Files.Should().Contain(f => f.LocalPath == "objects/subdir/three.txt");
    }
}
