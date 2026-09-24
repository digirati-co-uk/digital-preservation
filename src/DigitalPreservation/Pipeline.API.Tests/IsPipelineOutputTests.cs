using FluentAssertions;
using Pipeline.API.Features.Pipeline.Requests;
using Xunit;

namespace Pipeline.API.Tests;

/// <summary>
/// The upload guard used to test filePath.Contains("brunnhilde") - which only worked because the
/// configured ProcessFolder happens to contain that substring too, so exif and virus-definition
/// outputs would silently stop uploading if the folder were ever renamed. IsPipelineOutput checks
/// the managed folder list instead (issue #275 item 1).
/// </summary>
public class IsPipelineOutputTests
{
    private static readonly string[] PipelineFolders =
        ["metadata/brunnhilde", "metadata/exif", "metadata/virus-definition"];

    [Theory]
    [InlineData("/tmp/work/dep1/metadata", "/tmp/work/dep1/metadata/brunnhilde/report.txt")]
    [InlineData("/tmp/work/dep1/metadata", "/tmp/work/dep1/metadata/exif/output.json")]
    [InlineData("/tmp/work/dep1/metadata", "/tmp/work/dep1/metadata/virus-definition/defs.txt")]
    public void True_ForFilesUnderAManagedFolder_WithASourcePathThatDoesNotContainBrunnhilde(
        string sourcePath, string filePath)
    {
        ProcessPipelineJobHandler.IsPipelineOutput(filePath, sourcePath, PipelineFolders).Should().BeTrue();
    }

    [Fact]
    public void False_ForAFileDirectlyInMetadata()
    {
        ProcessPipelineJobHandler.IsPipelineOutput(
            "/tmp/work/dep1/metadata/readme.txt", "/tmp/work/dep1/metadata", PipelineFolders).Should().BeFalse();
    }

    [Fact]
    public void False_ForAFileInAnUnmanagedSubfolder()
    {
        ProcessPipelineJobHandler.IsPipelineOutput(
            "/tmp/work/dep1/metadata/other/x.txt", "/tmp/work/dep1/metadata", PipelineFolders).Should().BeFalse();
    }

    [Fact]
    public void False_ForASiblingThatSharesAPrefix()
    {
        ProcessPipelineJobHandler.IsPipelineOutput(
            "/tmp/work/dep1/metadata/exifX/a.txt", "/tmp/work/dep1/metadata", PipelineFolders).Should().BeFalse();
    }

    [Fact]
    public void True_WithWindowsStyleSeparators()
    {
        ProcessPipelineJobHandler.IsPipelineOutput(
            @"C:\tmp\work\dep1\metadata\exif\output.json", @"C:\tmp\work\dep1\metadata", PipelineFolders)
            .Should().BeTrue();
    }
}
