using DigitalPreservation.Utils;
using FluentAssertions;

namespace XmlGen.Tests.Utils;

public class MetsTimeCodeTests
{
    // --- ToSeconds ---

    [Theory]
    [InlineData("00:00:15",    15.0)]      // Liddle LOG_0001 start
    [InlineData("00:35:09.5",  2109.5)]    // Liddle LOG_0001 end
    [InlineData("00:36:40",    2200.0)]    // Liddle LOG_0002 start (tape1side1)
    [InlineData("00:45:00",    2700.0)]    // Liddle LOG_0002 end (tape1side1)
    [InlineData("00:00:09.2",  9.2)]       // Liddle LOG_0002 start (tape1side2)
    [InlineData("00:20:09",    1209.0)]    // Liddle LOG_0002 end (tape1side2)
    [InlineData("00:20:50",    1250.0)]    // Liddle LOG_0003 start
    [InlineData("00:31:40",    1900.0)]    // Liddle LOG_0003 end
    [InlineData("00:00:13.9",  13.9)]      // Liddle LOG_0004 start (tape2side1)
    [InlineData("00:44:50.54", 2690.54)]   // Liddle LOG_0004 end (tape2side1)
    [InlineData("00:00:20.6",  20.6)]      // Liddle LOG_0004 start (tape2side2)
    [InlineData("00:23:07.51", 1387.51)]   // Liddle LOG_0004 end (tape2side2)
    [InlineData("00:00:00",    0.0)]       // zero
    [InlineData("01:00:00",    3600.0)]    // one hour exactly
    [InlineData("01:01:01.001", 3661.001)] // hours, minutes, seconds and milliseconds
    [InlineData("23:59:59.999", 86399.999)]
    public void ToSeconds_ParsesCorrectly(string timeCode, double expected)
    {
        MetsTimeCode.ToSeconds(timeCode).Should().BeApproximately(expected, precision: 0.0001);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ToSeconds_ThrowsOnNullOrWhitespace(string input)
    {
        var act = () => MetsTimeCode.ToSeconds(input);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("00:00")]          // missing seconds part
    [InlineData("00:00:00:00")]    // extra part
    [InlineData("aa:00:00")]       // non-numeric hours
    [InlineData("00:60:00")]       // minutes out of range
    [InlineData("00:00:60")]       // seconds out of range
    [InlineData("00:00:-1")]       // negative seconds
    public void ToSeconds_ThrowsOnInvalidFormat(string input)
    {
        var act = () => MetsTimeCode.ToSeconds(input);
        act.Should().Throw<FormatException>();
    }

    // --- FromSeconds ---

    [Theory]
    [InlineData(15.0,      "00:00:15")]
    [InlineData(2109.5,    "00:35:09.5")]
    [InlineData(2200.0,    "00:36:40")]
    [InlineData(2700.0,    "00:45:00")]
    [InlineData(9.2,       "00:00:09.2")]
    [InlineData(1209.0,    "00:20:09")]
    [InlineData(1250.0,    "00:20:50")]
    [InlineData(1900.0,    "00:31:40")]
    [InlineData(13.9,      "00:00:13.9")]
    [InlineData(2690.54,   "00:44:50.54")]
    [InlineData(20.6,      "00:00:20.6")]
    [InlineData(1387.51,   "00:23:07.51")]
    [InlineData(0.0,       "00:00:00")]
    [InlineData(3600.0,    "01:00:00")]
    [InlineData(3661.001,  "01:01:01.001")]
    [InlineData(86399.999, "23:59:59.999")]
    public void FromSeconds_FormatsCorrectly(double seconds, string expected)
    {
        MetsTimeCode.FromSeconds(seconds).Should().Be(expected);
    }

    [Fact]
    public void FromSeconds_ThrowsOnNegative()
    {
        var act = () => MetsTimeCode.FromSeconds(-0.001);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // --- Round-trip ---

    [Theory]
    [InlineData("00:00:15")]
    [InlineData("00:35:09.5")]
    [InlineData("00:44:50.54")]
    [InlineData("00:23:07.51")]
    [InlineData("00:00:00")]
    [InlineData("01:00:00")]
    [InlineData("23:59:59.999")]
    public void RoundTrip_FromSeconds_ToSeconds(string timeCode)
    {
        var seconds = MetsTimeCode.ToSeconds(timeCode);
        var result = MetsTimeCode.FromSeconds(seconds);
        result.Should().Be(timeCode);
    }
}
