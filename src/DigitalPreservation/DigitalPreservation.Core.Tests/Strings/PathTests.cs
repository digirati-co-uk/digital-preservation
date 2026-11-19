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

    [Theory]
    [InlineData("aa/bb/cc", "aa", "bb/cc")]
    [InlineData("aa/bb/cc", "aa/bb", "cc")]
    [InlineData("/aa/bb/cc", "/aa/bb", "cc")]
    [InlineData("aa/bb/cc/", "aa/bb", "cc")]
    [InlineData("aa/bb/cc", "aa/bb/", "cc")]
    [InlineData("aa/bb/cc/", "aa/bb/", "cc")]
    [InlineData("/repository/cc/thing/metadata/ad-hoc", "/repository/cc/thing", "metadata/ad-hoc")]
    [InlineData("/aa/bb", "/", "aa/bb")]     // "/" is a real root
    public void Can_Get_Path_Below(string path, string parent, string expected)
    {
        path.GetPathBelow(parent).Should().Be(expected);
    }

    [Theory]
    [InlineData("aa/bb", "aa/bb")]          // equal, not below
    [InlineData("aa/bb/", "aa/bb")]         // equal, not below
    [InlineData("aa/bbb", "aa/bb")]         // longer sibling, not a child
    [InlineData("aa/bb", "aa/bb/cc")]       // above, not below
    [InlineData("xx/bb/cc", "aa")]          // elsewhere entirely
    [InlineData("aa/bb", "")]               // no parent means nothing is "below" it...
    [InlineData("/aa/bb", "")]              // ...rooted or not
    public void Path_Not_Below_Parent_Is_Null(string path, string parent)
    {
        path.GetPathBelow(parent).Should().BeNull();
    }

    // IsStrictlyBelow (issue #388 item 2.7) is GetPathBelow's yes/no form, used by both APIs'
    // "resource lies within its Archival Group" check in place of hand-rolled TrimEnd('/') + "/"
    // and StartsWith. Same cases as the two theories above, through the bool-returning form.
    [Theory]
    [InlineData("aa/bb/cc", "aa")]
    [InlineData("aa/bb/cc", "aa/bb")]
    [InlineData("/aa/bb/cc", "/aa/bb")]
    [InlineData("/aa/bb", "/")]              // "/" is a real root
    public void IsStrictlyBelow_IsTrue_WhenPathIsAChildOfParent(string path, string parent)
    {
        path.IsStrictlyBelow(parent).Should().BeTrue();
    }

    [Theory]
    [InlineData("aa/bb", "aa/bb")]          // equal, not below
    [InlineData("aa/bbb", "aa/bb")]         // longer sibling, not a child
    [InlineData("aa/bb", "aa/bb/cc")]       // above, not below
    [InlineData("xx/bb/cc", "aa")]          // elsewhere entirely
    [InlineData("aa/bb", "")]               // no parent means nothing is "below" it
    public void IsStrictlyBelow_IsFalse_WhenPathIsNotAStrictChildOfParent(string path, string parent)
    {
        path.IsStrictlyBelow(parent).Should().BeFalse();
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