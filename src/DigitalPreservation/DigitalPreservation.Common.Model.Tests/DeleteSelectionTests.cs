using DigitalPreservation.Common.Model.DepositHelpers;
using FluentAssertions;

namespace DigitalPreservation.Common.Model.Tests;

/// <summary>
/// ContinueIfFail is meant to name paths whose deletion failures may be carried past, but
/// DeleteItemsHandler used to treat it as the opposite: the list of paths whose failure is fatal
/// (issue #259). FailureIsTolerated is the one place the rule now lives, so it can be tested
/// without S3.
/// </summary>
public class DeleteSelectionTests
{
    [Fact]
    public void An_Empty_ContinueIfFail_Tolerates_Nothing()
    {
        var selection = new DeleteSelection { ContinueIfFail = [] };

        selection.FailureIsTolerated("metadata/brunnhilde/report.csv").Should().BeFalse();
    }

    [Fact]
    public void A_Null_ContinueIfFail_Tolerates_Nothing()
    {
        var selection = new DeleteSelection { ContinueIfFail = null };

        selection.FailureIsTolerated("metadata/brunnhilde/report.csv").Should().BeFalse();
    }

    [Fact]
    public void A_Path_Listed_Exactly_Is_Tolerated()
    {
        var selection = new DeleteSelection { ContinueIfFail = ["metadata/brunnhilde"] };

        selection.FailureIsTolerated("metadata/brunnhilde").Should().BeTrue();
    }

    [Fact]
    public void A_File_Under_A_Listed_Path_Is_Tolerated()
    {
        var selection = new DeleteSelection { ContinueIfFail = ["metadata/brunnhilde"] };

        selection.FailureIsTolerated("metadata/brunnhilde/report.csv").Should().BeTrue();
    }

    [Fact]
    public void A_Nested_Subfolder_Under_A_Listed_Path_Is_Tolerated()
    {
        var selection = new DeleteSelection { ContinueIfFail = ["metadata/brunnhilde"] };

        selection.FailureIsTolerated("metadata/brunnhilde/logs/siegfried.log").Should().BeTrue();
    }

    [Fact]
    public void A_Sibling_That_Merely_Shares_A_Prefix_Is_Not_Tolerated()
    {
        // "metadata/brunnhildeX" is not under "metadata/brunnhilde" - a bare StartsWith without the
        // "/" separator would wrongly tolerate it.
        var selection = new DeleteSelection { ContinueIfFail = ["metadata/brunnhilde"] };

        selection.FailureIsTolerated("metadata/brunnhildeX/a.csv").Should().BeFalse();
    }

    [Fact]
    public void An_Unlisted_Path_Is_Not_Tolerated()
    {
        var selection = new DeleteSelection { ContinueIfFail = ["metadata/brunnhilde"] };

        selection.FailureIsTolerated("metadata/exif").Should().BeFalse();
    }
}
