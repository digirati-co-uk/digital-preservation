namespace DigitalPreservation.UI.Infrastructure.SharedState;

/// <summary>
/// Where the UI keeps the state a user's sign-in depends on: the data-protection keys that encrypt
/// its cookies, anti-forgery tokens and TempData, and the distributed cache behind the MSAL token
/// cache and session (issue #320). Kept somewhere shared, a redeploy no longer signs everyone out,
/// and the UI can run more than one task.
/// </summary>
/// <remarks>
/// The rest of the UI sees only ASP.NET Core's own abstractions (<c>IDistributedCache</c> and data
/// protection), so another installation can add a different <see cref="Store"/> - Redis, say -
/// in <see cref="SharedStateServiceCollectionX"/> alone.
/// <para>
/// Whatever the store, it is optional: if it is unconfigured or unavailable for any reason, the UI
/// falls back to keeping this state in memory, exactly as it did before, and logs a warning.
/// </para>
/// </remarks>
public class SharedStateOptions
{
    public const string SharedState = "SharedState";

    public const string MemoryStore = "Memory";
    public const string AwsStore = "Aws";

    /// <summary>"Memory" (the default: per-process, as before) or "Aws".</summary>
    public string Store { get; set; } = MemoryStore;

    /// <summary>
    /// Data protection's application discriminator. Every task of one UI deployment must share it;
    /// different deployments sharing a key store must not.
    /// </summary>
    public string ApplicationName { get; set; } = "dlip-preservation-ui";

    /// <summary>How long one call to the shared store may take before the UI gives up on it.</summary>
    public int TimeoutMilliseconds { get; set; } = 2000;

    /// <summary>After a failed call, how long the UI uses memory alone before trying the store again.</summary>
    public int RetryAfterSeconds { get; set; } = 30;

    public AwsSharedStateOptions Aws { get; set; } = new();
}

public class AwsSharedStateOptions
{
    /// <summary>DynamoDB table for the distributed cache. Partition key <see cref="PartitionKeyName"/>
    /// (string), with TTL enabled on <see cref="TtlAttributeName"/>.</summary>
    public string? CacheTableName { get; set; }

    public string PartitionKeyName { get; set; } = "id";

    public string TtlAttributeName { get; set; } = "expires";

    /// <summary>
    /// Secrets Manager name prefix for the data-protection keys, e.g.
    /// <c>dlip-pres-dev/preservation-ui/data-protection/</c>. Each key is its own secret, created once
    /// and never updated, so two tasks can't overwrite each other's keys.
    /// </summary>
    public string? KeySecretPrefix { get; set; }

    /// <summary>KMS key for the key secrets; Secrets Manager's default key if empty.</summary>
    public string? KmsKeyId { get; set; }
}
