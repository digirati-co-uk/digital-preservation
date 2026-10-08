using System.Text.Json.Nodes;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Mets;
using DigitalPreservation.Workspace;
using DigitalPreservation.Workspace.Requests;
using FakeItEasy;
using MediatR;

namespace Preservation.API.Tests;

/// <summary>
/// WorkspaceManager.ValidateDepositFileSystem compares the S3 file tree against the stored
/// Deposit File System file via System.Text.Json's JsonNode.DeepEquals, then - on a mismatch -
/// WorkspaceManager.FindFirstDifference walks both trees to report where they diverge.
/// </summary>
public class WorkspaceManagerValidateDepositFileSystemTests
{
    private static readonly Uri DepositFiles = new("https://preservation.test/deposits/d1/files");

    private static WorkingDirectory BuildTree(string fileName) =>
        new()
        {
            LocalPath = string.Empty,
            Directories =
            [
                new WorkingDirectory
                {
                    LocalPath = "objects",
                    Files =
                    [
                        new WorkingFile { LocalPath = $"objects/{fileName}" }
                    ]
                }
            ]
        };

    private static WorkspaceManager CreateManager(WorkingDirectory s3Tree, WorkingDirectory depositFileSystemTree)
    {
        var mediator = A.Fake<IMediator>();
        A.CallTo(() => mediator.Send(
                A<GetWorkingDirectory>.That.Matches(r => r.ReadFromS3), A<CancellationToken>._))
            .Returns(Task.FromResult(Result.OkNotNull<WorkingDirectory?>(s3Tree)));
        A.CallTo(() => mediator.Send(
                A<GetWorkingDirectory>.That.Matches(r => !r.ReadFromS3), A<CancellationToken>._))
            .Returns(Task.FromResult(Result.OkNotNull<WorkingDirectory?>(depositFileSystemTree)));

        return new WorkspaceManager(
            new Deposit { Files = DepositFiles },
            mediator,
            A.Fake<IMetsParser>());
    }

    [Fact]
    public async Task Equal_Trees_Succeed()
    {
        var manager = CreateManager(BuildTree("a.tif"), BuildTree("a.tif"));

        var result = await manager.ValidateDepositFileSystem();

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task A_Differing_File_Fails_With_Conflict_And_Reports_Where()
    {
        var manager = CreateManager(BuildTree("a.tif"), BuildTree("b.tif"));

        var result = await manager.ValidateDepositFileSystem();

        result.Failure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ErrorMessage.Should()
            .Contain("S3 has").And
            .Contain("objects/a.tif").And
            .Contain("Deposit File System file has").And
            .Contain("objects/b.tif").And
            .Contain("/directories/0/files/0/localPath");
    }
}

/// <summary>
/// WorkspaceManager.FindFirstDifference is exercised directly here (it's internal, exposed to this
/// assembly via InternalsVisibleTo) because ValidateDepositFileSystem can only ever feed it trees
/// that already differ, so cases like "no difference found" or array-length mismatches can't be
/// reached through that higher-level path.
/// </summary>
public class FindFirstDifferenceTests
{
    [Fact]
    public void Finds_Differing_Property_Regardless_Of_Each_Objects_Key_Order()
    {
        var first = JsonNode.Parse("""{"type": "WorkingDirectory", "localPath": "a", "extra": "x"}""");
        var second = JsonNode.Parse("""{"extra": "x", "localPath": "b", "type": "WorkingDirectory"}""");

        var (location, firstValue, secondValue) = WorkspaceManager.FindFirstDifference(first, second, string.Empty);

        location.Should().Be("/localPath");
        firstValue.Should().Be("\"a\"");
        secondValue.Should().Be("\"b\"");
    }

    [Fact]
    public void An_Extra_Array_Element_Is_Reported_As_Missing_On_The_Other_Side()
    {
        var first = JsonNode.Parse("""{"files": [{"localPath": "a"}, {"localPath": "b"}]}""");
        var second = JsonNode.Parse("""{"files": [{"localPath": "a"}]}""");

        var (location, firstValue, secondValue) = WorkspaceManager.FindFirstDifference(first, second, string.Empty);

        location.Should().Be("/files/1");
        firstValue.Should().Be("""{"localPath":"b"}""");
        secondValue.Should().BeNull();
    }

    [Fact]
    public void A_Key_Needing_Escaping_Is_Escaped_In_Json_Pointer_Format()
    {
        var first = JsonNode.Parse("""{"a/b~c": "1"}""");
        var second = JsonNode.Parse("""{"a/b~c": "2"}""");

        var (location, firstValue, secondValue) = WorkspaceManager.FindFirstDifference(first, second, string.Empty);

        location.Should().Be("/a~1b~0c");
        firstValue.Should().Be("\"1\"");
        secondValue.Should().Be("\"2\"");
    }
}
