using DigitalPreservation.Core.Auth;
using Storage.Repository.Common;

namespace Storage.API.Features.Export;

/// <summary>
/// The buckets the Storage API will export into: the platform's deposit buckets, meaning the default
/// working bucket plus every KnownClients profile's own DepositBucket. There is no separate allow-list.
/// The Storage API's caller is usually the Preservation API rather than the end caller, so this is
/// deliberately every configured deposit bucket, not just the caller's own (issue #288). Tightening it
/// to per-caller waits for #293. One place, so that /export and /exportMetsOnly can't drift apart.
/// </summary>
public static class PermittedExportBuckets
{
    public static List<string> All(AwsStorageOptions storageOptions, IClientDirectory clientDirectory) =>
        [storageOptions.DefaultWorkingBucket, .. clientDirectory.DepositBuckets];
}
