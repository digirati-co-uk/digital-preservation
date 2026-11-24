using System.Net;
using Storage.API.Fedora.Http;
using Storage.API.Tests.TestingInfrastructure;

namespace Storage.API.Tests.Fedora.Contract;

/// <summary>
/// How Fedora's transactions behave, as RFC 010 section 2 describes them from Fedora's source and
/// as the importer depends on: one transaction per import job, kept alive by a timer, committed in
/// one request, with its outcome confirmed afterwards (#350, #383). Run against every Fedora image
/// we deploy or evaluate (RFC 010 T14, ULIIIFCSS-45), so a difference in an upgrade shows up as a
/// failing test rather than a failed import.
/// </summary>
[Trait("Category", "Integration")]
public abstract class FedoraTransactionContractTests(FedoraFixture fedora) : FedoraContractTestBase(fedora)
{
    [Fact]
    public async Task A_New_Transaction_Reports_Itself_Open()
    {
        var transaction = await BeginTransaction();

        transaction.Should().StartWith(Fedora.Root + "fcr:tx/");
        using var status = await OnTransaction(HttpMethod.Get, transaction);
        status.StatusCode.Should().Be(HttpStatusCode.NoContent, "an open transaction answers 204 (RFC 010 2.6)");
    }

    [Fact]
    public async Task A_Write_Is_Invisible_Outside_Its_Transaction_Until_The_Commit()
    {
        var path = NewPath("isolation");
        var transaction = await BeginTransaction();

        (await PutContainer(path, transaction)).Should().Be(HttpStatusCode.Created);
        (await StatusOf(path, transaction)).Should().Be(HttpStatusCode.OK, "the transaction sees its own write");
        (await StatusOf(path)).Should().Be(HttpStatusCode.NotFound, "nothing outside sees it before the commit");

        (await Commit(transaction)).Should().Be(HttpStatusCode.NoContent);
        (await StatusOf(path)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_Committed_Transaction_Reports_Gone_As_Committed()
    {
        // #383 confirms an uncertain commit's outcome from this answer.
        var transaction = await BeginTransaction();
        (await PutContainer(NewPath("committed"), transaction)).Should().Be(HttpStatusCode.Created);
        (await Commit(transaction)).Should().Be(HttpStatusCode.NoContent);

        using var status = await OnTransaction(HttpMethod.Get, transaction);

        status.StatusCode.Should().Be(HttpStatusCode.Gone, "a committed transaction answers 410 (RFC 010 2.6)");
        (await status.Content.ReadAsStringAsync(Ct)).Should().ContainEquivalentOf("committed");
    }

    [Fact]
    public async Task Committing_Again_Answers_Gone_And_Says_It_Was_Committed()
    {
        // Not 204, as RFC 010 2.6 has it: a second commit PUT answers 410 like the status GET. The
        // outcome is in the body, which says "already been committed" (a failed one says rolled back).
        var transaction = await BeginTransaction();
        (await PutContainer(NewPath("recommit"), transaction)).Should().Be(HttpStatusCode.Created);
        (await Commit(transaction)).Should().Be(HttpStatusCode.NoContent);

        using var again = await OnTransaction(HttpMethod.Put, transaction);

        again.StatusCode.Should().Be(HttpStatusCode.Gone);
        (await again.Content.ReadAsStringAsync(Ct)).Should().Contain("already been committed");
    }

    [Fact]
    public async Task A_Rollback_Discards_The_Writes()
    {
        var path = NewPath("rollback");
        var transaction = await BeginTransaction();
        (await PutContainer(path, transaction)).Should().Be(HttpStatusCode.Created);

        using (var rollback = await OnTransaction(HttpMethod.Delete, transaction))
        {
            rollback.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await StatusOf(path)).Should().Be(HttpStatusCode.NotFound);
        using var status = await OnTransaction(HttpMethod.Get, transaction);
        status.StatusCode.Should().Be(HttpStatusCode.Gone, "a rolled-back transaction answers 410 (RFC 010 2.6)");
    }

    [Fact]
    public async Task A_Keep_Alive_Moves_The_Expiry_On()
    {
        var transaction = await BeginTransaction();
        var first = await KeepAlive(transaction);
        await Task.Delay(TimeSpan.FromSeconds(2), Ct);

        var second = await KeepAlive(transaction);

        second.Should().BeAfter(first, "POST /fcr:tx/{id} refreshes the expiry (RFC 010 2.3)");
    }

    [Fact]
    public async Task An_Idle_Transaction_Expires_After_The_Session_Timeout()
    {
        var path = NewPath("expired");
        var transaction = await BeginTransaction();
        (await PutContainer(path, transaction)).Should().Be(HttpStatusCode.Created);

        await Task.Delay(FedoraFixture.SessionTimeout + TimeSpan.FromSeconds(5), Ct);

        (await Commit(transaction)).Should().NotBe(HttpStatusCode.NoContent,
            "an expired transaction can't be committed (RFC 010 2.3, 2.4)");
        (await StatusOf(path)).Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Keep_Alives_Hold_A_Transaction_Open_Past_The_Session_Timeout()
    {
        // Why FedoraTransactionMonitor exists: one long binary upload would otherwise outlive it.
        var path = NewPath("kept-alive");
        var transaction = await BeginTransaction();
        (await PutContainer(path, transaction)).Should().Be(HttpStatusCode.Created);

        var until = DateTime.UtcNow + FedoraFixture.SessionTimeout * 1.5;
        while (DateTime.UtcNow < until)
        {
            await Task.Delay(FedoraFixture.SessionTimeout / 4, Ct);
            await KeepAlive(transaction);
        }

        (await Commit(transaction)).Should().Be(HttpStatusCode.NoContent);
        (await StatusOf(path)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_Refused_Digest_Fails_The_Whole_Transaction()
    {
        var path = NewPath("poisoned");
        var transaction = await BeginTransaction();
        (await PutContainer(path, transaction)).Should().Be(HttpStatusCode.Created);

        (await PutBinary($"{path}/file.txt", Bytes("hello"), transaction, digest: Sha256Hex(Bytes("goodbye"))))
            .Should().Be(HttpStatusCode.Conflict, "Fedora checks the Digest header at PUT time (RFC 010 2.1)");

        (await Commit(transaction)).Should().NotBe(HttpStatusCode.NoContent,
            "one failed request marks the transaction FAILED (RFC 010 2.1)");
        (await StatusOf(path)).Should().Be(HttpStatusCode.NotFound, "nothing from it is committed");
    }

    [Fact]
    public async Task A_Second_Transaction_Cannot_Change_An_Archival_Group_The_First_Is_Changing()
    {
        var group = NewPath("locked");
        (await PutContainer(group, archivalGroup: true, title: "Group")).Should().Be(HttpStatusCode.Created);
        var first = await BeginTransaction();
        var second = await BeginTransaction();

        (await PutContainer($"{group}/one", first)).Should().Be(HttpStatusCode.Created);
        (await PutContainer($"{group}/two", second)).Should().Be(HttpStatusCode.Conflict,
            "a write locks the whole Archival Group, and a conflict fails at once (RFC 010 2.2)");

        (await Commit(first)).Should().Be(HttpStatusCode.NoContent);
        (await StatusOf($"{group}/one")).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reads_Outside_See_The_Last_Committed_Version_While_A_Transaction_Is_Open()
    {
        var group = NewPath("reads");
        (await PutContainer(group, archivalGroup: true, title: "Before")).Should().Be(HttpStatusCode.Created);
        var transaction = await BeginTransaction();
        var rename = new HttpRequestMessage(HttpMethod.Patch, At(group)).AsReplaceTitlePatch("After", "tester");
        using (var response = await Send(rename, transaction))
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await Titles(group)).Should().Equal(["Before"], "a read outside takes no lock and sees the committed version (RFC 010 2.2)");
        (await Titles(group, transaction)).Should().Equal("After");

        (await Commit(transaction)).Should().Be(HttpStatusCode.NoContent);
        (await Titles(group)).Should().Equal("After");
    }

    [Fact]
    public async Task Each_Commit_Makes_One_New_Version_Of_An_Archival_Group()
    {
        // RFC 010 section 3: one transaction per import is the only way to get one OCFL version per import.
        var group = NewPath("versions");
        var first = await BeginTransaction();
        (await PutContainer(group, first, archivalGroup: true, title: "Group")).Should().Be(HttpStatusCode.Created);
        (await PutContainer($"{group}/objects", first)).Should().Be(HttpStatusCode.Created);
        (await PutBinary($"{group}/objects/a.txt", Bytes("a"), first)).Should().Be(HttpStatusCode.Created);
        (await PutBinary($"{group}/objects/b.txt", Bytes("b"), first)).Should().Be(HttpStatusCode.Created);
        (await Commit(first)).Should().Be(HttpStatusCode.NoContent);

        (await VersionCount(group)).Should().Be(1, "four writes in one transaction are one version");

        // Mementos are named to the second. Two commits in the same second make two OCFL versions
        // but Fedora lists one memento for them, so FedoraClient.MergeVersions would refuse to read
        // the group. Imports are further apart than that; this keeps the test to what an import does.
        await Task.Delay(TimeSpan.FromSeconds(1.1), Ct);
        var second = await BeginTransaction();
        (await PutBinary($"{group}/objects/c.txt", Bytes("c"), second)).Should().Be(HttpStatusCode.Created);
        var rename = new HttpRequestMessage(HttpMethod.Patch, At(group)).AsReplaceTitlePatch("Renamed", "tester");
        using (var response = await Send(rename, second))
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
        (await Commit(second)).Should().Be(HttpStatusCode.NoContent);

        (await VersionCount(group)).Should().Be(2);
    }

    private async Task<DateTimeOffset> KeepAlive(string transaction)
    {
        using var response = await OnTransaction(HttpMethod.Post, transaction);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.TryGetValues("Atomic-Expires", out var values).Should().BeTrue("a keep-alive reports the new expiry");
        return DateTimeOffset.Parse(values!.First());
    }
}

[Collection(Fedora651Collection.Name)]
public class FedoraTransactionContract651Tests(Fedora651Fixture fedora) : FedoraTransactionContractTests(fedora);

[Collection(Fedora700Collection.Name)]
[Trait("Category", "Manual")] // See Fedora700Collection
public class FedoraTransactionContract700Tests(Fedora700Fixture fedora) : FedoraTransactionContractTests(fedora);
