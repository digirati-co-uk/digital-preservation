using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using FakeItEasy;

namespace DigitalPreservation.UI.Tests.Infrastructure.SharedState;

/// <summary>An in-memory Secrets Manager for the calls the key repository makes. Set
/// <see cref="Down"/> to make every call fail, as an outage or missing permission would.</summary>
public sealed class FakeSecretsManager
{
    public Dictionary<string, string> Secrets { get; } = new(StringComparer.Ordinal);
    public bool Down { get; set; }
    public int Creates { get; private set; }
    public IAmazonSecretsManager Client { get; } = A.Fake<IAmazonSecretsManager>();

    public FakeSecretsManager()
    {
        A.CallTo(() => Client.CreateSecretAsync(A<CreateSecretRequest>._, A<CancellationToken>._))
            .ReturnsLazily((CreateSecretRequest r, CancellationToken _) =>
            {
                ThrowIfDown();
                Creates++;
                if (!Secrets.TryAdd(r.Name, r.SecretString))
                {
                    throw new ResourceExistsException("exists");
                }
                return Task.FromResult(new CreateSecretResponse { Name = r.Name });
            });
        A.CallTo(() => Client.ListSecretsAsync(A<ListSecretsRequest>._, A<CancellationToken>._))
            .ReturnsLazily((ListSecretsRequest r, CancellationToken _) =>
            {
                ThrowIfDown();
                var prefix = r.Filters.Single().Values.Single();
                return Task.FromResult(new ListSecretsResponse
                {
                    // Like the real filter: a case-insensitive prefix match.
                    SecretList = Secrets.Keys
                        .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        .Select(n => new SecretListEntry { Name = n })
                        .ToList()
                });
            });
        A.CallTo(() => Client.GetSecretValueAsync(A<GetSecretValueRequest>._, A<CancellationToken>._))
            .ReturnsLazily((GetSecretValueRequest r, CancellationToken _) =>
            {
                ThrowIfDown();
                return Task.FromResult(new GetSecretValueResponse { Name = r.SecretId, SecretString = Secrets[r.SecretId] });
            });
    }

    private void ThrowIfDown()
    {
        if (Down)
        {
            throw new AmazonSecretsManagerException("Secrets Manager is down");
        }
    }
}
