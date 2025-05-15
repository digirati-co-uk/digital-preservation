using DigitalPreservation.Utils;

namespace DigitalPreservation.Core.Tests.Strings;

public class StringUtilTests
{
    // Only add tests for new Util methods
    
    [Fact]
    public void Can_Join_Strings_To_Path()
    {
        // Act
        var path = StringUtils.BuildPath(false, "aa", "bb", "cc");
        // Assert
        path.Should().Be("aa/bb/cc");
    } 
    
    
    [Fact]
    public void Can_Join_Strings_To_Path_Initial_Sep()
    {
        // Act
        var path = StringUtils.BuildPath(true, "aa", "bb", "cc");
        // Assert
        path.Should().Be("/aa/bb/cc");
    } 
    
    
    [Fact]
    public void Can_Join_Strings_To_Path_OneEl()
    {
        // Act
        var path = StringUtils.BuildPath(true, "a");
        // Assert
        path.Should().Be("/a");
    } 
    
    
    [Fact]
    public void Can_Join_Strings_To_Path_SubPaths()
    {
        // Act
        var path = StringUtils.BuildPath(true, "aa", "bb/cc", "/dd/ee", "/ff/gg/hh", "ii/jj/kk/");
        // Assert
        path.Should().Be("/aa/bb/cc/dd/ee/ff/gg/hh/ii/jj/kk");
    }

    [Fact]
    public void GetCommonPrefix_returns_common_prefix()
    {
        string[] strings =
        [
            "tomsk",
            "tomato",
            "tomahawk"
        ];
        StringUtils.GetCommonPrefix(strings).Should().Be("tom");
    }
    [Fact]
    
    public void GetCommonPrefix_returns_no_common_prefix()
    {
        string[] strings =
        [
            "foo",
            "bar",
            "baz"
        ];
        StringUtils.GetCommonPrefix(strings).Should().Be(string.Empty);
    }

    [Fact]
    public void GetCommonPrefix_returns_common_file_path()
    {
        string[] strings =
        [
            "/home/tomcrane/__packing_area/bc-example-1/objects/Fedora-Usage-Principles.docx",
            "/home/tomcrane/__packing_area/bc-example-1/objects/IMAGE-2.tiff",
            "/home/tomcrane/__packing_area/bc-example-1/objects/awkward/7 ways to celebrate #WomensHistoryMonth 💜 And a sneak peek at SICK new art.htm",
            "/home/tomcrane/__packing_area/bc-example-1/objects/awkward/慷獹琠散敬牢瑡圣浯湥䡳獩潴祲潍瑮㼿䄠摮愠猠敮歡瀠敥瑡匠䍉敮⁷牡�.msg",
            "/home/tomcrane/__packing_area/bc-example-1/objects/nyc/DSCF0969.JPG",
            "/home/tomcrane/__packing_area/bc-example-1/objects/nyc/DSCF0981.JPG",
            "/home/tomcrane/__packing_area/bc-example-1/objects/warteck.jpg"
        ];
        
        StringUtils.GetCommonPrefix(strings).Should().Be("/home/tomcrane/__packing_area/bc-example-1/objects/");
    }
    
     

    [Fact]
    public void GetCommonParent_returns_common_parent_folder()
    {
        string[] strings =
        [
            "/home/tomcrane/__packing_area/bc-example-1/objects/Fedora-Usage-Principles.docx",
            "/home/tomcrane/__packing_area/bc-example-1/objects/IMAGE-2.tiff",
            "/home/tomcrane/__packing_area/bc-example-1/objects/awkward/7 ways to celebrate #WomensHistoryMonth 💜 And a sneak peek at SICK new art.htm",
            "/home/tomcrane/__packing_area/bc-example-1/objects/awkward/慷獹琠散敬牢瑡圣浯湥䡳獩潴祲潍瑮㼿䄠摮愠猠敮歡瀠敥瑡匠䍉敮⁷牡�.msg",
            "/home/tomcrane/__packing_area/bc-example-1/objects/nyc/DSCF0969.JPG",
            "/home/tomcrane/__packing_area/bc-example-1/objects/nyc/DSCF0981.JPG",
            "/home/tomcrane/__packing_area/bc-example-1/objects/warteck.jpg"
        ];
        
        StringUtils.GetCommonParent(strings).Should().Be("/home/tomcrane/__packing_area/bc-example-1/objects");
    }

    [Fact]
    public void GetCommonParent_returns_common_parent_folder_despite_further_prefix_match()
    {
        string[] strings =
        [
            "/home/tomcrane/__packing_area/bc-example-1/objects/xxFedora-Usage-Principles.docx",
            "/home/tomcrane/__packing_area/bc-example-1/objects/xxIMAGE-2.tiff",
            "/home/tomcrane/__packing_area/bc-example-1/objects/xxawkward/7 ways to celebrate #WomensHistoryMonth 💜 And a sneak peek at SICK new art.htm",
            "/home/tomcrane/__packing_area/bc-example-1/objects/xxawkward/慷獹琠散敬牢瑡圣浯湥䡳獩潴祲潍瑮㼿䄠摮愠猠敮歡瀠敥瑡匠䍉敮⁷牡�.msg",
            "/home/tomcrane/__packing_area/bc-example-1/objects/xxnyc/DSCF0969.JPG",
            "/home/tomcrane/__packing_area/bc-example-1/objects/xxnyc/DSCF0981.JPG",
            "/home/tomcrane/__packing_area/bc-example-1/objects/xxwarteck.jpg"
        ];
        
        StringUtils.GetCommonParent(strings).Should().Be("/home/tomcrane/__packing_area/bc-example-1/objects");
    }
    
}