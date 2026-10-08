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
            .Contain("objects/a.tif").And
            .Contain("objects/b.tif").And
            .Contain("/directories/0/files/0/localPath");
    }

    [Fact]
    public void The_Comparison_Ignores_Object_Property_Order()
    {
        // ValidateDepositFileSystem can't exercise this directly: System.Text.Json always
        // serialises WorkingDirectory/WorkingFile in the same (JsonPropertyOrder-declared) order
        // for equivalent content, so two differently-key-ordered-but-equal documents never
        // actually arise there. This locks in the JsonNode.DeepEquals guarantee it relies on.
        var tree = JsonNode.Parse("""{"type": "WorkingDirectory", "localPath": "objects"}""");
        var sameTreeDifferentPropertyOrder = JsonNode.Parse("""{"localPath": "objects", "type": "WorkingDirectory"}""");

        JsonNode.DeepEquals(tree, sameTreeDifferentPropertyOrder).Should().BeTrue();
    }
}
