using DigitalPreservation.Utils;
using FluentAssertions;
using Xunit;

namespace Pipeline.API.Tests;

/// <summary>
/// <see cref="PathX.IsUnderRoot"/> is the boundary check added to close the deposit-folder path
/// traversal in the Pipeline API's diagnostics endpoint and process-folder clean-up: Path.Combine
/// silently drops the root when the second segment is itself rooted, and Path.GetFullPath resolves
/// ".." lexically with no regard to where it ends up, so neither of those alone stops a
/// caller-controlled segment from escaping a configured root directory.
/// </summary>
public class PathXTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "pathx-tests-root");

    [Fact]
    public void The_Root_Itself_Is_Under_The_Root()
    {
        PathX.IsUnderRoot(Root, Root).Should().BeTrue();
    }

    [Fact]
    public void A_Direct_Child_Is_Under_The_Root()
    {
        var candidate = Path.Combine(Root, "deposit-1");

        PathX.IsUnderRoot(Root, candidate).Should().BeTrue();
    }

    [Fact]
    public void A_Nested_Descendant_Is_Under_The_Root()
    {
        var candidate = Path.Combine(Root, "deposit-1", "objects", "a.tif");

        PathX.IsUnderRoot(Root, candidate).Should().BeTrue();
    }

    [Fact]
    public void A_Path_Resolving_Above_The_Root_Via_Traversal_Is_Not_Under_The_Root()
    {
        var candidate = Path.GetFullPath(Path.Combine(Root, "..", ".."));

        PathX.IsUnderRoot(Root, candidate).Should().BeFalse();
    }

    [Fact]
    public void A_Rooted_Candidate_Elsewhere_On_Disk_Is_Not_Under_The_Root()
    {
        var candidate = Path.Combine(Path.GetTempPath(), "somewhere-else");

        PathX.IsUnderRoot(Root, candidate).Should().BeFalse();
    }

    [Fact]
    public void A_Sibling_That_Merely_Shares_A_Name_Prefix_Is_Not_Under_The_Root()
    {
        // "pathx-tests-root-other" starts with the same characters as the root but is a different
        // directory - a plain candidate.StartsWith(root) check (without the trailing separator)
        // would wrongly treat this as contained.
        var sibling = Root + "-other";
        var candidate = Path.Combine(sibling, "deposit-1");

        PathX.IsUnderRoot(Root, candidate).Should().BeFalse();
    }
}
