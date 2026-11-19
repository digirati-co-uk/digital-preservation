using Amazon.S3;
using Amazon.S3.Model;
using DigitalPreservation.Mets;
using DigitalPreservation.XmlGen.Mets;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
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

    private static string GetMetsSample()
    {
        var metsFile = "Samples/mets-sample-001.xml";
        var metsXml = File.ReadAllText(metsFile);
        return metsXml;
    }

    private static string GetBadMetsSample()
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


    /// <summary>
    /// AmazonS3Client throws AmazonS3Exception for every non-2xx response rather than returning
    /// one - verified against a local HTTP listener answering 412 to a conditional PutObject -
    /// so the HttpStatusCode.PreconditionFailed check this used to simulate as a returned
    /// response never actually happens; it was dead code (issue #355). This exercises the real
    /// shape: a thrown exception with that status.
    /// </summary>
    [Fact]
    public async Task GetFullMets_PreconditionFailed()
    {
        var uri = new UriBuilder("s3://example.com/mets.xml")
        {
            Scheme = "s3"
        }.Uri;

        A.CallTo(() => s3Client.GetObjectAsync(
                A<GetObjectRequest>.Ignored, CancellationToken.None))
            .Throws(new AmazonS3Exception("Precondition Failed")
            {
                StatusCode = HttpStatusCode.PreconditionFailed
            });

        var result = await metsStorage.GetFullMets(uri, "s3");

        result.Failure.Should().BeTrue();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("PreconditionFailed");
    }

    /// <summary>
    /// As above: WriteMets's own PutObjectAsync call throws on a lost If-Match race rather than
    /// returning a 412 response, so the result must come from catching the exception, not from
    /// a status-code switch that never sees it (issue #355).
    /// </summary>
    [Fact]
    public async Task WriteMets_PreconditionFailed()
    {
        var fullMets = new FullMets
        {
            Mets = new Mets(),
            Uri = new UriBuilder("http://example.com/mets.xml") { Scheme = "s3" }.Uri,
            ETag = "\"some-etag\""
        };

        A.CallTo(() => s3Client.PutObjectAsync(A<PutObjectRequest>.Ignored, CancellationToken.None))
            .Throws(new AmazonS3Exception("Precondition Failed")
            {
                StatusCode = HttpStatusCode.PreconditionFailed
            });

        var result = await metsStorage.WriteMets(fullMets);

        result.Failure.Should().BeTrue();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("PreconditionFailed");
    }

    /// <summary>
    /// Issue #355 review: AWS's conditional-writes documentation says a concurrent PUT can also
    /// come back as 409 Conflict, not only 412 - and a concurrent delete of the same key can too.
    /// Both are the same "someone else won the race" outcome as 412 from this caller's point of
    /// view, so both must map to PreconditionFailed rather than escape as an unhandled exception.
    /// </summary>
    [Fact]
    public async Task WriteMets_Conflict_Maps_To_PreconditionFailed()
    {
        var fullMets = new FullMets
        {
            Mets = new Mets(),
            Uri = new UriBuilder("http://example.com/mets.xml") { Scheme = "s3" }.Uri,
            ETag = "\"some-etag\""
        };

        A.CallTo(() => s3Client.PutObjectAsync(A<PutObjectRequest>.Ignored, CancellationToken.None))
            .Throws(new AmazonS3Exception("Conflict")
            {
                StatusCode = HttpStatusCode.Conflict
            });

        var result = await metsStorage.WriteMets(fullMets);

        result.Failure.Should().BeTrue();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("PreconditionFailed");
    }

    /// <summary>
    /// Issue #355 review: GetFullMets already guards every S3 failure with a catch-all; WriteMets
    /// did not, so any AmazonS3Exception that wasn't specifically 412/409 (a throttle, a transient
    /// 5xx) escaped uncaught. No S3 failure should be able to escape WriteMets.
    /// </summary>
    [Fact]
    public async Task WriteMets_Other_S3_Exception_Does_Not_Escape()
    {
        var fullMets = new FullMets
        {
            Mets = new Mets(),
            Uri = new UriBuilder("http://example.com/mets.xml") { Scheme = "s3" }.Uri,
            ETag = "\"some-etag\""
        };

        A.CallTo(() => s3Client.PutObjectAsync(A<PutObjectRequest>.Ignored, CancellationToken.None))
            .Throws(new AmazonS3Exception("Slow down")
            {
                StatusCode = HttpStatusCode.ServiceUnavailable
            });

        var result = await metsStorage.WriteMets(fullMets);

        result.Failure.Should().BeTrue();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("UnknownError");
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
