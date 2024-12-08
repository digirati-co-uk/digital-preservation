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
}