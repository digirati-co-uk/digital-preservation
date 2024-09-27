using DigitalPreservation.Utils;

namespace DigitalPreservation.Core.Tests.Strings;

public class PathTests
{
    [Theory]
    [InlineData("aa/bb/cc", "cc")]
    [InlineData("aa/bb/cc/", "cc")]
    [InlineData("/aa/bb/cc", "cc")]
    [InlineData("/aa/bb/cc/", "cc")]
    public void Can_Get_Slug(string path, string expected)
    {
        var slug = path.GetSlug();
        slug.Should().Be(expected);
    }
    
    
    [Theory]
    [InlineData("aa/bb/cc", "aa/bb")]
    [InlineData("aa/bb/cc/", "aa/bb")]
    [InlineData("/aa/bb/cc", "/aa/bb")]
    [InlineData("/aa/bb/cc/", "/aa/bb")]
    public void Can_Get_Parent(string path, string expected)
    {
        var parent = path.GetParent();
        parent.Should().Be(expected);
    }
    
    [Theory]
    [InlineData("aa", "")]
    [InlineData("aa/", "")]
    [InlineData("/aa", "/")]
    [InlineData("/aa/", "/")]
    public void Can_Get_Parent_Root(string path, string? expected)
    {
        var parent = path.GetParent();
        parent.Should().Be(expected);
    }
    
    [Theory]
    [InlineData("", null)]
    [InlineData("/", null)]
    public void Parent_Of_Root_Is_Null(string path, string? expected)
    {
        var parent = path.GetParent();
        parent.Should().Be(expected);
    }

    [Fact]
    public void Can_Walk_Up()
    {
        var path = "aa/bb/cc/dd/ee";
        var parent = path.GetParent();
        int counter = 0;
        while (parent != null)
        {
            parent = parent.GetParent();
            counter++;
        }
        counter.Should().Be(5);
    }
}