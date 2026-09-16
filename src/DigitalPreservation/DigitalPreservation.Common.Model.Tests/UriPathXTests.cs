using DigitalPreservation.Utils;
using FluentAssertions;

namespace DigitalPreservation.Common.Model.Tests;

/// <summary>
/// The shared dot-segment and encoded-separator rules are reached from external input - slugs,
/// METS paths, route values - so they must never throw on a malformed percent sequence.
/// Uri.UnescapeDataString leaves such sequences as they are; these pin that, and that none of them
/// is mistaken for a dot segment.
/// </summary>
public class UriPathXTests
{
    [Theory]
    [InlineData("%")]
    [InlineData("%2")]
    [InlineData("%GG")]
    [InlineData("..%")]
    [InlineData("%2e%")]
    [InlineData("%%2e")]
    public void A_Malformed_Percent_Sequence_Neither_Throws_Nor_Counts_As_A_Dot_Segment(string segment)
    {
        var isDot = () => UriPathX.IsDotSegment(segment);
        var hasSeparator = () => UriPathX.ContainsEncodedSeparator(segment);

        isDot.Should().NotThrow().Which.Should().BeFalse();
        hasSeparator.Should().NotThrow().Which.Should().BeFalse();
    }

    [Theory]
    [InlineData(".", true)]
    [InlineData("..", true)]
    [InlineData("%2e", true)]
    [InlineData("%2E%2E", true)]
    [InlineData("...", false)]
    [InlineData(".hidden", false)]
    [InlineData("%252e%252e", false)]   // decodes once to %2e%2e, which nothing downstream decodes again
    public void Dot_Segments_Are_Recognised_As_Given_Or_After_One_Decode(string segment, bool expected)
    {
        UriPathX.IsDotSegment(segment).Should().Be(expected);
    }

    [Theory]
    [InlineData("a%2fb", true)]
    [InlineData("a%5Cb", true)]
    [InlineData("%2e%2e%2fx", true)]
    [InlineData("a%20b", false)]
    [InlineData("100%", false)]
    public void Encoded_Separators_Are_Recognised(string segment, bool expected)
    {
        UriPathX.ContainsEncodedSeparator(segment).Should().Be(expected);
    }
}
