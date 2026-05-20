using FluentAssertions;

namespace XmlGen.Tests.Parsing;

/// <summary>
/// Focused tests for ApplyAreaProperties: parsing of mets:area attributes (TIME, RECT)
/// within logical structMap fptr elements.
/// </summary>
public class LogicalFptrAreaTests : MetsParserTestBase
{
    private static string MetsWithAreaFptr(string areaAttributes) => $"""
        <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                   xmlns:xlink="http://www.w3.org/1999/xlink">
          <mets:fileSec>
            <mets:fileGrp>
              <mets:file ID="FILE_1" MIMETYPE="image/tiff">
                <mets:FLocat xlink:href="objects/page.tif"/>
              </mets:file>
            </mets:fileGrp>
          </mets:fileSec>
          <mets:structMap TYPE="PHYSICAL">
            <mets:div ID="PHYS_0001"><mets:fptr FILEID="FILE_1"/></mets:div>
          </mets:structMap>
          <mets:structMap TYPE="LOGICAL">
            <mets:div ID="LOG_0001" TYPE="Item" LABEL="Item">
              <mets:fptr>
                <mets:area FILEID="FILE_1" {areaAttributes}/>
              </mets:fptr>
            </mets:div>
          </mets:structMap>
        </mets:mets>
        """;

    [Fact]
    public void Time_coded_area_sets_BeginTime_and_EndTime()
    {
        var xml = MetsWithAreaFptr("""BETYPE="TIME" BEGIN="00:00:15" END="00:35:09.5" """);

        var mets = Parse(xml);

        var fp = mets.LogicalStructures[0].Files[0];
        fp.LocalPath.Should().Be("objects/page.tif");
        fp.BeginTime.Should().Be(15.0);
        fp.EndTime.Should().Be(2109.5);
        fp.Region.Should().BeNull();
    }

    [Fact]
    public void Rect_area_sets_Region_coordinates()
    {
        var xml = MetsWithAreaFptr("""SHAPE="RECT" COORDS="0,2000,6000,4000" """);

        var mets = Parse(xml);

        var fp = mets.LogicalStructures[0].Files[0];
        fp.LocalPath.Should().Be("objects/page.tif");
        fp.Region.Should().NotBeNull();
        fp.Region!.X1.Should().Be(0);
        fp.Region.Y1.Should().Be(2000);
        fp.Region.X2.Should().Be(6000);
        fp.Region.Y2.Should().Be(4000);
        fp.BeginTime.Should().BeNull();
        fp.EndTime.Should().BeNull();
    }

    [Fact]
    public void Malformed_time_code_is_caught_and_FilePointer_is_still_returned()
    {
        // A FormatException from MetsTimeCode.ToSeconds must be swallowed; the FilePointer
        // should still be returned with its LocalPath but no timing information.
        var xml = MetsWithAreaFptr("""BETYPE="TIME" BEGIN="not-a-timecode" END="00:00:30" """);

        var mets = Parse(xml);

        var fp = mets.LogicalStructures[0].Files[0];
        fp.LocalPath.Should().Be("objects/page.tif");
        fp.BeginTime.Should().BeNull();
        fp.EndTime.Should().BeNull();
    }
}
