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

    [Fact]
    public void Combined_TIME_and_RECT_on_same_area_element_sets_both_BeginTime_and_Region()
    {
        // A single mets:area can carry both temporal (BETYPE/BEGIN/END) and spatial
        // (SHAPE/COORDS) attributes simultaneously. Both must be parsed.
        var xml = MetsWithAreaFptr("""BETYPE="TIME" BEGIN="00:03:57" END="00:04:10" SHAPE="RECT" COORDS="10,20,100,200" """);

        var mets = Parse(xml);

        var fp = mets.LogicalStructures[0].Files[0];
        fp.BeginTime.Should().Be(237.0);
        fp.EndTime.Should().Be(250.0);
        fp.Region.Should().NotBeNull();
        fp.Region!.X1.Should().Be(10);
        fp.Region.Y1.Should().Be(20);
        fp.Region.X2.Should().Be(100);
        fp.Region.Y2.Should().Be(200);
    }

    [Fact]
    public void Unrecognised_BETYPE_preserves_all_area_attributes_in_ExtraAreaAttributes()
    {
        // An area element with a BETYPE we don't handle (e.g. BYTE) must not be silently
        // dropped: all its attributes (except FILEID) must be stored in ExtraAreaAttributes
        // so that a round-trip through the manager can write them back unchanged.
        var xml = MetsWithAreaFptr("""BETYPE="BYTE" BEGIN="0" END="1000" """);

        var mets = Parse(xml);

        var fp = mets.LogicalStructures[0].Files[0];
        fp.LocalPath.Should().Be("objects/page.tif");
        fp.BeginTime.Should().BeNull();
        fp.EndTime.Should().BeNull();
        fp.Region.Should().BeNull();
        fp.ExtraAreaAttributes.Should().NotBeNull();
        fp.ExtraAreaAttributes.Should().ContainKey("BETYPE").WhoseValue.Should().Be("BYTE");
        fp.ExtraAreaAttributes.Should().ContainKey("BEGIN").WhoseValue.Should().Be("0");
        fp.ExtraAreaAttributes.Should().ContainKey("END").WhoseValue.Should().Be("1000");
        fp.ExtraAreaAttributes.Should().NotContainKey("FILEID");
    }

    [Fact]
    public void Unrecognised_SHAPE_preserves_all_area_attributes_in_ExtraAreaAttributes()
    {
        // SHAPE=CIRCLE is not currently supported; its attributes must be preserved
        // in ExtraAreaAttributes rather than silently discarded.
        var xml = MetsWithAreaFptr("""SHAPE="CIRCLE" COORDS="300,300,200" """);

        var mets = Parse(xml);

        var fp = mets.LogicalStructures[0].Files[0];
        fp.LocalPath.Should().Be("objects/page.tif");
        fp.Region.Should().BeNull();
        fp.BeginTime.Should().BeNull();
        fp.EndTime.Should().BeNull();
        fp.ExtraAreaAttributes.Should().NotBeNull();
        fp.ExtraAreaAttributes.Should().ContainKey("SHAPE").WhoseValue.Should().Be("CIRCLE");
        fp.ExtraAreaAttributes.Should().ContainKey("COORDS").WhoseValue.Should().Be("300,300,200");
        fp.ExtraAreaAttributes.Should().NotContainKey("FILEID");
    }
}
