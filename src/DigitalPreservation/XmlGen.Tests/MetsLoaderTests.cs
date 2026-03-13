using Amazon.Runtime;
using Amazon.Runtime.SharedInterfaces;
using Amazon.S3;
using Amazon.S3.Model;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.XmlGen.Mets;
using FakeItEasy;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.DataCollection;
using Storage.Repository.Common.Mets;
using Storage.Repository.Common.Mets.StorageImpl;
using System.Net;
using System.Text;

namespace XmlGen.Tests;

public class MetsLoaderTests
{
    private readonly IAmazonS3 s3Client;
    private readonly IMetsLoader metsLoader;


    public MetsLoaderTests()
    {
        s3Client = A.Fake<IAmazonS3>();
        metsLoader = new S3MetsLoader(s3Client);
    }


    public string GetMetsSample()
    {
        var metsFile = "Samples/mets-sample-001.xml";
        var metsXml = File.ReadAllText(metsFile);
        return metsXml;
    }


    [Fact]
    public async Task ExamineXml_Load()
    {
        var uri = new UriBuilder("s3://example.com/mets.xml")
        {
            Scheme = "s3"
        }.Uri;

        var metsXml = GetMetsSample();

        A.CallTo(() => s3Client.GetObjectAsync(
                A<string>.Ignored,
                A<string>.Ignored,
                CancellationToken.None))
            .Returns(Task.FromResult(new GetObjectResponse()
            {
                HttpStatusCode = HttpStatusCode.OK,
                ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(metsXml))

            }));

        var (xDoc, s3ETag) = await metsLoader.ExamineXml(uri, null, true);

        xDoc.Should().NotBeNull();
        s3ETag.Should().BeNull();
    }

    [Fact]
    public async Task LoadMetsFileAsWorkingFile_Throw()
    {
        var uri = new UriBuilder("s3://example.com/mets.xml")
        {
            Scheme = "s3"
        }.Uri;

        A.CallTo(() => s3Client.GetObjectMetadataAsync(
                A<GetObjectMetadataRequest>.Ignored,
                CancellationToken.None))
            .Throws(new AmazonS3Exception("Some S3 error"));

        var act = async () => { await metsLoader.LoadMetsFileAsWorkingFile(uri); };

        await act.Should().ThrowAsync<AmazonS3Exception>();

    }

    [Fact]
    public async Task LoadMetsFileAsWorkingFile_NotFound()
    {
        var uri = new UriBuilder("s3://example.com/mets.xml")
        {
            Scheme = "s3"
        }.Uri;

        A.CallTo(() => s3Client.GetObjectMetadataAsync(
                A<GetObjectMetadataRequest>.Ignored,
                CancellationToken.None))
            .Throws(
                new AmazonS3Exception(
                    "not found", new AmazonS3Exception("not found"),
                    ErrorType.Receiver, "404 ",
                    "123456", HttpStatusCode.NotFound, "12345"));


        var result = await metsLoader.LoadMetsFileAsWorkingFile(uri);

        result.Should().BeNull();

    }


    [Fact]
    public async Task LoadMetsFileAsWorkingFile_Sucess()
    {
        var uri = new UriBuilder("s3://example.com/mets.xml")
        {
            Scheme = "s3"
        }.Uri;

        var metsXml = GetMetsSample();

        A.CallTo(() => s3Client.GetObjectMetadataAsync(
                A<GetObjectMetadataRequest>.Ignored,
                CancellationToken.None))
            .Returns(Task.FromResult(new GetObjectMetadataResponse()
            {
                HttpStatusCode = HttpStatusCode.OK,
                ETag = "123456"
            }));


        var fakeS3 = A.Fake<ICoreAmazonS3>();
        var expectedStream = new MemoryStream(Encoding.UTF8.GetBytes(metsXml));
        A.CallTo(() => s3Client.GetObjectStreamAsync(
                A<string>.Ignored,
                A<string>.Ignored,
                A<System.Collections.Generic.IDictionary<string, object>>.Ignored,
                A<CancellationToken>.Ignored))
            .Returns(Task.FromResult<Stream>(expectedStream));


        var result = await metsLoader.LoadMetsFileAsWorkingFile(uri);

        result.Should().NotBeNull();
        result!.Name.Should().Be("mets.xml");
        result.ContentType.Should().Be("application/xml");


    }

    [Fact]
    public async Task FindMetsFile_NotS3()
    {
        var uri = new UriBuilder("http://example.com/mets.xml")
        {
            Scheme = "http"
        }.Uri;
        var act = async () => { await metsLoader.FindMetsFile(uri); };

        await act.Should().ThrowAsync<NotSupportedException>();

    }

    [Fact]
    public async Task FindMetsFile_S3()
    {
        var uri = new UriBuilder("s3://example.com/mets.xml")
        {
            Scheme = "s3"
        }.Uri;

        A.CallTo(() => s3Client.ListObjectsV2Async(
                A<ListObjectsV2Request>.Ignored,
                CancellationToken.None))
            .Returns(Task.FromResult(new ListObjectsV2Response()
            {
                S3Objects = new System.Collections.Generic.List<S3Object>
                {
                    new S3Object
                    {
                        BucketName = "example.com",
                        Key = "mets.xml"
                    }
                },
                HttpStatusCode = HttpStatusCode.OK
            }));

        var result = await metsLoader.FindMetsFile(uri);
        result.Should().NotBeNull();
        

    }


    [Fact]
    public async Task FindMetsFile_S3_NotFound()
    {
        var uri = new UriBuilder("s3://example.com/mets.xml")
        {
            Scheme = "s3"
        }.Uri;

        A.CallTo(() => s3Client.ListObjectsV2Async(
                A<ListObjectsV2Request>.Ignored,
                CancellationToken.None))
            .Returns(Task.FromResult(new ListObjectsV2Response()
            {
                S3Objects = new System.Collections.Generic.List<S3Object>
                {
                },
                HttpStatusCode = HttpStatusCode.OK
            }));

        var result = await metsLoader.FindMetsFile(uri);
        result.Should().BeNull();

    }

}