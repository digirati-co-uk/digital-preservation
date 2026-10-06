using System.Net;
using System.Text;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using FakeItEasy;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;
using Storage.Repository.Common;

namespace Storage.API.Tests.Fedora;

/// <summary>
/// September 2026 security review, issue #292 item 3: when Fedora refuses a binary PUT because the
/// streamed bytes don't match the Digest header FedoraClient sent, the resulting failed Result must
/// name the binary - ExecuteImportJob reports fedoraPutBinaryResult.CodeAndMessage() verbatim as the
/// import job's own failure message, and without a name a caller can't tell which file was wrong.
/// </summary>
public class FedoraClientDigestRefusalTests
{
    private static readonly Uri FedoraRoot = new("http://fedora.test/fcrepo/rest/");

    private static FedoraClient BuildClient(HttpMessageHandler handler, Stream content)
    {
        var httpClient = new HttpClient(handler, disposeHandler: false);
        var storage = A.Fake<IStorage>();
        A.CallTo(() => storage.GetStream(A<Uri>._))
            .Returns(Result.OkNotNull<(Stream?, DateTime)>((content, DateTime.UtcNow)));

        var fedoraOptions = Options.Create(new FedoraOptions
        {
            Root = FedoraRoot,
            AdminUser = "admin",
            AdminPassword = "test-password",
            Bucket = "fedora-bucket",
            OcflS3Prefix = string.Empty
        });
        var converterOptions = Options.Create(new ConverterOptions { StorageRoot = new Uri("https://storage.test/") });
        var converters = new Converters(fedoraOptions, converterOptions);
        var fedoraDB = new FedoraDB(converters, null, NullLogger<FedoraDB>.Instance);

        return new FedoraClient(
            httpClient,
            storage,
            NullLogger<FedoraClient>.Instance,
            fedoraOptions,
            converters,
            new MemoryCache(new MemoryCacheOptions()),
            A.Fake<IStorageMapper>(),
            fedoraDB);
    }

    // Deliberately in a sub-folder: a bare file name like page-001.jpg is ambiguous in any deposit
    // with more than one volume, so the message must carry the binary's whole path.
    private static Binary MakeBinary() => new()
    {
        Id = new Uri("https://storage.test/repository/cc/thing/objects/vol2/page-001.jpg"),
        Digest = "abc123",
        ContentType = "image/jpeg",
        Origin = new Uri("s3://bucket/deposits/dep-1/objects/vol2/page-001.jpg")
    };

    private static readonly Transaction Transaction = new() { Location = new Uri(FedoraRoot, "tx:abc") };

    [Fact]
    public async Task A_Digest_Refusal_From_Fedora_Names_The_Binary_In_The_Failure_Message()
    {
        var handler = new FixedResponseHandler(HttpStatusCode.Conflict, "Checksum Mismatch");
        var client = BuildClient(handler, new MemoryStream([1, 2, 3]));

        var result = await client.PutBinary(MakeBinary(), "tester", Transaction, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("cc/thing/objects/vol2/page-001.jpg",
            "the import job result reports this message verbatim, so it must say which file was wrong");
    }

    [Fact]
    public async Task A_Fedora_Computed_Checksum_That_Differs_From_Ours_Names_The_Binary_And_Both_Digests()
    {
        // Fedora accepts the PUT, but the digest it computed and reports in the binary's metadata
        // isn't ours: PutBinary's own post-PUT comparison is the second way wrong bytes are caught.
        var metadata = $$"""
            {
              "@id": "{{FedoraRoot}}cc/thing/objects/vol2/page-001.jpg",
              "@type": ["fedora:Binary"],
              "title": "page-001.jpg",
              "hasMessageDigest": "urn:sha-256:fff999",
              "hasSize": "3",
              "hasMimeType": "image/jpeg"
            }
            """;
        var handler = new PutThenMetadataHandler(metadata);
        var client = BuildClient(handler, new MemoryStream([1, 2, 3]));

        var result = await client.PutBinary(MakeBinary(), "tester", Transaction, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("cc/thing/objects/vol2/page-001.jpg")
            .And.Contain("abc123").And.Contain("fff999");
    }

    /// <summary>Answers the binary PUT with 201 and every GET with the given fcr:metadata JSON-LD.</summary>
    private class PutThenMetadataHandler(string metadataJsonLd) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(request.Method == HttpMethod.Put
                ? new HttpResponseMessage(HttpStatusCode.Created)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(metadataJsonLd, Encoding.UTF8, "application/ld+json")
                });
    }

    private class FixedResponseHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/plain")
            });
    }
}
