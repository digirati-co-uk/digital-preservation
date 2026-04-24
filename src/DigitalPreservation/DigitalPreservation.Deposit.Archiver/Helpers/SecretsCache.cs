using System.Text.Json;
using Amazon;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using DigitalPreservation.Deposit.Archiver;

namespace DigitalPreservation.Deposit.Archiver.Helpers;

public static class SecretsCache
{
    private static Task<AuthProviderModel>? _cachedSecret;

    public static Task<AuthProviderModel> GetAsync(
        string secretName,
        string region)
    {
        _cachedSecret ??= LoadAsync(secretName, region);
        return _cachedSecret;
    }

    private static async Task<AuthProviderModel> LoadAsync(
        string secretName,
        string region)
    {
        using var client =
            new AmazonSecretsManagerClient(
                RegionEndpoint.GetBySystemName(region));

        var response = await client.GetSecretValueAsync(
            new GetSecretValueRequest
            {
                SecretId = secretName,
                VersionStage = "AWSCURRENT"
            }).ConfigureAwait(false);

        return JsonSerializer.Deserialize<AuthProviderModel>(
                   response.SecretString!)
               ?? throw new InvalidOperationException("Invalid secret");
    }
}