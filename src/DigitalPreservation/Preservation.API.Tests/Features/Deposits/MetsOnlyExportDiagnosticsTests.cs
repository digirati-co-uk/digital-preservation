using System.Security.Claims;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Export;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Storage;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Core.Auth;
using DigitalPreservation.Mets;
using DigitalPreservation.Workspace;
using FakeItEasy;
using LeedsDlipServices.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Preservation.API.Data;
using Preservation.API.Features.Deposits.Requests;
using Preservation.API.Mutation;
using Storage.Client;
using Storage.Repository.Common;
using Storage.Repository.Common.Mets;

namespace Preservation.API.Tests.Features.Deposits;

/// <summary>
/// Issue #273/PR #333 made the Storage API's /exportMetsOnly answer 200 with the failure recorded
/// as an error on the Export, rather than throwing - so a successful Result with no DateFinished
/// is how CreateDepositBase.EnsureMets now learns the export itself failed. Falling through to
/// the failed-Result branch there read ErrorCode and ErrorMessage, both null on a successful
/// Result, so the 500 the caller got back had no detail at all - the exception text now sitting in
/// Export.Errors was never read (issue #358).
/// </summary>
public class MetsOnlyExportDiagnosticsTests
{
    private const string MintedId = "dep-mets-only-1";
    private static readonly Uri ArchivalGroup = new("https://preservation.test/repository/cc/thing");
    private static readonly Uri Files = new($"s3://deposits/{MintedId}/deposit-files/");

    private readonly IStorageApiClient storageApiClient = A.Fake<IStorageApiClient>();
    private readonly IStorage storage = A.Fake<IStorage>();

    private static StorageMap StorageMapWithMets() => new()
    {
        Version = new ObjectVersion { MementoTimestamp = "1", MementoDateTime = DateTime.UtcNow },
        StorageType = "S3",
        Root = "deposits",
        ObjectPath = "cc/thing",
        Files = new Dictionary<string, OriginFile>
        {
            ["mets.xml"] = new() { Hash = "abc", FullPath = "mets.xml" }
        },
        Hashes = new Dictionary<string, string>(),
        HeadVersion = new ObjectVersion { MementoTimestamp = "1", MementoDateTime = DateTime.UtcNow },
        AllVersions = [],
        ArchivalGroup = ArchivalGroup
    };

    private CreateDepositHandler CreateHandler()
    {
        var dbOptions = new DbContextOptionsBuilder<PreservationContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dbContext = new PreservationContext(dbOptions);

        var resourceMutator = new ResourceMutator(Options.Create(new MutatorOptions
        {
            Storage = "https://storage.test",
            Preservation = "https://preservation.test"
        }));

        var identityService = A.Fake<IIdentityService>();
        A.CallTo(() => identityService.MintIdentity(nameof(Deposit), A<Uri?>._)).Returns(MintedId);

        A.CallTo(() => storage.GetWorkingFilesLocation(MintedId, A<TemplateType>._, A<string?>._, A<bool>._))
            .Returns(Result.OkNotNull(Files));

        A.CallTo(() => storageApiClient.GetResourceType(ArchivalGroup.AbsolutePath))
            .Returns(Result.Ok<string>(nameof(DigitalPreservation.Common.Model.ArchivalGroup)));
        A.CallTo(() => storageApiClient.GetStorageMap(A<string>.Ignored, A<string?>._))
            .Returns(Result.OkNotNull(StorageMapWithMets()));

        return new CreateDepositHandler(
            NullLogger<CreateDepositHandler>.Instance,
            dbContext,
            resourceMutator,
            identityService,
            storageApiClient,
            storage,
            A.Fake<IMetsManager>(),
            A.Fake<MetsFromArchivalGroup>(),
            A.Fake<WorkspaceManagerFactory>(),
            A.Fake<IMetsParser>(),
            A.Fake<IClientDirectory>(),
            Options.Create(new AwsStorageOptions { DefaultWorkingBucket = "deposits" }));
    }

    private static CreateDeposit Request() => new(
        new Deposit { Template = TemplateType.RootLevel, ArchivalGroup = ArchivalGroup },
        export: false,
        new ClaimsPrincipal(new ClaimsIdentity()));

    [Fact]
    public async Task A_METS_Only_Export_With_No_DateFinished_Fails_With_The_Captured_Error_Text()
    {
        A.CallTo(() => storageApiClient.ExportArchivalGroupMetsOnly(A<Uri>._, A<Uri>._, A<string?>._, A<CancellationToken>._))
            .Returns(Result.OkNotNull(new Export
            {
                ArchivalGroup = ArchivalGroup,
                Destination = Files,
                DateFinished = null,
                Errors = [new DigitalPreservation.Common.Model.Error { Message = "Unable to export archival group cc/thing" }]
            }));

        var result = await CreateHandler().Handle(Request(), CancellationToken.None);

        result.Success.Should().BeFalse("the export never finished, so the deposit can't be created");
        result.ErrorMessage.Should().Contain("Unable to export archival group cc/thing");
    }

    [Fact]
    public async Task A_METS_Only_Export_With_No_DateFinished_And_No_Errors_Still_Fails_With_A_Message()
    {
        A.CallTo(() => storageApiClient.ExportArchivalGroupMetsOnly(A<Uri>._, A<Uri>._, A<string?>._, A<CancellationToken>._))
            .Returns(Result.OkNotNull(new Export
            {
                ArchivalGroup = ArchivalGroup,
                Destination = Files,
                DateFinished = null,
                Errors = null
            }));

        var result = await CreateHandler().Handle(Request(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }
}
