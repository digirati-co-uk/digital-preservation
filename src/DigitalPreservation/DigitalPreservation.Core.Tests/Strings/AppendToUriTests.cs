using DigitalPreservation.Utils;

namespace DigitalPreservation.Core.Tests.Strings;

public class AppendToUriTests
{
    
    
    [Fact]
    public void Simple_Append_Slug_1()
    {
        var uri = new Uri("http://www.test.com/aa/bb/cc");
        var slug = "slug";
        var uri2 = uri.AppendEscapedSlug(slug);
        uri2.ToString().Should().Be("http://www.test.com/aa/bb/cc/slug");
    }
    
    [Fact]
    public void Simple_Append_Slug_2()
    {
        var uri = new Uri("http://www.test.com/aa/bb/cc/");
        var slug = "slug";
        var uri2 = uri.AppendEscapedSlug(slug);
        uri2.ToString().Should().Be("http://www.test.com/aa/bb/cc/slug");
    }
    
    [Fact]
    public void Simple_Append_Slug_3()
    {
        var uri = new Uri("http://www.test.com/aa/bb/cc/");
        var slug = "/slug";
        var uri2 = uri.AppendEscapedSlug(slug);
        uri2.ToString().Should().Be("http://www.test.com/aa/bb/cc/slug");
    }
    
    [Fact]
    public void Simple_Append_Slug_4()
    {
        var uri = new Uri("http://www.test.com/aa/bb/cc");
        var slug = "/slug";
        var uri2 = uri.AppendEscapedSlug(slug);
        uri2.ToString().Should().Be("http://www.test.com/aa/bb/cc/slug");
    }
    
    
    [Fact]
    public void Complex_Append_Slug_1()
    {
        var uri = new Uri("http://www.test.com/aa/bb/cc");
        var slug = "花园里的猫";
        var uri2 = uri.AppendEscapedSlug(slug);
        uri2.ToString().Should().Be("http://www.test.com/aa/bb/cc/花园里的猫");
    }

    
    [Fact]
    public void Complex_Append_Slug_2()
    {
        var uri = new Uri("http://www.test.com/aa/bb/cc");
        var slug = "花园里 💜 的猫";
        var uri2 = uri.AppendEscapedSlug(slug);
        uri2.ToString().Should().Be("http://www.test.com/aa/bb/cc/花园里 💜 的猫");
    }
    
    
    [Fact]
    public void Complex_Append_Slug_Hash()
    {
        var uri = new Uri("http://www.test.com/aa/bb/cc");
        var slug = "this has a # in it";
        var uri2 = uri.AppendEscapedSlug(slug);
        uri2.ToString().Should().Be("http://www.test.com/aa/bb/cc/this has a # in it");
    }
    
    
    [Fact]
    public void Complex_Append_Slug_Hash_Escaped()
    {
        var uri = new Uri("http://www.test.com/aa/bb/cc");
        var slug = "this has a # in it";
        var escapedSlug = Uri.EscapeDataString(slug);
        var uri2 = uri.AppendEscapedSlug(escapedSlug);
        uri2.ToString().Should().Be("http://www.test.com/aa/bb/cc/this has a %23 in it");
    }
}