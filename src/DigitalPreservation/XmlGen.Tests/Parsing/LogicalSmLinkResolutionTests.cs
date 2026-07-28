using FluentAssertions;

namespace XmlGen.Tests.Parsing;

/// <summary>
/// Focused tests for ApplySmLinkLogicalFileRefs: the "deepest-only" rule and ALTO filtering
/// when mets:smLink elements connect logical div IDs to physical div IDs (Goobi-style).
/// </summary>
public class LogicalSmLinkResolutionTests : MetsParserTestBase
{
    [Fact]
    public void When_parent_and_child_claim_same_physical_div_files_go_to_child_not_parent()
    {
        // LOG_0000 claims both physical divs; LOG_0001 and LOG_0002 (depth 1) each also claim one.
        // Deepest-only: both physical divs are owned by the children, so LOG_0000 gets nothing.
        const string xml = """
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                       xmlns:xlink="http://www.w3.org/1999/xlink">
              <mets:fileSec>
                <mets:fileGrp>
                  <mets:file ID="FILE_1" MIMETYPE="image/tiff">
                    <mets:FLocat xlink:href="objects/page1.tif"/>
                  </mets:file>
                  <mets:file ID="FILE_2" MIMETYPE="image/tiff">
                    <mets:FLocat xlink:href="objects/page2.tif"/>
                  </mets:file>
                </mets:fileGrp>
              </mets:fileSec>
              <mets:structMap TYPE="PHYSICAL">
                <mets:div ID="PHYS_0001"><mets:fptr FILEID="FILE_1"/></mets:div>
                <mets:div ID="PHYS_0002"><mets:fptr FILEID="FILE_2"/></mets:div>
              </mets:structMap>
              <mets:structMap TYPE="LOGICAL">
                <mets:div ID="LOG_0000" TYPE="Collection" LABEL="All">
                  <mets:div ID="LOG_0001" TYPE="Part" LABEL="Part 1"/>
                  <mets:div ID="LOG_0002" TYPE="Part" LABEL="Part 2"/>
                </mets:div>
              </mets:structMap>
              <mets:structLink>
                <mets:smLink xlink:from="LOG_0000" xlink:to="PHYS_0001"/>
                <mets:smLink xlink:from="LOG_0000" xlink:to="PHYS_0002"/>
                <mets:smLink xlink:from="LOG_0001" xlink:to="PHYS_0001"/>
                <mets:smLink xlink:from="LOG_0002" xlink:to="PHYS_0002"/>
              </mets:structLink>
            </mets:mets>
            """;

        var mets = Parse(xml);

        var root = mets.LogicalStructures[0];
        root.Files.Should().BeEmpty("LOG_0000 is not the deepest claimant of either physical div");
        root.Ranges[0].Files.Should().HaveCount(1);
        root.Ranges[0].Files[0].LocalPath.Should().Be("objects/page1.tif");
        root.Ranges[1].Files.Should().HaveCount(1);
        root.Ranges[1].Files[0].LocalPath.Should().Be("objects/page2.tif");
    }

    [Fact]
    public void Physical_div_claimed_only_by_parent_contributes_its_files_to_parent()
    {
        // LOG_0000 claims PHYS_0001/2/3. LOG_0001 claims PHYS_0001; LOG_0002 claims PHYS_0002.
        // PHYS_0003 has no deeper claimant, so LOG_0000 owns it and gets page3.tif.
        const string xml = """
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                       xmlns:xlink="http://www.w3.org/1999/xlink">
              <mets:fileSec>
                <mets:fileGrp>
                  <mets:file ID="FILE_1" MIMETYPE="image/tiff">
                    <mets:FLocat xlink:href="objects/page1.tif"/>
                  </mets:file>
                  <mets:file ID="FILE_2" MIMETYPE="image/tiff">
                    <mets:FLocat xlink:href="objects/page2.tif"/>
                  </mets:file>
                  <mets:file ID="FILE_3" MIMETYPE="image/tiff">
                    <mets:FLocat xlink:href="objects/page3.tif"/>
                  </mets:file>
                </mets:fileGrp>
              </mets:fileSec>
              <mets:structMap TYPE="PHYSICAL">
                <mets:div ID="PHYS_0001"><mets:fptr FILEID="FILE_1"/></mets:div>
                <mets:div ID="PHYS_0002"><mets:fptr FILEID="FILE_2"/></mets:div>
                <mets:div ID="PHYS_0003"><mets:fptr FILEID="FILE_3"/></mets:div>
              </mets:structMap>
              <mets:structMap TYPE="LOGICAL">
                <mets:div ID="LOG_0000" TYPE="Collection" LABEL="All">
                  <mets:div ID="LOG_0001" TYPE="Part" LABEL="Part 1"/>
                  <mets:div ID="LOG_0002" TYPE="Part" LABEL="Part 2"/>
                </mets:div>
              </mets:structMap>
              <mets:structLink>
                <mets:smLink xlink:from="LOG_0000" xlink:to="PHYS_0001"/>
                <mets:smLink xlink:from="LOG_0000" xlink:to="PHYS_0002"/>
                <mets:smLink xlink:from="LOG_0000" xlink:to="PHYS_0003"/>
                <mets:smLink xlink:from="LOG_0001" xlink:to="PHYS_0001"/>
                <mets:smLink xlink:from="LOG_0002" xlink:to="PHYS_0002"/>
              </mets:structLink>
            </mets:mets>
            """;

        var mets = Parse(xml);

        var root = mets.LogicalStructures[0];
        root.Files.Should().HaveCount(1);
        root.Files[0].LocalPath.Should().Be("objects/page3.tif");
        root.Ranges[0].Files.Should().HaveCount(1);
        root.Ranges[0].Files[0].LocalPath.Should().Be("objects/page1.tif");
        root.Ranges[1].Files.Should().HaveCount(1);
        root.Ranges[1].Files[0].LocalPath.Should().Be("objects/page2.tif");
    }

    [Fact]
    public void Alto_files_are_excluded_from_logical_range_files()
    {
        // PHYS_0001 contains both a primary image (USE=OBJECTS) and its ALTO transcription (USE=ALTO).
        // Only the primary file should appear in LogicalRange.Files; ALTO files are technical derivatives
        // used for search/display, not as IIIF canvases in their own right.
        const string xml = """
            <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                       xmlns:xlink="http://www.w3.org/1999/xlink">
              <mets:fileSec>
                <mets:fileGrp USE="OBJECTS">
                  <mets:file ID="FILE_1" MIMETYPE="image/tiff">
                    <mets:FLocat xlink:href="objects/page1.tif"/>
                  </mets:file>
                </mets:fileGrp>
                <mets:fileGrp USE="ALTO">
                  <mets:file ID="ALTO_1" MIMETYPE="text/xml">
                    <mets:FLocat xlink:href="objects/page1.alto.xml"/>
                  </mets:file>
                </mets:fileGrp>
              </mets:fileSec>
              <mets:structMap TYPE="PHYSICAL">
                <mets:div ID="PHYS_0001">
                  <mets:fptr FILEID="FILE_1"/>
                  <mets:fptr FILEID="ALTO_1"/>
                </mets:div>
              </mets:structMap>
              <mets:structMap TYPE="LOGICAL">
                <mets:div ID="LOG_0000" TYPE="Collection" LABEL="All">
                  <mets:div ID="LOG_0001" TYPE="Part" LABEL="Page 1"/>
                </mets:div>
              </mets:structMap>
              <mets:structLink>
                <mets:smLink xlink:from="LOG_0001" xlink:to="PHYS_0001"/>
              </mets:structLink>
            </mets:mets>
            """;

        var mets = Parse(xml);

        var part = mets.LogicalStructures[0].Ranges[0];
        part.Files.Should().HaveCount(1);
        part.Files[0].LocalPath.Should().Be("objects/page1.tif");
    }
}
