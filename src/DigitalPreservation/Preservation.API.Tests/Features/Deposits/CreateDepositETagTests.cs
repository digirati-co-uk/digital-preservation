using System.Security.Claims;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
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
/// CreateDepositBase adds the metadata and metadata/ad-hoc folders to a brand-new deposit's METS,
/// then must read the ETag the caller gets back AFTER those writes, not reuse the wrapper it read
/// earlier to decide whether the folders were needed - that ETag is already stale by the time the
/// response goes out (issue #264).
/// </summary>
public class CreateDepositETagTests
{
    private const string MintedId = "dep-etag-1";
    private static readonly Uri Files = new("s3://deposits/" + MintedId + "/deposit-files/");

    private readonly IMetsManager metsManager = A.Fake<IMetsManager>();
    private readonly IMetsParser metsParser = A.Fake<IMetsParser>();
    private readonly IStorage storage = A.Fake<IStorage>();

    [Fact]
    public async Task The_Returned_ETag_Is_Read_After_The_Metadata_Folders_Are_Added_Not_Before()
    {
        // The read at the top of HandleBase, used to decide whether the metadata folders already
        // exist - deliberately a different value from the one below, so the test fails if the
        // final ETag is ever read from (or defaulted to) this one instead.
        A.CallTo(() => metsParser.GetMetsFileWrapper(Files, true)).Returns(Result.OkNotNull(new MetsFileWrapper
        {
            ETag = "\"before-the-folder-writes\"",
            Editable = true,
            PhysicalStructure = WorkingDirectory.RootDirectory()
        }));
        // The final, post-write read (parse: false) - this is the one that must end up on the response.
        A.CallTo(() => metsParser.GetMetsFileWrapper(Files, false)).Returns(Result.OkNotNull(new MetsFileWrapper
        {
            ETag = "\"after-the-folder-writes\""
        }));
        A.CallTo(() => metsManager.HandleCreateFolder(A<Uri>._, A<WorkingDirectory>._, A<string>._))
            .Returns(Result.Ok());
        A.CallTo(() => storage.GetWorkingFilesLocation(MintedId, A<TemplateType>._, A<string?>._, A<bool>._))
            .Returns(Result.OkNotNull(Files));
        A.CallTo(() => metsManager.CreateStandardMets(A<Uri>._, A<string?>._))
            .Returns(Result.OkNotNull(new MetsFileWrapper()));

        // MetadataReader.Create (called for every new deposit, to decorate the working file
        // system) reads for bagit/brunnhilde/siegfried/exif output before deciding there is none -
        // an unconfigured fake Stream is not readable, so these need an explicit "nothing here".
        A.CallTo(() => storage.GetStream(A<Uri>._))
            .Returns(Result.FailNotNull<(Stream? ResponseStream, DateTime LastModified)>(ErrorCodes.NotFound, "not found"));
        A.CallTo(() => storage.Exists(A<Uri>._)).Returns(false);
        A.CallTo(() => storage.GetListing(A<Uri>._, A<string>._)).Returns(new List<Uri>());

        var dbOptions = new DbContextOptionsBuilder<PreservationContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new PreservationContext(dbOptions);

        var identityService = A.Fake<IIdentityService>();
        A.CallTo(() => identityService.MintIdentity(nameof(Deposit), A<Uri?>._)).Returns(MintedId);

        var resourceMutator = new ResourceMutator(Options.Create(new MutatorOptions
        {
            Storage = "https://storage.test",
            Preservation = "https://preservation.test"
        }));

        var handler = new CreateDepositHandler(
            NullLogger<CreateDepositHandler>.Instance,
            dbContext,
            resourceMutator,
            identityService,
            A.Fake<IStorageApiClient>(),
            storage,
            metsManager,
            A.Fake<MetsFromArchivalGroup>(),
            A.Fake<WorkspaceManagerFactory>(),
            metsParser,
            A.Fake<IClientDirectory>(),
            // Matches the bucket of the fake working location above (s3://deposits/...); the #288
            // export-destination assertion compares against it whenever an export is involved.
            Options.Create(new AwsStorageOptions { DefaultWorkingBucket = "deposits" }));

        // No ArchivalGroup: the simplest "brand-new deposit" shape, so ArchivalGroupRequestValidator
        // short-circuits to archivalGroupExists=false without needing a working storageApiClient.
        var request = new CreateDeposit(
            new Deposit { Template = TemplateType.RootLevel },
            export: false,
            new ClaimsPrincipal(new ClaimsIdentity()));

        var result = await handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Value!.MetsETag.Should().Be("\"after-the-folder-writes\"");
    }
}
