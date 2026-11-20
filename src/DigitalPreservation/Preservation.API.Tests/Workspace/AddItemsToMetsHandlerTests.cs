using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Mets;
using DigitalPreservation.Workspace.Requests;
using FakeItEasy;
using FluentAssertions;

namespace Preservation.API.Tests.Workspace;

/// <summary>
/// Issue #486: a file whose content types can't be settled is still added to the METS, with no
/// content type, and the diff later refuses it. Adding it now says so.
/// </summary>
public class AddItemsToMetsHandlerTests
{
    private readonly IMetsManager metsManager = A.Fake<IMetsManager>();

    public AddItemsToMetsHandlerTests()
    {
        var mets = new FullMets
        {
            Mets = new DigitalPreservation.XmlGen.Mets.Mets(),
            Uri = new Uri("s3://bucket/deposits/dep123/mets.xml")
        };
        A.CallTo(() => metsManager.GetFullMets(A<Uri>._, A<string?>._)).Returns(Result.OkNotNull(mets));
        A.CallTo(() => metsManager.AddToMets(A<FullMets>._, A<WorkingBase>._)).Returns(Result.Ok());
        A.CallTo(() => metsManager.WriteMets(A<FullMets>._)).Returns(Result.Ok());
    }

    [Fact]
    public async Task A_File_Whose_Content_Types_Disagree_Is_Added_With_A_Warning()
    {
        var result = await Add(
            File("objects/recording.m4a", uploadedAs: "audio/mp4", identifiedAs: "audio/mpeg"),
            File("objects/photo.tiff", uploadedAs: "image/tiff", identifiedAs: "image/tiff"));

        result.Success.Should().BeTrue();
        result.Value!.Items.Select(item => item.RelativePath)
            .Should().Contain(["objects/recording.m4a", "objects/photo.tiff"]);
        result.Value.Warnings.Should().ContainSingle()
            .Which.Should().StartWith("objects/recording.m4a has more than one content type");
    }

    [Fact]
    public async Task Files_Whose_Content_Types_Are_Settled_Have_No_Warnings()
    {
        var result = await Add(File("objects/photo.tiff", uploadedAs: "image/tiff", identifiedAs: "image/tiff"));

        result.Value!.Warnings.Should().BeEmpty();
    }

    private Task<Result<DigitalPreservation.Workspace.ItemsAffected>> Add(params WorkingBase[] items) =>
        new AddItemsToMetsHandler(metsManager).Handle(
            new AddItemsToMets(new Uri("s3://bucket/deposits/dep123/"), items.ToList(), "\"etag\""),
            CancellationToken.None);

    private static WorkingFile File(string localPath, string uploadedAs, string identifiedAs) => new()
    {
        LocalPath = localPath,
        ContentType = uploadedAs,
        Metadata = [new FileFormatMetadata { Source = "Siegfried", ContentType = identifiedAs }]
    };
}
