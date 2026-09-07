using DigitalPreservation.Common.Model.Storage;
using FluentAssertions;

namespace DigitalPreservation.Common.Model.Tests;

public class ObjectVersionTests
{
    private static ObjectVersion Version(string memento, string? ocflVersion = null) => new()
    {
        MementoTimestamp = memento,
        MementoDateTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        OcflVersion = ocflVersion
    };

    [Fact]
    public void GetHashCode_DoesNotThrow_WhenOcflVersionIsNull()
    {
        // The old implementation threw here, so a memento-only version - which Equals
        // explicitly supports - crashed any HashSet, Dictionary or Distinct() it met.
        var act = () => Version("20250101000000").GetHashCode();
        act.Should().NotThrow();
    }

    [Fact]
    public void EqualVersions_HaveEqualHashCodes_InTheMixedCaseEqualsAllows()
    {
        // Equals falls back to MementoTimestamp when either OcflVersion is null, so a
        // memento-only version equals the same memento carrying its OCFL version.
        var mementoOnly = Version("20250101000000");
        var withOcflVersion = Version("20250101000000", "v2");

        mementoOnly.Should().Be(withOcflVersion);
        mementoOnly.GetHashCode().Should().Be(withOcflVersion.GetHashCode());
    }

    [Fact]
    public void MementoOnlyVersions_WorkInHashContainers()
    {
        var versions = new HashSet<ObjectVersion>
        {
            Version("20250101000000"),
            Version("20250101000000"),
            Version("20250202000000", "v2")
        };
        versions.Should().HaveCount(2);
    }
}
