using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using FakeItEasy;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;

namespace Storage.API.Tests.Fedora;

/// <summary>
/// WithIdentity binds callerIdentity once so a caller making several Fedora writes (e.g.
/// ExecuteImportJobHandler) doesn't have to repeat it on every call (issue #20). These tests only
/// need to prove the wrapper forwards to the exact same IFedoraClient method with the bound
/// identity and every other argument untouched - the real behaviour lives in FedoraClient itself.
/// </summary>
public class FedoraClientXTests
{
    private readonly IFedoraClient inner = A.Fake<IFedoraClient>();
    private const string CallerIdentity = "someone@example.com";
    private static readonly Transaction Transaction = new() { Location = new Uri("https://fedora.example/tx/1") };
    private static readonly CancellationToken Token = CancellationToken.None;

    [Fact]
    public async Task CreateContainer_ForwardsWithBoundIdentity()
    {
        var expected = Result.OkNotNull<Container?>(new Container());
        A.CallTo(() => inner.CreateContainer("path", CallerIdentity, "name", Transaction, Token)).Returns(expected);

        var result = await inner.WithIdentity(CallerIdentity).CreateContainer("path", "name", Transaction, Token);

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task CreateContainerWithinArchivalGroup_ForwardsWithBoundIdentity()
    {
        var expected = Result.OkNotNull<Container?>(new Container());
        A.CallTo(() => inner.CreateContainerWithinArchivalGroup("path", CallerIdentity, "name", Transaction, Token)).Returns(expected);

        var result = await inner.WithIdentity(CallerIdentity).CreateContainerWithinArchivalGroup("path", "name", Transaction, Token);

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task CreateArchivalGroup_ForwardsWithBoundIdentity()
    {
        var expected = Result.OkNotNull<ArchivalGroup?>(new ArchivalGroup());
        A.CallTo(() => inner.CreateArchivalGroup("path", CallerIdentity, "name", Transaction, Token)).Returns(expected);

        var result = await inner.WithIdentity(CallerIdentity).CreateArchivalGroup("path", "name", Transaction, Token);

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task PutBinary_ForwardsWithBoundIdentity()
    {
        var binary = new Binary();
        var expected = Result.OkNotNull<Binary?>(binary);
        A.CallTo(() => inner.PutBinary(binary, CallerIdentity, Transaction, Token)).Returns(expected);

        var result = await inner.WithIdentity(CallerIdentity).PutBinary(binary, Transaction, Token);

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task Delete_ForwardsWithBoundIdentity()
    {
        var resource = new Binary();
        var expected = Result.OkNotNull<PreservedResource>(resource);
        A.CallTo(() => inner.Delete(resource, CallerIdentity, Transaction, Token)).Returns(expected);

        var result = await inner.WithIdentity(CallerIdentity).Delete(resource, Transaction, Token);

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task DeleteContainerOutsideOfArchivalGroup_ForwardsWithBoundIdentity()
    {
        var expected = Result.Ok();
        A.CallTo(() => inner.DeleteContainerOutsideOfArchivalGroup("path", CallerIdentity, true, Token)).Returns(expected);

        var result = await inner.WithIdentity(CallerIdentity).DeleteContainerOutsideOfArchivalGroup("path", true, Token);

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task UpdateContainerMetadata_ForwardsWithBoundIdentity()
    {
        var expected = Result.Ok();
        A.CallTo(() => inner.UpdateContainerMetadata("path", "name", CallerIdentity, Transaction, Token)).Returns(expected);

        var result = await inner.WithIdentity(CallerIdentity).UpdateContainerMetadata("path", "name", Transaction, Token);

        result.Should().BeSameAs(expected);
    }
}
