using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DigitalPreservation.UI.Infrastructure.SharedState;

/// <summary>
/// The UI's <see cref="IDistributedCache"/> (issue #320): a shared store when it works, and the
/// in-memory cache the UI always used when it doesn't. It never throws.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Every write goes to memory as well as the shared store, so if the store stops answering,
/// this task carries on with what it has, as before.</item>
/// <item>Reads prefer the shared store, which is what lets a second task, or the next deployment,
/// see a user's tokens and session. A miss there falls back to memory, which covers anything written
/// while the store was down.</item>
/// <item>Any failure or timeout makes the cache use memory alone for <c>RetryAfterSeconds</c>, so an
/// outage costs one timeout per period, not one per request.</item>
/// <item>Values in the shared store are encrypted with data protection: the MSAL cache holds refresh
/// tokens. A value that no longer decrypts (its key is gone) is a miss.</item>
/// </list>
/// </remarks>
public sealed class FallbackDistributedCache : IDistributedCache
{
    internal const string ProtectorPurpose = "DigitalPreservation.UI.SharedState.v1";

    private readonly Func<IDistributedCache> primaryFactory;
    private readonly IDataProtector protector;
    private readonly ILogger<FallbackDistributedCache> logger;
    private readonly TimeProvider clock;
    private readonly TimeSpan timeout;
    private readonly TimeSpan retryAfter;
    private readonly MemoryDistributedCache memory = new(Options.Create(new MemoryDistributedCacheOptions()));
    private readonly Lock gate = new();

    private IDistributedCache? primary;
    private DateTimeOffset? unavailableUntil;

    public FallbackDistributedCache(
        Func<IDistributedCache> primaryFactory,
        IDataProtectionProvider dataProtection,
        IOptions<SharedStateOptions> options,
        ILogger<FallbackDistributedCache> logger,
        TimeProvider? clock = null)
    {
        this.primaryFactory = primaryFactory;
        protector = dataProtection.CreateProtector(ProtectorPurpose);
        this.logger = logger;
        this.clock = clock ?? TimeProvider.System;
        timeout = TimeSpan.FromMilliseconds(Math.Max(1, options.Value.TimeoutMilliseconds));
        retryAfter = TimeSpan.FromSeconds(Math.Max(0, options.Value.RetryAfterSeconds));
    }

    public byte[]? Get(string key) => GetAsync(key).GetAwaiter().GetResult();

    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        var stored = await TryPrimary((p, ct) => p.GetAsync(key, ct), "read", token);
        if (stored is { Length: > 0 })
        {
            var value = Unprotect(stored, key);
            if (value != null)
            {
                return value;
            }
        }
        return await memory.GetAsync(key, token);
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        => SetAsync(key, value, options).GetAwaiter().GetResult();

    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options,
        CancellationToken token = default)
    {
        await memory.SetAsync(key, value, options, token);
        await TryPrimary(async (p, ct) => { await p.SetAsync(key, protector.Protect(value), options, ct); return true; },
            "write", token);
    }

    public void Refresh(string key) => RefreshAsync(key).GetAwaiter().GetResult();

    public async Task RefreshAsync(string key, CancellationToken token = default)
    {
        await memory.RefreshAsync(key, token);
        await TryPrimary(async (p, ct) => { await p.RefreshAsync(key, ct); return true; }, "refresh", token);
    }

    public void Remove(string key) => RemoveAsync(key).GetAwaiter().GetResult();

    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        await memory.RemoveAsync(key, token);
        await TryPrimary(async (p, ct) => { await p.RemoveAsync(key, ct); return true; }, "remove", token);
    }

    /// <summary>Runs one call against the shared store; default(T) if it is unavailable or fails.</summary>
    private async Task<T?> TryPrimary<T>(Func<IDistributedCache, CancellationToken, Task<T>> call, string operation,
        CancellationToken token)
    {
        bool wasUnavailable;
        lock (gate)
        {
            if (unavailableUntil > clock.GetUtcNow())
            {
                return default;
            }
            wasUnavailable = unavailableUntil != null;
        }

        try
        {
            primary ??= primaryFactory();
            // Cancel the call itself at the timeout, and also stop waiting for it then, in case it
            // doesn't honour cancellation.
            using var cts = new CancellationTokenSource(timeout, clock);
            await using var link = token.Register(cts.Cancel);
            var result = await call(primary, cts.Token).WaitAsync(timeout, clock, token);
            if (wasUnavailable)
            {
                lock (gate)
                {
                    unavailableUntil = null;
                }
                logger.LogInformation("The UI's shared state store is available again");
            }
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The caller gave up (the request was aborted): not the store's fault.
            return default;
        }
        catch (Exception ex)
        {
            bool firstFailure;
            lock (gate)
            {
                firstFailure = unavailableUntil == null;
                unavailableUntil = clock.GetUtcNow() + retryAfter;
            }
            if (firstFailure)
            {
                logger.LogWarning(ex,
                    "The UI's shared state store failed a {Operation}, so sign-in state is being kept in " +
                    "memory, as without a shared store. A redeploy will sign users out until it recovers. " +
                    "Retrying every {RetryAfter}.", operation, retryAfter);
            }
            else
            {
                logger.LogDebug(ex, "The UI's shared state store failed a {Operation} again", operation);
            }
            return default;
        }
    }

    private byte[]? Unprotect(byte[] stored, string key)
    {
        try
        {
            return protector.Unprotect(stored);
        }
        catch (CryptographicException ex)
        {
            // Written under a key this task can't read: treat as a miss, and the user signs in again.
            logger.LogDebug(ex, "Ignoring a shared cache entry that no longer decrypts ({Key})", key);
            return null;
        }
    }

}
