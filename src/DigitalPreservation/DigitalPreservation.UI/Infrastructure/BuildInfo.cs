using System.Globalization;

namespace DigitalPreservation.UI.Infrastructure;

/// <summary>
/// What this image was built from: the "Build" configuration section. Dockerfile.UI sets it as
/// Build__* environment variables, from the build arguments that the docker-build-and-push action
/// passes. A local build has none of them.
/// </summary>
public class BuildInfo
{
    public const string Build = "Build";

    /// <summary>Full commit SHA.</summary>
    public string? Commit { get; set; }

    /// <summary>The branch or tag the image was built from.</summary>
    public string? Ref { get; set; }

    /// <summary>
    /// <c>git describe --tags --always</c>: v1.3.4 for a build of that tag, or v1.3.1-377-g95c9c658 for
    /// one 377 commits after the nearest earlier tag on its branch.
    /// </summary>
    public string? Describe { get; set; }

    public string? CommitSubject { get; set; }
    public string? CommitAuthor { get; set; }

    /// <summary>ISO 8601.</summary>
    public string? CommitDate { get; set; }

    /// <summary>The pull request the commit merged, or the open one the image was built for.</summary>
    public string? PullRequest { get; set; }

    public string? PullRequestTitle { get; set; }

    /// <summary>e.g. https://github.com/digirati-co-uk/digital-preservation</summary>
    public string? Repository { get; set; }

    /// <summary>The GitHub Actions run that built the image.</summary>
    public string? Run { get; set; }

    /// <summary>When the image was built. ISO 8601.</summary>
    public string? Date { get; set; }

    public bool IsKnown => !string.IsNullOrEmpty(Commit);

    public string? CommitUrl => RepositoryLink("commit", Commit);
    public string? RefUrl => RepositoryLink("tree", Ref);
    public string? PullRequestUrl => RepositoryLink("pull", PullRequest);

    public string? CommitDateUtc => InUtc(CommitDate);
    public string? DateUtc => InUtc(Date);

    /// <summary>The date as "2026-11-20 15:24 UTC", or as it came if it can't be read.</summary>
    private static string? InUtc(string? iso8601) =>
        DateTimeOffset.TryParse(iso8601, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC"
            : iso8601;

    private string? RepositoryLink(string kind, string? value) =>
        string.IsNullOrEmpty(Repository) || string.IsNullOrEmpty(value) ? null : $"{Repository}/{kind}/{value}";
}
