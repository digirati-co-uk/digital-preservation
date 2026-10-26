using System.Net;
using System.Text;
using DigitalPreservation.Common.Model;
using FakeItEasy;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;
using Storage.Repository.Common;

namespace Storage.API.Tests.Fedora;

/// <summary>
/// Issue #388 item 2.1: FedoraClient.Delete used to hand-roll its own HttpStatusCode -&gt; ErrorCode
/// switch, covering only NotFound and Unauthorized and reporting everything else - including 403,
/// 410 and 412, which ErrorCodes.GetErrorCode already classifies correctly - as UnknownError. Delete
/// now gets its ErrorCode from GetErrorCode, the single source of truth also used by the S3 helpers.
/// </summary>
public class FedoraClientDeleteErrorCodeTests
{
    private static readonly Uri FedoraRoot = new("http://fedora.test/fcrepo/rest/");
    private const string StorageHost = "storage.test";

    private static readonly Transaction Transaction = new() { Location = new Uri(FedoraRoot, "tx:abc") };

    private static Binary MakeBinary() => new()
    {
        Id = new Uri($"https://{StorageHost}/repository/cc/thing/objects/page-001.jpg"),
        Digest = "abc123",
        ContentType = "image/jpeg",
        Origin = new Uri("s3://bucket/deposits/dep-1/objects/page-001.jpg")
    };

    private static FedoraClient BuildClient(HttpStatusCode responseStatus, string responseBody)
    {
        var handler = new FixedResponseHandler(responseStatus, responseBody);
        var httpClient = new HttpClient(handler, disposeHandler: false);
        var storage = A.Fake<IStorage>();

        var fedoraOptions = Options.Create(new FedoraOptions
        {
            Root = FedoraRoot,
            AdminUser = "admin",
            AdminPassword = "test-password",
            Bucket = "fedora-bucket",
            OcflS3Prefix = string.Empty
        });
        var converterOptions = Options.Create(new ConverterOptions { StorageRoot = new Uri($"https://{StorageHost}/") });
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

    [Theory]
    [InlineData(HttpStatusCode.NotFound, ErrorCodes.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized)]
    // Previously fell through Delete's own switch to UnknownError, even though GetErrorCode has
    // always classified these three correctly - the divergence issue #388 flags. Forbidden (403)
    // from Fedora is OUR OWN admin credentials being refused, not the caller's - UpstreamUnauthorized,
    // not Unauthorized (review feedback on #394).
    [InlineData(HttpStatusCode.Forbidden, ErrorCodes.UpstreamUnauthorized)]
    [InlineData(HttpStatusCode.Gone, ErrorCodes.Tombstone)]
    [InlineData(HttpStatusCode.UnprocessableEntity, ErrorCodes.Unprocessable)]
    [InlineData(HttpStatusCode.PreconditionFailed, ErrorCodes.PreconditionFailed)]
    [InlineData(HttpStatusCode.InternalServerError, ErrorCodes.UnknownError)]
    public async Task Delete_Maps_Fedora_Status_Through_GetErrorCode(HttpStatusCode fedoraStatus, string expectedErrorCode)
    {
        var client = BuildClient(fedoraStatus, "Fedora said no");

        var result = await client.Delete(MakeBinary(), "tester", Transaction, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(expectedErrorCode);
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
