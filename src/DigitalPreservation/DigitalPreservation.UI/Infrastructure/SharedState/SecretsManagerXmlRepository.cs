using System.Xml.Linq;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;

namespace DigitalPreservation.UI.Infrastructure.SharedState;

/// <summary>
/// Keeps the UI's data-protection keys in AWS Secrets Manager (issue #320), one secret per key under
/// <see cref="AwsSharedStateOptions.KeySecretPrefix"/>. A key is created once and never updated, so
/// two tasks generating keys at the same moment can't overwrite each other.
/// </summary>
/// <remarks>
/// It never throws. If Secrets Manager can't be read, it returns the keys it last read plus any it
/// made itself, which is what data protection had before: keys this task made, in memory. A key made
/// while Secrets Manager couldn't be written is saved the next time the key ring is read.
/// </remarks>
public sealed class SecretsManagerXmlRepository(
    Func<IAmazonSecretsManager> clientFactory,
    IOptions<SharedStateOptions> options,
    ILogger<SecretsManagerXmlRepository> logger) : IXmlRepository
{
    private readonly string prefix = options.Value.Aws.KeySecretPrefix ?? "";
    private readonly string? kmsKeyId = string.IsNullOrWhiteSpace(options.Value.Aws.KmsKeyId) ? null : options.Value.Aws.KmsKeyId;
    private readonly TimeSpan timeout = TimeSpan.FromMilliseconds(Math.Max(1, options.Value.TimeoutMilliseconds));
    private readonly Lock gate = new();

    // Created on first use, inside the try blocks: a client that can't be built (no credentials, say)
    // is one more way for Secrets Manager to be unavailable.
    private IAmazonSecretsManager? client;
    private IAmazonSecretsManager SecretsManager => client ??= clientFactory();

    // Secret name -> key element: the last successful read, and the keys not yet saved.
    private Dictionary<string, XElement> lastRead = new(StringComparer.Ordinal);
    private readonly Dictionary<string, XElement> unsaved = new(StringComparer.Ordinal);

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        Dictionary<string, XElement> read;
        try
        {
            read = ReadAll();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not read the UI's data-protection keys from Secrets Manager ({Prefix}); using the keys " +
                "this task already has. Users may have to sign in again after a redeploy until it recovers.", prefix);
            lock (gate)
            {
                return Known();
            }
        }

        KeyValuePair<string, XElement>[] toSave;
        lock (gate)
        {
            lastRead = read;
            toSave = unsaved.Where(u => !read.ContainsKey(u.Key)).ToArray();
            foreach (var name in unsaved.Keys.Where(read.ContainsKey).ToList())
            {
                unsaved.Remove(name);
            }
        }
        foreach (var (name, element) in toSave)
        {
            TrySave(name, element);
        }

        lock (gate)
        {
            return Known();
        }
    }

    // Call inside the lock.
    private List<XElement> Known() =>
        lastRead.Values.Concat(unsaved.Where(u => !lastRead.ContainsKey(u.Key)).Select(u => u.Value)).ToList();

    public void StoreElement(XElement element, string friendlyName)
    {
        var name = prefix + (string.IsNullOrWhiteSpace(friendlyName) ? Guid.NewGuid().ToString("N") : friendlyName);
        lock (gate)
        {
            unsaved[name] = element;
        }
        TrySave(name, element);
    }

    private void TrySave(string name, XElement element)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                SecretsManager.CreateSecretAsync(new CreateSecretRequest
                {
                    Name = name,
                    Description = "Preservation UI data-protection key (digital-preservation#320). Do not edit.",
                    SecretString = element.ToString(SaveOptions.DisableFormatting),
                    KmsKeyId = kmsKeyId
                }, cts.Token).WaitAsync(timeout).GetAwaiter().GetResult();
            }
            catch (ResourceExistsException)
            {
                // Saved already, by an earlier attempt whose reply was lost.
            }
            lock (gate)
            {
                unsaved.Remove(name);
                lastRead[name] = element;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not save a new UI data-protection key to Secrets Manager as {Name}; this task keeps it " +
                "in memory and will save it when Secrets Manager is available.", name);
        }
    }

    private Dictionary<string, XElement> ReadAll()
    {
        var names = new List<string>();
        string? next = null;
        do
        {
            using var cts = new CancellationTokenSource(timeout);
            var page = SecretsManager.ListSecretsAsync(new ListSecretsRequest
            {
                // Secrets Manager's name filter is a case-insensitive prefix match; check exactly below.
                Filters = [new Filter { Key = FilterNameStringType.Name, Values = [prefix] }],
                NextToken = next
            }, cts.Token).WaitAsync(timeout).GetAwaiter().GetResult();
            names.AddRange((page.SecretList ?? [])
                .Select(s => s.Name)
                .Where(n => n != null && n.StartsWith(prefix, StringComparison.Ordinal)));
            next = page.NextToken;
        } while (!string.IsNullOrEmpty(next));

        var read = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            using var cts = new CancellationTokenSource(timeout);
            var value = SecretsManager.GetSecretValueAsync(new GetSecretValueRequest { SecretId = name }, cts.Token)
                .WaitAsync(timeout).GetAwaiter().GetResult();
            try
            {
                read[name] = XElement.Parse(value.SecretString);
            }
            catch (Exception ex)
            {
                // One damaged secret shouldn't cost the others.
                logger.LogWarning(ex, "Ignoring UI data-protection key secret {Name}: it is not a key", name);
            }
        }
        return read;
    }
}
