using DigitalPreservation.Common.Model.DepositHelpers;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using Preservation.API.Tests.WorkingDirectories;

namespace DigitalPreservation.Core.Tests.Lists;
public class ExifMetadataComparer
{
    [Fact]
    public void Combined_With_Multiple_Value_Mismatches()
    {
        var depositExifMetadata = new List<ExifTag>
        {
            new() { TagName = "ExifToolVersionNumber", TagValue = "13:43" },
            new() { TagName = "ExifToolVersionNumber", TagValue = "13:45" },
            new() { TagName = "Duration", TagValue = "1267" }
        };

        var metsExifMetadata = new List<ExifTag>
        {
            new() { TagName = "ExifToolVersionNumber", TagValue = "13:44" },
            new() { TagName = "ExifToolVersionNumber", TagValue = "13:46" },
            new() { TagName = "Duration", TagValue = "126" }
        };

        var fileSystem = TestStructure.GetTestMetsStructure();
        var mets = TestStructure.GetTestMetsStructure();

        var metsWorkingFile = new WorkingFile
        {
            LocalPath = "extra-file.txt", ContentType = "text/plain",
            Metadata =
            [
                new ExifMetadata
                {
                    Source = "",
                    Timestamp = DateTime.UtcNow,
                    Tags = metsExifMetadata
                }
            ]
        };

        var depositWorkingFile = new WorkingFile
        {
            LocalPath = "extra-file.txt", ContentType = "text/plain",
            Metadata =
            [
                new ExifMetadata
                {
                    Source = "",
                    Timestamp = DateTime.UtcNow,
                    Tags = depositExifMetadata
                }
            ]
        };

        mets.Files.Add(metsWorkingFile);
        fileSystem.Files.Add(depositWorkingFile);

        var combined = CombinedBuilder.Build(fileSystem, mets, null);

        var mismatches = combined.GetMisMatches();
        mismatches.Item2.Should().HaveCount(1);
        mismatches.Item2[0].Item1.Should().HaveCount(3);
    }

    [Fact]
    public void Combined_With_Multiple_Value_And_Key_Mismatches()
    {
        var depositExifMetadata = new List<ExifTag>
        {
            new() { TagName = "ExifToolVersionNumber", TagValue = "13:43" },
            new() { TagName = "ExifToolVersionNumber", TagValue = "13:45" },
            new() { TagName = "Duration", TagValue = "1267" },
            new() { TagName = "NewNode", TagValue = "1267" }
        };

        var metsExifMetadata = new List<ExifTag>
        {
            new() { TagName = "ExifToolVersionNumber", TagValue = "13:44" },
            new() { TagName = "ExifToolVersionNumber", TagValue = "13:46" },
            new() { TagName = "Duration", TagValue = "126" }
        };

        var fileSystem = TestStructure.GetTestMetsStructure();
        var mets = TestStructure.GetTestMetsStructure();

        var metsWorkingFile = new WorkingFile
        {
            LocalPath = "extra-file.txt", ContentType = "text/plain",
            Metadata =
            [
                new ExifMetadata
                {
                    Source = "",
                    Timestamp = DateTime.UtcNow,
                    Tags = metsExifMetadata
                }
            ]
        };

        var depositWorkingFile = new WorkingFile
        {
            LocalPath = "extra-file.txt", ContentType = "text/plain",
            Metadata =
            [
                new ExifMetadata
                {
                    Source = "",
                    Timestamp = DateTime.UtcNow,
                    Tags = depositExifMetadata
                }
            ]
        };

        mets.Files.Add(metsWorkingFile);
        fileSystem.Files.Add(depositWorkingFile);

        var combined = CombinedBuilder.Build(fileSystem, mets, null);

        var mismatches = combined.GetMisMatches();
        mismatches.Item2.Should().HaveCount(1);
        mismatches.Item2[0].Item1.Should().HaveCount(4);
    }

    [Fact]
    public void Combined_With_No_Mismatches()
    {
        var depositExifMetadata = new List<ExifTag>
        {
            new() { TagName = "ExifToolVersionNumber", TagValue = "13:44" },
            new() { TagName = "ExifToolVersionNumber", TagValue = "13:46" },
            new() { TagName = "Duration", TagValue = "1267" }
        };

        var metsExifMetadata = new List<ExifTag>
        {
            new() { TagName = "ExifToolVersionNumber", TagValue = "13:44" },
            new() { TagName = "ExifToolVersionNumber", TagValue = "13:46" },
            new() { TagName = "Duration", TagValue = "1267" }
        };

        var fileSystem = TestStructure.GetTestMetsStructure();
        var mets = TestStructure.GetTestMetsStructure();

        var metsWorkingFile = new WorkingFile
        {
            LocalPath = "extra-file.txt", ContentType = "text/plain",
            Metadata =
            [
                new ExifMetadata
                {
                    Source = "",
                    Timestamp = DateTime.UtcNow,
                    Tags = metsExifMetadata
                }
            ]
        };

        var depositWorkingFile = new WorkingFile
        {
            LocalPath = "extra-file.txt", ContentType = "text/plain",
            Metadata =
            [
                new ExifMetadata
                {
                    Source = "",
                    Timestamp = DateTime.UtcNow,
                    Tags = depositExifMetadata
                }
            ]
        };

        mets.Files.Add(metsWorkingFile);
        fileSystem.Files.Add(depositWorkingFile);

        var combined = CombinedBuilder.Build(fileSystem, mets, null);

        var mismatches = combined.GetMisMatches();
        mismatches.Item2.Should().HaveCount(0);
    }

    [Fact]
    public void CompareTwoTagValueListsWithZeroMismatches()
    {
        var depositExifMetadata = new List<ExifTag>
        {
            new() { TagName = "ExifToolVersionNumber", TagValue = "13:43" },
            new() { TagName = "ExifToolVersionNumber", TagValue = "13:45" },
            new() { TagName = "Duration", TagValue = "1267" }
        };

        var metsExifMetadata = new List<ExifTag>
        {
            new() { TagName = "ExifToolVersionNumber", TagValue = "13:43" },
            new() { TagName = "ExifToolVersionNumber", TagValue = "13:45" },
            new() { TagName = "Duration", TagValue = "1267" }
        };

        var isEqual = depositExifMetadata.SequenceEqual(metsExifMetadata, new ExifTagComparer());

        isEqual.Should().Be(true);
    }

}
