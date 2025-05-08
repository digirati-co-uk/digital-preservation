using Amazon.S3.Util;
using Storage.Repository.Common.S3;

namespace Storage.API.Tests.S3;

public class BucketAndKeyTests
{
    [Fact]
    public void Get_Bucket_And_Key()
    {
        var uri = new Uri("s3://my-bucket/some/path/to/thing");
        var s3Uri = new AmazonS3Uri(uri);
        
        s3Uri.Bucket.Should().Be("my-bucket");
        s3Uri.Key.Should().Be("some/path/to/thing");
    }
    
    
    [Fact]
    public void Get_Bucket_And_Key_Using_Custom()
    {
        var uri = new Uri("s3://my-bucket/some/path/to/thing");
        var s3Uri = new AmazonS3Uri(uri);
        
        s3Uri.GetKeyFromLocalPath().Should().Be("some/path/to/thing");
    }
    
    [Fact]
    public void Get_Bucket_And_Key_Using_Custom_with_original()
    {
        var uri = new Uri("s3://my-bucket/some/path/to/thing");
        var s3Uri = new AmazonS3Uri(uri);
        
        s3Uri.GetKeyFromLocalPath(uri).Should().Be("some/path/to/thing");
    }
    
    [Fact]
    public void Get_Bucket_And_Key_Using_Custom_with_fragment()
    {
        var uri = new Uri("s3://my-bucket/some/path/to/thing%23with-fragment");
        var s3Uri = new AmazonS3Uri(uri);
        
        s3Uri.GetKeyFromLocalPath(uri).Should().Be("some/path/to/thing#with-fragment");
    }
    
    [Fact]
    public void Get_Bucket_And_Key_Using_Custom_with_fragment_and_spaces()
    {
        var uri = new Uri("s3://my-bucket/some/path/to/thing with %23 a-fragment");
        var s3Uri = new AmazonS3Uri(uri);
        
        s3Uri.GetKeyFromLocalPath(uri).Should().Be("some/path/to/thing with # a-fragment");
    }
    
    
    [Fact]
    public void Get_Bucket_And_Key_Using_Custom_with_char_mixes()
    {
        var uri = new Uri("s3://my-bucket/some/path/to/慷獹琠散敬牢瑡圣浯湥䡳獩潴祲潍瑮㼿䄠摮愠猠敮歡瀠敥瑡匠䍉敮⁷牡�.msg");
        var s3Uri = new AmazonS3Uri(uri);
        
        s3Uri.GetKeyFromLocalPath(uri).Should().Be("some/path/to/慷獹琠散敬牢瑡圣浯湥䡳獩潴祲潍瑮㼿䄠摮愠猠敮歡瀠敥瑡匠䍉敮⁷牡�.msg");
    }
    
    [Fact]
    public void Get_Bucket_And_Key_Using_Custom_with_emojis()
    {
        var uri = new Uri("s3://my-bucket/some/path/to/7 ways to celebrate %23WomensHistoryMonth 💜 And a sneak peek at SICK new art.htm");
        var s3Uri = new AmazonS3Uri(uri);
        
        s3Uri.GetKeyFromLocalPath(uri).Should().Be("some/path/to/7 ways to celebrate #WomensHistoryMonth 💜 And a sneak peek at SICK new art.htm");
    }
}