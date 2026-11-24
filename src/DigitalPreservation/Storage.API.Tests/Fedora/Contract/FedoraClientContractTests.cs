using System.Net;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using FakeItEasy;
using Storage.API.Fedora.Vocab;
using Storage.API.Tests.TestingInfrastructure;
using Storage.Repository.Common;

namespace Storage.API.Tests.Fedora.Contract;

/// <summary>
/// The Storage API's own FedoraClient and FedoraDB against a real Fedora: the calls an import job
/// makes, and the reads behind the repository, browse and search. What these exercise that the raw
/// contract tests don't is our side - the JSON-LD converters, and the SQL we run against Fedora's
/// tables - which RFC 010 section 9 lists as unverified for Fedora 7 (its extra triples, its
/// migrated schema).
/// </summary>
[Trait("Category", "Integration")]
public abstract class FedoraClientContractTests(FedoraFixture fedora) : FedoraContractTestBase(fedora)
{
    private const string Caller = "contract-tester";

    [Fact]
    public async Task An_Import_Creates_An_Archival_Group_With_A_Container_And_A_Binary()
    {
        var group = NewPath("import");
        var bytes = Bytes("page one");
        var client = Fedora.CreateClient(StorageServing(bytes));
        var transaction = await client.BeginTransaction();

        (await client.CreateArchivalGroup(group, Caller, "An archival group", transaction, Ct))
            .Success.Should().BeTrue();
        (await client.CreateContainerWithinArchivalGroup($"{group}/objects", Caller, "Objects", transaction, Ct))
            .Success.Should().BeTrue();
        var put = await client.PutBinary(NewBinary($"{group}/objects/page-1.txt", bytes, "Page one"), Caller, transaction, Ct);
        put.Success.Should().BeTrue(put.ErrorMessage);
        await client.CommitTransaction(transaction, Ct);

        transaction.CommitStatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetResourceType(group)).Value.Should().Be(nameof(ArchivalGroup));

        var archivalGroup = (await client.GetResourceLightweight(group, null)).Value;
        archivalGroup.Should().BeOfType<ArchivalGroup>().Which.Name.Should().Be("An archival group");

        var objects = (await client.GetResourceLightweight($"{group}/objects", null)).Value;
        objects.Should().BeOfType<Container>().Which.Name.Should().Be("Objects");

        var binary = (await client.GetResourceLightweight($"{group}/objects/page-1.txt", null)).Value
            .Should().BeOfType<Binary>().Subject;
        binary.Name.Should().Be("Page one");
        binary.Digest.Should().Be(Sha256Hex(bytes));
        binary.Size.Should().Be(bytes.Length);
        binary.ContentType.Should().Be("text/plain");
    }

    [Fact]
    public async Task A_Binary_Whose_Digest_Fedora_Disagrees_With_Is_Refused()
    {
        var group = NewPath("bad-digest");
        var bytes = Bytes("what was uploaded");
        var client = Fedora.CreateClient(StorageServing(bytes));
        var transaction = await client.BeginTransaction();
        (await client.CreateArchivalGroup(group, Caller, "Group", transaction, Ct)).Success.Should().BeTrue();

        var binary = NewBinary($"{group}/file.txt", bytes, "File");
        binary.Digest = Sha256Hex(Bytes("what the deposit said"));
        var put = await client.PutBinary(binary, Caller, transaction, Ct);

        put.Failure.Should().BeTrue();
        await client.CommitTransaction(transaction, Ct);
        transaction.CommitStatusCode.Should().NotBe(HttpStatusCode.NoContent);
        (await StatusOf(group)).Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_Deleted_Binary_Leaves_A_Tombstone()
    {
        var group = NewPath("tombstone");
        var bytes = Bytes("to be deleted");
        var client = Fedora.CreateClient(StorageServing(bytes));
        var create = await client.BeginTransaction();
        (await client.CreateArchivalGroup(group, Caller, "Group", create, Ct)).Success.Should().BeTrue();
        var binary = NewBinary($"{group}/file.txt", bytes, "File");
        (await client.PutBinary(binary, Caller, create, Ct)).Success.Should().BeTrue();
        await client.CommitTransaction(create, Ct);

        var delete = await client.BeginTransaction();
        (await client.Delete(binary, Caller, delete, Ct)).Success.Should().BeTrue();
        await client.CommitTransaction(delete, Ct);

        delete.CommitStatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetResourceType($"{group}/file.txt")).Value.Should().Be(RepositoryTypes.Tombstone);
        (await client.GetResource($"{group}/file.txt")).ErrorCode.Should().Be(ErrorCodes.Tombstone);
    }

    [Fact]
    public async Task A_Container_Is_Read_With_Its_Children()
    {
        // Through WithContainedDescriptions: one JSON-LD @graph, parsed by our converters.
        var parent = NewPath("children");
        var bytes = Bytes("a child file");
        var client = Fedora.CreateClient(StorageServing(bytes));
        (await client.CreateContainer(parent, Caller, "Parent", cancellationToken: Ct)).Success.Should().BeTrue();
        (await client.CreateContainer($"{parent}/child", Caller, "Child", cancellationToken: Ct)).Success.Should().BeTrue();
        var transaction = await client.BeginTransaction();
        (await client.PutBinary(NewBinary($"{parent}/file.txt", bytes, "File"), Caller, transaction, Ct))
            .Success.Should().BeTrue();
        await client.CommitTransaction(transaction, Ct);

        var container = (await client.GetResource(parent)).Value.Should().BeOfType<Container>().Subject;

        container.Name.Should().Be("Parent");
        container.Containers.Should().ContainSingle().Which.Name.Should().Be("Child");
        var file = container.Binaries.Should().ContainSingle().Subject;
        file.Name.Should().Be("File");
        file.Digest.Should().Be(Sha256Hex(bytes));
        file.Size.Should().Be(bytes.Length);
    }

    [Fact]
    public async Task A_Wide_Container_Is_Read_From_Fedoras_Database()
    {
        // FedoraClient reads a container with 20 or more children from Fedora's tables (FedoraDB),
        // which must find our Archival Group type there: make one first.
        var client = Fedora.CreateClient(StorageServing(Bytes("x")));
        var setup = await client.BeginTransaction();
        (await client.CreateArchivalGroup(NewPath("type-seed"), Caller, "Seed", setup, Ct)).Success.Should().BeTrue();
        await client.CommitTransaction(setup, Ct);

        var parent = NewPath("wide");
        (await PutContainer(parent)).Should().Be(HttpStatusCode.Created);
        for (var i = 0; i < 20; i++)
        {
            (await PutBinary($"{parent}/file-{i:D2}.txt", Bytes($"file {i:D2}"))).Should().Be(HttpStatusCode.Created);
        }

        var db = Fedora.CreateFedoraDb();
        db.Available.Should().BeTrue("FedoraDB reads Fedora's search_rdf_type table");
        var fromDb = await db.GetPopulatedContainer(At(parent));

        fromDb!.Binaries.Should().HaveCount(20);
        fromDb.Binaries.Should().OnlyContain(b => b.ContentType == "text/plain" && b.Size == "file 00".Length);

        var container = (await Fedora.CreateClient(withDatabase: true).GetResource(parent)).Value
            .Should().BeOfType<Container>().Subject;
        container.Binaries.Should().HaveCount(20);
    }

    [Fact]
    public async Task Search_Finds_A_Committed_Binary_By_Its_Path()
    {
        var token = Guid.NewGuid().ToString("N");
        var parent = NewPath("search");
        (await PutContainer(parent)).Should().Be(HttpStatusCode.Created);
        var bytes = Bytes("searchable");
        (await PutBinary($"{parent}/found-{token}.txt", bytes)).Should().Be(HttpStatusCode.Created);

        var db = Fedora.CreateFedoraDb();

        (await db.GetSearchCount(token)).Should().Be(1);
        var hit = (await db.GetSimpleSearch(token)).Should().ContainSingle().Subject;
        hit.FedoraId.Should().Contain(token);
        hit.MimeType.Should().Be("text/plain");
        hit.ContentSize.Should().Be(bytes.Length);
    }

    [Fact]
    public async Task The_Health_Check_Path_Answers()
    {
        // The Storage API's IsAlive, and the deployed health check, call fcr:systeminfo.
        using var response = await Fedora.Client.GetAsync(new Uri(Fedora.Root + "fcr:systeminfo"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static Binary NewBinary(string path, byte[] bytes, string name) => new()
    {
        Id = new Uri(FedoraFixture.StorageRoot, $"repository/{path}"),
        Origin = new Uri($"s3://deposit-bucket/{path}"),
        Digest = Sha256Hex(bytes),
        ContentType = "text/plain",
        Name = name,
        Size = bytes.Length
    };

    /// <summary>A deposit bucket that serves <paramref name="bytes"/> for any file.</summary>
    private static IStorage StorageServing(byte[] bytes)
    {
        var storage = A.Fake<IStorage>();
        A.CallTo(() => storage.GetStream(A<Uri>._))
            .ReturnsLazily(() => Task.FromResult(
                Result.Ok<(Stream?, DateTime)>((new MemoryStream(bytes), DateTime.UtcNow))));
        return storage;
    }
}

[Collection(Fedora651Collection.Name)]
public class FedoraClientContract651Tests(Fedora651Fixture fedora) : FedoraClientContractTests(fedora);

[Collection(Fedora700Collection.Name)]
[Trait("Category", "Manual")] // See Fedora700Collection
public class FedoraClientContract700Tests(Fedora700Fixture fedora) : FedoraClientContractTests(fedora);
