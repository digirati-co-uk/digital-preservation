using System.Diagnostics;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace XmlGen.Tests;

/// <summary>
/// What adding files to a METS actually costs, at several sizes.
/// </summary>
/// <remarks>
/// The path cache is maintained incrementally - entries added and evicted as the document is
/// mutated, rather than rebuilt - and that machinery exists for one reason: navigation was
/// quadratic on the add path, measured at 14,017ms for 1,600 files (8.76ms each) before the ID
/// index, and 705ms (0.44ms each) after. Nothing in the suite would have noticed that regressing.
/// <para>
/// This does not assert a time: a wall-clock threshold in CI is a flaky test, and Manual keeps it
/// out of the gating run anyway. What it does is make the number reproducible on demand, next to
/// the claim it justifies. The per-file column is the one to read - if it climbs with N, the cost
/// is superlinear again, whatever the absolute numbers on the machine of the day.
/// </para>
/// <para>
/// At the time of writing it reports ~0.38ms per file at 1,600 - the same order as the 0.44ms
/// recorded when the index was added - and a per-file cost about 2.6× higher at 1,600 than at 200.
/// That residual is <c>SortChildDivs</c> re-sorting a growing sibling list on every add; it
/// predates this work and is deliberately left alone, because sorting on insert would start
/// reordering documents that were never sorted. A figure near 8× would mean the quadratic
/// behaviour was back.
/// </para>
/// </remarks>
public class PathCacheBenchmark(ITestOutputHelper output)
{
    private const string TestDigest = "eb634d64ce8e6be5195174ceaef9ac9e19c37119f3b31618630aa633ccdbf68f";

    private readonly MetsManager metsManager = BuildMetsManager();

    private static MetsManager BuildMetsManager()
    {
        var serviceProvider = new ServiceCollection().AddLogging().BuildServiceProvider();
        var parserLogger = serviceProvider.GetService<ILoggerFactory>()!.CreateLogger<MetsParser>();
        var parser = new MetsParser(new FileSystemMetsLoader(), parserLogger);
        var metadataManager = new MetadataManager(
            new PremisManager(), new PremisManagerExif(), new PremisEventManagerVirus());
        return new MetsManager(parser, new FileSystemMetsStorage(parser), metadataManager);
    }

    [Fact, Trait("Category", "Manual")]
    public async Task Adding_Files_Scales_Linearly()
    {
        int[] sizes = [200, 400, 800, 1600];
        var perFile = new List<double>();

        output.WriteLine($"{"files",8}{"total ms",12}{"ms/file",12}");
        foreach (var size in sizes)
        {
            var elapsed = await TimeAdding(size);
            perFile.Add(elapsed.TotalMilliseconds / size);
            output.WriteLine($"{size,8}{elapsed.TotalMilliseconds,12:F0}{elapsed.TotalMilliseconds / size,12:F2}");
        }

        output.WriteLine("");
        output.WriteLine($"per-file cost at {sizes[^1]} vs {sizes[0]}: ×{perFile[^1] / perFile[0]:F2}");
        output.WriteLine("(≈1 is linear; ≈3 is the known residual from SortChildDivs re-sorting a");
        output.WriteLine(" growing sibling list; ≈8 would be the quadratic behaviour the ID index removed)");
    }

    private async Task<TimeSpan> TimeAdding(int fileCount)
    {
        var uri = new Uri(new FileInfo($"Outputs/benchmark-{fileCount}.xml").FullName);
        var create = await metsManager.CreateStandardMets(uri, $"Benchmark {fileCount}");
        create.Success.Should().BeTrue(create.ErrorMessage ?? "");
        var fullMets = (await metsManager.GetFullMets(uri, create.Value!.ETag)).Value!;

        // In memory, and one file at a time, which is the shape the pipeline uses: the cost being
        // measured is navigating to the parent and maintaining the cache, not serialization.
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < fileCount; i++)
        {
            var add = metsManager.AddToMets(fullMets, new WorkingFile
            {
                LocalPath = $"objects/file-{i:D5}.pdf",
                Name = $"file-{i:D5}.pdf",
                ContentType = "application/pdf",
                Digest = TestDigest,
                Size = 54321,
                Modified = DateTime.UtcNow
            });
            add.Success.Should().BeTrue(add.ErrorMessage ?? "");
        }
        stopwatch.Stop();

        fullMets.PhysicalDivsByPath.Should().HaveCountGreaterThanOrEqualTo(fileCount);
        return stopwatch.Elapsed;
    }
}
