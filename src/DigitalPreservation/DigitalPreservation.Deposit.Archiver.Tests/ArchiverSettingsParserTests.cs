using FluentAssertions;

namespace DigitalPreservation.Deposit.Archiver.Tests;

public class ArchiverSettingsParserTests
{
    [Theory]
    [InlineData("6", 6)]
    [InlineData("-6", 6)] // sign is not significant, only magnitude
    public void ParseLastModifiedMonths_ReturnsAbsoluteValue(string raw, int expected)
    {
        ArchiverSettingsParser.ParseLastModifiedMonths(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    public void ParseLastModifiedMonths_Throws_WhenMissingOrZero(string? raw)
    {
        var act = () => ArchiverSettingsParser.ParseLastModifiedMonths(raw);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("LAST_MODIFIED_MONTHS must be a non-zero value.");
    }

    [Fact]
    public void ParseBatchSize_ReturnsValue_WhenPositive()
    {
        ArchiverSettingsParser.ParseBatchSize("25").Should().Be(25);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-1")]
    public void ParseBatchSize_Throws_WhenMissingOrNotPositive(string? raw)
    {
        var act = () => ArchiverSettingsParser.ParseBatchSize(raw);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("BATCH_SIZE must be a positive integer.");
    }
}
