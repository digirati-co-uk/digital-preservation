using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Storage;
using FakeItEasy;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;
using Storage.API.Features.Repository.Requests;

namespace Storage.API.Tests.Features.Repository;

/// <summary>
/// Issue #387 (split from #244): GetResourceFromFedoraHandler only cached a built Archival Group
/// once the build finished, so every concurrent request for the same path - including a client
/// retrying after its own GET timed out - started its own full build. For a large group a cold
/// build is long (#244 measured over 600s for one with 2,796 files) and memory-hungry, so a client
/// that times out and retries made the problem worse, not better. These tests exercise the
/// coalescing added for that: one in-flight fetch per path, shared by every concurrent caller.
/// </summary>
public class GetResourceFromFedoraCoalescingTests
{
    private const string Path = "cc/thing";
    private static readonly Uri ResourceId = new("https://fedora.test/repository/cc/thing");

    private readonly IFedoraClient fedoraClient = A.Fake<IFedoraClient>();
    private readonly IMemoryCache memoryCache = new MemoryCache(new MemoryCacheOptions());

    private GetResourceFromFedoraHandler Handler() =>
        new(NullLogger<GetResourceFromFedoraHandler>.Instance, memoryCache, fedoraClient);

    private static Container NonArchivalGroupResource() => new() { Id = ResourceId };

    private static ArchivalGroup ArchivalGroupAt(string ocflVersion, string headVersion) => new()
    {
        Id = ResourceId,
        Version = new ObjectVersion { MementoTimestamp = "1", MementoDateTime = DateTime.UtcNow, OcflVersion = ocflVersion },
        StorageMap = new StorageMap
        {
            Version = new ObjectVersion { MementoTimestamp = "1", MementoDateTime = DateTime.UtcNow, OcflVersion = ocflVersion },
            StorageType = "S3",
            Root = "preservation",
            ObjectPath = "cc/thing",
            Files = new Dictionary<string, OriginFile>(),
            Hashes = new Dictionary<string, string>(),
            HeadVersion = new ObjectVersion { MementoTimestamp = "1", MementoDateTime = DateTime.UtcNow, OcflVersion = headVersion },
            AllVersions = [],
            ArchivalGroup = ResourceId
        }
    };

    [Fact]
    public async Task Two_Concurrent_Requests_For_The_Same_Path_Share_One_Fetch()
    {
        var tcs = new TaskCompletionSource<Result<PreservedResource?>>();
        A.CallTo(() => fedoraClient.GetResource(Path, A<Transaction?>._, A<CancellationToken>._))
            .ReturnsLazily(() => tcs.Task);
        var handler = Handler();

        // Neither call can complete synchronously - fedoraClient.GetResource won't resolve until
        // tcs is completed below - so by the time this line returns, Handle has already run as far
        // as the in-flight dictionary lookup for both calls: the second genuinely joins the first's
        // fetch rather than racing it.
        var task1 = handler.Handle(new GetResourceFromFedora(Path), CancellationToken.None);
        var task2 = handler.Handle(new GetResourceFromFedora(Path), CancellationToken.None);

        var resource = NonArchivalGroupResource();
        tcs.SetResult(Result.OkNotNull<PreservedResource?>(resource));

        var result1 = await task1;
        var result2 = await task2;

        result1.Value.Should().BeSameAs(resource);
        result2.Value.Should().BeSameAs(resource);
        A.CallTo(() => fedoraClient.GetResource(Path, A<Transaction?>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Cancelling_The_First_Callers_Token_Does_Not_Cancel_The_Second_Callers_Wait_Or_The_Fetch()
    {
        var tcs = new TaskCompletionSource<Result<PreservedResource?>>();
        A.CallTo(() => fedoraClient.GetResource(Path, A<Transaction?>._, A<CancellationToken>._))
            .ReturnsLazily(() => tcs.Task);
        var handler = Handler();
        var firstCallersCts = new CancellationTokenSource();

        var task1 = handler.Handle(new GetResourceFromFedora(Path), firstCallersCts.Token);
        var task2 = handler.Handle(new GetResourceFromFedora(Path), CancellationToken.None);

        firstCallersCts.Cancel();
        var act = async () => await task1;
        await act.Should().ThrowAsync<OperationCanceledException>(
            "the first caller's own wait must stop when its token is cancelled");

        // The underlying fetch - and the second caller's wait on it - must carry on regardless.
        var resource = NonArchivalGroupResource();
        tcs.SetResult(Result.OkNotNull<PreservedResource?>(resource));
        var result2 = await task2;

        result2.Value.Should().BeSameAs(resource);
        A.CallTo(() => fedoraClient.GetResource(Path, A<Transaction?>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task A_Joined_Result_Whose_Version_Is_No_Longer_Head_Is_Fetched_Again()
    {
        var tcs = new TaskCompletionSource<Result<PreservedResource?>>();
        var staleAg = ArchivalGroupAt("v1", "v1");
        var freshAg = ArchivalGroupAt("v2", "v2");
        A.CallTo(() => fedoraClient.GetResource(Path, A<Transaction?>._, A<CancellationToken>._))
            .ReturnsLazily(() => tcs.Task)
            .Once()
            .Then.Returns(Result.OkNotNull<PreservedResource?>(freshAg));
        // By the time the joiner reacts to the (stale) joined result, an import has moved HEAD on.
        A.CallTo(() => fedoraClient.GetArchivalGroupVersion(Path, A<Transaction?>._))
            .Returns(Result.OkNotNull(new ObjectVersion { MementoTimestamp = "2", MementoDateTime = DateTime.UtcNow, OcflVersion = "v2" }));
        var handler = Handler();

        var leader = handler.Handle(new GetResourceFromFedora(Path), CancellationToken.None);
        var joiner = handler.Handle(new GetResourceFromFedora(Path), CancellationToken.None);

        tcs.SetResult(Result.OkNotNull<PreservedResource?>(staleAg));
        var leaderResult = await leader;
        var joinerResult = await joiner;

        leaderResult.Value.Should().BeSameAs(staleAg,
            "the leader just started its own fresh fetch, so it has no reason to doubt the result");
        joinerResult.Value.Should().BeSameAs(freshAg,
            "the joiner's copy was already stale by the time it reacted, so it must fetch again rather than hand back v1");
        A.CallTo(() => fedoraClient.GetResource(Path, A<Transaction?>._, A<CancellationToken>._))
            .MustHaveHappened(2, Times.Exactly);
    }

    [Fact]
    public async Task A_Failed_Fetch_Is_Not_Left_In_The_InFlight_Map()
    {
        A.CallTo(() => fedoraClient.GetResource(Path, A<Transaction?>._, A<CancellationToken>._))
            .Returns(Result.FailNotNull<PreservedResource?>(ErrorCodes.NotFound, "not found"));
        var handler = Handler();

        var first = await handler.Handle(new GetResourceFromFedora(Path), CancellationToken.None);
        var second = await handler.Handle(new GetResourceFromFedora(Path), CancellationToken.None);

        first.Success.Should().BeFalse();
        second.Success.Should().BeFalse();
        // Two genuinely separate Handle calls, run one after the other (not concurrently): if the
        // first fetch's entry were still in the map, the second call would join its already-complete
        // task rather than trying again - indistinguishable here from a fresh attempt by the return
        // value alone, so this asserts on the call count instead.
        A.CallTo(() => fedoraClient.GetResource(Path, A<Transaction?>._, A<CancellationToken>._))
            .MustHaveHappened(2, Times.Exactly);
    }
}
