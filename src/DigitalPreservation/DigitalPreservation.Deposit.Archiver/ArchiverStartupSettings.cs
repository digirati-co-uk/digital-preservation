namespace DigitalPreservation.Deposit.Archiver;

/// <summary>
/// Everything <see cref="ArchiverServiceCollectionX.AddArchiverServices"/> needs, gathered by
/// <see cref="Startup.ConfigureServices"/> from environment variables and Secrets Manager - kept
/// separate so the registrations themselves can be built and validated in a test without AWS
/// credentials or a real secret.
/// </summary>
public sealed record ArchiverStartupSettings(
    string? ScopeUri,
    string? ClientId,
    string? ClientSecret,
    string? TenantId,
    string? ResourceUri,
    string? ClientBaseAddress);
