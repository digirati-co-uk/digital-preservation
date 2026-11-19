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
/// Issue #347: FedoraClient.Rename replaces a resource's dc:title - on a Container itself, on a
/// Binary's fcr:metadata - inside the job's transaction, and reports failures by path under the
/// repository root, never the Storage API host.
/// </summary>
public class FedoraClientRenameTests
{
    private static readonly Uri FedoraRoot = new("http://fedora.test/fcrepo/rest/");
    private const string StorageHost = "storage.test";

    private static readonly Transaction Transaction = new() { Location = new Uri(FedoraRoot, "fcr:tx/abc") };

    private readonly RecordingHandler handler = new();

    [Fact]
    public async Task A_Binary_Is_Renamed_On_Its_Metadata_In_The_Transaction()
    {
        var binary = new Binary
        {
            Id = new Uri($"https://{StorageHost}/repository/cc/thing/objects/page-001.jpg"),
            Name = "Page one"
        };

        var result = await BuildClient(HttpStatusCode.NoContent).Rename(binary, "tester", Transaction, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Value.Should().BeSameAs(binary);
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Patch);
        request.Uri.Should().Be(new Uri(FedoraRoot, "cc/thing/objects/page-001.jpg/fcr:metadata"));
        request.TransactionHeader.Should().Be(Transaction.Location.ToString());
        request.Body.Should().Contain("DELETE").And.Contain("<> dc:title \"Page one\" .");
    }

    [Fact]
    public async Task A_Container_Is_Renamed_On_Itself()
    {
        var container = new Container
        {
            Id = new Uri($"https://{StorageHost}/repository/cc/thing/objects"),
            Name = "Digitised objects"
        };

        var result = await BuildClient(HttpStatusCode.NoContent).Rename(container, "tester", Transaction, CancellationToken.None);

        result.Success.Should().BeTrue();
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Uri.Should().Be(new Uri(FedoraRoot, "cc/thing/objects"));
        request.Body.Should().Contain("<> dc:title \"Digitised objects\" .");
    }

    [Fact]
    public async Task A_Fedora_Refusal_Is_Reported_By_Path_Under_Root()
    {
        var binary = new Binary
        {
            Id = new Uri($"https://{StorageHost}/repository/cc/thing/objects/page-001.jpg"),
            Name = "Page one"
        };

        var result = await BuildClient(HttpStatusCode.Conflict).Rename(binary, "tester", Transaction, CancellationToken.None);

        result.Failure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("cc/thing/objects/page-001.jpg");
        result.ErrorMessage.Should().NotContain(StorageHost);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task An_Empty_Name_Is_Refused_Without_Calling_Fedora(string? name)
    {
        var container = new Container { Id = new Uri($"https://{StorageHost}/repository/cc/thing/objects"), Name = name };

        var result = await BuildClient(HttpStatusCode.NoContent).Rename(container, "tester", Transaction, CancellationToken.None);

        result.Failure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BadRequest);
        handler.Requests.Should().BeEmpty();
    }

    private FedoraClient BuildClient(HttpStatusCode responseStatus)
    {
        handler.StatusCode = responseStatus;
        var httpClient = new HttpClient(handler, disposeHandler: false);
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
            A.Fake<IStorage>(),
            NullLogger<FedoraClient>.Instance,
            fedoraOptions,
            converters,
            new MemoryCache(new MemoryCacheOptions()),
            A.Fake<IStorageMapper>(),
            fedoraDB);
    }

    private record RecordedRequest(HttpMethod Method, Uri? Uri, string? TransactionHeader, string Body);

    private class RecordingHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.NoContent;
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var tx = request.Headers.TryGetValues(Transaction.HeaderName, out var values) ? values.Single() : null;
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri, tx, body));
            return new HttpResponseMessage(StatusCode) { Content = new StringContent("Fedora said so", Encoding.UTF8, "text/plain") };
        }
    }
}
