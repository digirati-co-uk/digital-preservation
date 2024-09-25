
using DigitalPreservation.Utils;

namespace DigitalPreservation.Core.Tests.Strings;

public class UriTests
{
    [Fact]
    public void Can_Get_Uri_Parent_Slash_Expt()
    {
        var uri = new Uri("http://www.test.com/aa/bb/cc/");
        var parent = new Uri(uri, "..");
        parent.ToString().Should().Be("http://www.test.com/aa/bb/");
    }
    
    [Fact]
    public void Can_Get_Uri_Parent_NoSlash_Expt()
    {
        var uri = new Uri("http://www.test.com/aa/bb/cc");
        var parent = new Uri(uri, ".");
        parent.ToString().Should().Be("http://www.test.com/aa/bb/");
    }
    
    [Fact]
    public void Can_Get_Uri_Parent_Slash()
    {
        var uri = new Uri("http://www.test.com/aa/bb/cc/");
        var parent = uri.GetParentUri();
        parent!.ToString().Should().Be("http://www.test.com/aa/bb/");
    }
    
    [Fact]
    public void Can_Get_Uri_Parent_NoSlash()
    {
        var uri = new Uri("http://www.test.com/aa/bb/cc");
        var parent = uri.GetParentUri();
        parent!.ToString().Should().Be("http://www.test.com/aa/bb/");
    }
    
    [Fact]
    public void Can_Get_Uri_Parent_Slash_Qs()
    {
        var uri = new Uri("http://www.test.com/aa/bb/cc/?q=1");
        var parent = uri.GetParentUri();
        parent!.ToString().Should().Be("http://www.test.com/aa/bb/");
    }
    
    [Fact]
    public void Can_Get_Uri_Parent_NoSlash_Qs()
    {
        var uri = new Uri("http://www.test.com/aa/bb/cc?q=1");
        var parent = uri.GetParentUri();
        parent!.ToString().Should().Be("http://www.test.com/aa/bb/");
    }
    
    
    [Fact]
    public void Can_Get_Uri_Parent_Root_Slash()
    {
        var uri = new Uri("http://www.test.com/aa/");
        var parent = uri.GetParentUri();
        parent!.ToString().Should().Be("http://www.test.com/");
    }
    
    [Fact]
    public void Can_Get_Uri_Parent_Root_NoSlash()
    {
        var uri = new Uri("http://www.test.com/aa");
        var parent = uri.GetParentUri();
        parent!.ToString().Should().Be("http://www.test.com/");
    }
    
    
    [Fact]
    public void Can_Get_Uri_Parent_Root_Slash_Host()
    {
        var uri = new Uri("http://www.test.com/");
        var parent = uri.GetParentUri();
        parent.Should().BeNull();
    }
    
    [Fact]
    public void Can_Get_Uri_Parent_Root_NoSlash_Host()
    {
        var uri = new Uri("http://www.test.com");
        var parent = uri.GetParentUri();
        parent.Should().BeNull();
    }
    
    [Fact]
    public void Can_Get_Uri_Parent_Root_Slash_Host_QS()
    {
        var uri = new Uri("http://www.test.com/?q=1");
        var parent = uri.GetParentUri();
        parent.Should().BeNull();
    }
    
    [Fact]
    public void Can_Get_Uri_Parent_Root_NoSlash_Host_QS()
    {
        var uri = new Uri("http://www.test.com?q=1");
        var parent = uri.GetParentUri();
        parent.Should().BeNull();
    }
    
    //============
    
    
    [Fact]
    public void Can_Get_Uri_Slug_Slash()
    {
        var uri = new Uri("http://www.test.com/aa/bb/cc/");
        var slug = uri.GetSlug();
        slug.Should().Be("cc");
    }
    
    [Fact]
    public void Can_Get_Uri_Slug_NoSlash()
    {
        var uri = new Uri("http://www.test.com/aa/bb/cc");
        var slug = uri.GetSlug();
        slug.Should().Be("cc");
    }  
    
    [Fact]
    public void Can_Get_Uri_Slug_Host_Slash()
    {
        var uri = new Uri("http://www.test.com/");
        var slug = uri.GetSlug();
        slug.Should().BeNull();
    }
    
    [Fact]
    public void Can_Get_Uri_Slug_Host_NoSlash()
    {
        var uri = new Uri("http://www.test.com");
        var slug = uri.GetSlug();
        slug.Should().BeNull();
    }
    
}