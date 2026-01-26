using Amazon.S3;
using Amazon.S3.Model;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.XmlGen.Mets;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Storage.Repository.Common.Mets;
using Storage.Repository.Common.Mets.StorageImpl;
using System.Net;
using System.Text;

namespace XmlGen.Tests;

public class S3MetsStorageTests
{
    private readonly IAmazonS3 s3Client;
    private readonly IMetsStorage metsStorage;

    public S3MetsStorageTests()
    {
        s3Client = A.Fake<IAmazonS3>();
        ILogger<MetsParser> logger1 = A.Fake<Logger<MetsParser>>();
        var loader1 = A.Fake<IMetsLoader>();
        IMetsParser metsParser1 = new MetsParser(loader1, logger1);
        metsStorage = new S3MetsStorage(s3Client, metsParser1);

    }

    public string GetMetsSample()
    {
        var metsFile = "Samples/mets-sample-001.xml";
        var metsXml = File.ReadAllText(metsFile);
        return metsXml;
    }

    public string GetBadMetsSample()
    {
        var goobiMetsFile = "Samples/goobi-wc-b29356350.xml";
        var metsXml = File.ReadAllText(goobiMetsFile);
        return metsXml;
    }

    [Fact]
    // Test that WriteMets returns failure when the URI scheme is not S3
    public async Task WriteMets_Not_S3()
    {
        var fullMets = new FullMets
        {
            Mets = A.Fake<Mets>(),
            Uri = new UriBuilder("http://example.com/mets.xml")
            {
                Scheme = "nots3"
            }.Uri
        };
        
        var result = await metsStorage.WriteMets(fullMets);

        result.Failure.Should().BeTrue();
        result.Success.Should().BeFalse();
    }


    [Fact]
    // Test that WriteMets returns failure when the Mets XML is saved to S3 unsuccessfully
    public async Task WriteMets_S3_Error()
    {
        var fullMets = new FullMets
        {
            Mets = new Mets(),
            Uri = new UriBuilder("http://example.com/mets.xml")
            {
                Scheme = "s3"
            }.Uri
        };

        A.CallTo(() => s3Client.PutObjectAsync(A<PutObjectRequest>.Ignored,
            CancellationToken.None)).Returns(Task.FromResult(new PutObjectResponse()
        {
            HttpStatusCode = HttpStatusCode.BadRequest,

        }));

        var result = await metsStorage.WriteMets(fullMets);

        result.Failure.Should().BeTrue();
        result.Success.Should().BeFalse();
    }


    [Fact]
    // Test that WriteMets returns success when the Mets XML is saved successfully to S3
    public async Task WriteMets_S3_Success()
    {
        var fullMets = new FullMets
        {
            Mets = new Mets(),
            Uri = new UriBuilder("http://example.com/mets.xml")
            {
                Scheme = "s3"
            }.Uri
        };

        A.CallTo(() => s3Client.PutObjectAsync(A<PutObjectRequest>.Ignored,
            CancellationToken.None)).Returns(Task.FromResult(new PutObjectResponse()
        {
            HttpStatusCode = HttpStatusCode.OK,

        }));

        var result = await metsStorage.WriteMets(fullMets);

        result.Failure.Should().BeFalse();
        result.Success.Should().BeTrue();
    }


    [Fact]
    public async Task GetFullMets_Not_s3()
    {
        var uri = new UriBuilder("s3://example.com/mets.xml")
        {
            Scheme = "nots3"
        }.Uri;

        var result = await metsStorage.GetFullMets(uri, null);

        result.Failure.Should().BeTrue();
        result.Success.Should().BeFalse();

    }

    [Fact]
    public async Task GetFullMets_Success()
    {
        var uri = new UriBuilder("s3://example.com/mets.xml")
        {
            Scheme = "s3"
        }.Uri;

        var metsXml = GetMetsSample();

        A.CallTo(() => s3Client.GetObjectAsync(
                A<GetObjectRequest>.Ignored, CancellationToken.None))
            .Returns(Task.FromResult(new GetObjectResponse()
            {
                HttpStatusCode = HttpStatusCode.OK,
                ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(metsXml))

            }));
        
        var result = await metsStorage.GetFullMets(uri, "s3");

        result.Failure.Should().BeFalse();
        result.Success.Should().BeTrue();

    }


    [Fact]
    public async Task GetFullMets_PreconditionFailed()
    {
        var uri = new UriBuilder("s3://example.com/mets.xml")
        {
            Scheme = "s3"
        }.Uri;

        var metsXml = GetMetsSample();

        A.CallTo(() => s3Client.GetObjectAsync(
                A<GetObjectRequest>.Ignored, CancellationToken.None))
            .Returns(Task.FromResult(new GetObjectResponse()
            {
                HttpStatusCode = HttpStatusCode.PreconditionFailed,
                ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(metsXml))

            }));

        var result = await metsStorage.GetFullMets(uri, "s3");

        result.Failure.Should().BeTrue();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("PreconditionFailed");

    }

    [Fact]
    public async Task GetFullMets_MetsCreatorAgent_NoMatch()
    {
        var uri = new UriBuilder("s3://example.com/mets.xml")
        {
            Scheme = "s3"
        }.Uri;

        var metsXml = GetBadMetsSample();

        A.CallTo(() => s3Client.GetObjectAsync(
                A<GetObjectRequest>.Ignored, CancellationToken.None))
            .Returns(Task.FromResult(new GetObjectResponse()
            {
                HttpStatusCode = HttpStatusCode.OK,
                ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(metsXml))

            }));

        var result = await metsStorage.GetFullMets(uri, "s3");

        result.Failure.Should().BeTrue();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("BadRequest");

    }
}
