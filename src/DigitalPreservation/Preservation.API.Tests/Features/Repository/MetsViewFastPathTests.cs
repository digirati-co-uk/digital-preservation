using System.Text;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Mets;
using FakeItEasy;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.Repository;
using Preservation.API.Features.Repository.Requests;

namespace Preservation.API.Tests.Features.Repository;

/// <summary>
/// `?view=mets` should not have to read a whole Archival Group out of Fedora just to find one file.
/// These tests pin the shortcut and, as importantly, pin the fallback: the shortcut may only ever
/// make the answer cheaper, never different.
/// </summary>
public class MetsViewFastPathTests
{
    private const string Path = "/repository/born-digital/1234";
    private const string MetsPath = "/repository/born-digital/1234/mets.xml";

    private readonly IMediator mediator = A.Fake<IMediator>();
    private readonly RepositoryController controller;

    public MetsViewFastPathTests()
    {
        controller = ControllerAt(Path);
    }

    private RepositoryController ControllerAt(string requestPath) =>
        new(mediator, A.Fake<IMetsParser>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { Request = { Path = requestPath } }
            }
        };

    [Fact]
    public async Task Mets_View_Streams_Conventional_Mets_Without_Reading_The_Archival_Group()
    {
        GivenResourceType(Path, nameof(ArchivalGroup));
        GivenBinaryStream(MetsPath, "<mets/>");

        var result = await controller.Browse(view: ViewValues.Mets);

        result.Should().BeOfType<FileStreamResult>()
            .Which.ContentType.Should().Be("application/xml");
        // The point of the whole exercise: no request per container, no OCFL inventory, no
        // hour-long cache entry holding the group in the Storage API's memory.
        A.CallTo(() => mediator.Send(A<GetResource>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Mets_Under_Another_Name_Still_Resolves_By_The_Full_Route()
    {
        // BagIt puts it at data/mets.xml, and third-party deposits name it what they like.
        GivenResourceType(Path, nameof(ArchivalGroup));
        GivenNoBinaryAt(MetsPath);
        var archivalGroup = ArchivalGroupWithMetsNamed("EAD-mets.xml");
        GivenResource(archivalGroup);
        GivenBinaryStream(archivalGroup.Binaries[0].Id!.AbsolutePath, "<mets/>");

        var result = await controller.Browse(view: ViewValues.Mets);

        result.Should().BeOfType<FileStreamResult>();
    }

    [Fact]
    public async Task Only_An_Archival_Group_Takes_The_Short_Route()
    {
        // A Container could perfectly well hold a file called mets.xml. Asking for view=mets on one
        // returns the container, as it always has - the shortcut must not turn it into a document.
        GivenResourceType(Path, nameof(Container));
        GivenResource(new Container { Id = new Uri("https://preservation.example/repository/born-digital/1234") });

        var result = await controller.Browse(view: ViewValues.Mets);

        result.Should().NotBeOfType<FileStreamResult>();
        A.CallTo(() => mediator.Send(A<GetBinaryStream>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Parsed_Mets_View_Does_Not_Take_The_Short_Route()
    {
        // It needs the binary's canonical Id to build the wrapper, which only the full route knows.
        GivenResourceType(Path, nameof(ArchivalGroup));
        GivenResource(ArchivalGroupWithMetsNamed("mets.xml"));

        await controller.Browse(view: ViewValues.ParsedMets);

        A.CallTo(() => mediator.Send(A<GetResource>._, A<CancellationToken>._)).MustHaveHappened();
    }

    [Theory]
    // Kestrel resolves dot segments before routing, so this should never arrive at all - which is
    // exactly why it is worth a test: nothing else would notice if that stopped being true.
    [InlineData("/repository/born-digital/../elsewhere")]
    [InlineData("/repository/../../etc/secrets")]
    public async Task A_Path_With_A_Dot_Segment_Does_Not_Take_The_Short_Route(string requestPath)
    {
        // Deliberately no GetResourceType set up: the path is rejected before anything is asked.
        var awkward = ControllerAt(requestPath);
        GivenResource(ArchivalGroupWithMetsNamed("mets.xml"));

        await awkward.Browse(view: ViewValues.Mets);

        A.CallTo(() => mediator.Send(A<GetResourceType>._, A<CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => mediator.Send(A<GetResource>._, A<CancellationToken>._)).MustHaveHappened();
    }

    [Theory]
    [InlineData("/repository/born-digital/od#d", "/repository/born-digital/od%23d")]
    [InlineData("/repository/born-digital/od?d", "/repository/born-digital/od%3Fd")]
    [InlineData("/repository/born-digital/a file", "/repository/born-digital/a%20file")]
    public async Task Encoding_Is_What_Keeps_The_Appended_Name_Part_Of_The_Path(
        string arrivesAs, string usedAs)
    {
        // The safety of appending a file name rests entirely on this: converting Request.Path to a
        // string percent-encodes it, so a '#' or '?' in a name cannot truncate the path that is then
        // asked for. Changing the controller to Request.Path.Value would silently break that, and
        // this is the test that would notice. Preservation API offers no general route to a binary's
        // bytes, and appending to an unencoded path would be one.
        var awkward = ControllerAt(arrivesAs);
        GivenResourceType(usedAs, nameof(ArchivalGroup));
        GivenBinaryStream($"{usedAs}/mets.xml", "<mets/>");

        var result = await awkward.Browse(view: ViewValues.Mets);

        result.Should().BeOfType<FileStreamResult>();
    }

    private static ArchivalGroup ArchivalGroupWithMetsNamed(string name)
    {
        var root = new Uri("https://preservation.example" + Path);
        return new ArchivalGroup
        {
            Id = root,
            Binaries = [new Binary { Id = new Uri($"{root}/{name}") }]
        };
    }

    private void GivenResourceType(string path, string type) =>
        A.CallTo(() => mediator.Send(
                A<GetResourceType>.That.Matches(r => r.Path == path), A<CancellationToken>._))
            .Returns(Result.Ok<string?>(type));

    private void GivenResource(PreservedResource resource) =>
        A.CallTo(() => mediator.Send(A<GetResource>._, A<CancellationToken>._))
            .Returns(Result.Ok<PreservedResource?>(resource));

    private void GivenBinaryStream(string path, string content) =>
        A.CallTo(() => mediator.Send(
                A<GetBinaryStream>.That.Matches(r => r.Path == path), A<CancellationToken>._))
            .Returns(Result.OkNotNull<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(content))));

    private void GivenNoBinaryAt(string path) =>
        A.CallTo(() => mediator.Send(
                A<GetBinaryStream>.That.Matches(r => r.Path == path), A<CancellationToken>._))
            .Returns(Result.FailNotNull<Stream>(ErrorCodes.NotFound, "no such binary"));
}
