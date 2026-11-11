using System.Text;
using DigitalPreservation.UI.Infrastructure.SharedState;
using FakeItEasy;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DigitalPreservation.UI.Tests.Infrastructure.SharedState;

/// <summary>Issue #320: a shared cache when it works, the old in-memory behaviour when it doesn't.</summary>
public class FallbackDistributedCacheTests
{
    private static readonly DistributedCacheEntryOptions Entry = new() { SlidingExpiration = TimeSpan.FromHours(1) };
    private static readonly byte[] Value = Encoding.UTF8.GetBytes("refresh token");

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static FallbackDistributedCache Cache(Func<IDistributedCache> primary, IDataProtectionProvider? keys = null,
        TimeProvider? clock = null, int timeoutMs = 2000) =>
        new(primary,
            keys ?? new EphemeralDataProtectionProvider(),
            Options.Create(new SharedStateOptions { TimeoutMilliseconds = timeoutMs, RetryAfterSeconds = 30 }),
            NullLogger<FallbackDistributedCache>.Instance,
            clock);

    private static MemoryDistributedCache SharedStore() => new(Options.Create(new MemoryDistributedCacheOptions()));

    private static IDistributedCache Broken()
    {
        var broken = A.Fake<IDistributedCache>();
        A.CallTo(broken).Throws(new InvalidOperationException("DynamoDB is down"));
        return broken;
    }

    [Fact]
    public async Task What_one_task_writes_another_reads()
    {
        var shared = SharedStore();
        var keys = new EphemeralDataProtectionProvider(); // both tasks share the key ring

        await Cache(() => shared, keys).SetAsync("k", Value, Entry);

        (await Cache(() => shared, keys).GetAsync("k")).Should().Equal(Value);
    }

    [Fact]
    public async Task Values_are_encrypted_in_the_shared_store()
    {
        var shared = SharedStore();
        await Cache(() => shared).SetAsync("k", Value, Entry);

        (await shared.GetAsync("k")).Should().NotBeNull().And.NotEqual(Value);
    }

    [Fact]
    public async Task A_value_written_under_other_keys_is_a_miss_not_an_error()
    {
        var shared = SharedStore();
        await Cache(() => shared, new EphemeralDataProtectionProvider()).SetAsync("k", Value, Entry);

        (await Cache(() => shared, new EphemeralDataProtectionProvider()).GetAsync("k")).Should().BeNull();
    }

    [Fact]
    public async Task When_the_store_fails_the_cache_behaves_as_memory_without_errors()
    {
        var cache = Cache(Broken);

        await cache.SetAsync("k", Value, Entry);
        (await cache.GetAsync("k")).Should().Equal(Value);
        cache.Get("k").Should().Equal(Value);
        await cache.RefreshAsync("k");
        await cache.RemoveAsync("k");
        (await cache.GetAsync("k")).Should().BeNull();
    }

    [Fact]
    public async Task A_store_that_cannot_be_built_is_an_outage_not_an_error()
    {
        var cache = Cache(() => throw new InvalidOperationException("no credentials"));

        await cache.SetAsync("k", Value, Entry);
        (await cache.GetAsync("k")).Should().Equal(Value);
    }

    [Fact]
    public async Task A_store_that_hangs_times_out_to_memory()
    {
        var hanging = A.Fake<IDistributedCache>();
        A.CallTo(() => hanging.GetAsync(A<string>._, A<CancellationToken>._))
            .Returns(new TaskCompletionSource<byte[]?>().Task);
        A.CallTo(() => hanging.SetAsync(A<string>._, A<byte[]>._, A<DistributedCacheEntryOptions>._, A<CancellationToken>._))
            .Returns(new TaskCompletionSource().Task);
        var cache = Cache(() => hanging, timeoutMs: 100);

        var started = DateTime.UtcNow;
        await cache.SetAsync("k", Value, Entry);
        (await cache.GetAsync("k")).Should().Equal(Value);

        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task After_a_failure_the_store_is_left_alone_until_the_retry_time()
    {
        var clock = new ManualClock();
        var broken = Broken();
        var cache = Cache(() => broken, clock: clock);

        await cache.GetAsync("k");
        await cache.GetAsync("k");
        await cache.SetAsync("k", Value, Entry);
        A.CallTo(broken).MustHaveHappenedOnceExactly();

        clock.Now += TimeSpan.FromSeconds(31);
        await cache.GetAsync("k");
        A.CallTo(broken).MustHaveHappenedTwiceExactly();
    }

    [Fact]
    public async Task Entries_written_during_an_outage_are_still_found_after_it()
    {
        var clock = new ManualClock();
        var shared = SharedStore();
        var down = true;
        var flaky = A.Fake<IDistributedCache>();
        A.CallTo(flaky).WithNonVoidReturnType().ReturnsLazily(call => down
            ? throw new InvalidOperationException("down")
            : call.Method.Invoke(shared, call.Arguments.ToArray()));
        var cache = Cache(() => flaky, clock: clock);

        await cache.SetAsync("k", Value, Entry);
        down = false;
        clock.Now += TimeSpan.FromSeconds(31);

        (await cache.GetAsync("k")).Should().Equal(Value);
    }
}
