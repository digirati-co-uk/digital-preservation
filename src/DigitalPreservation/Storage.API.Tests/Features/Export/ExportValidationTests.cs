using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Core.Auth;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Storage.API.Features.Export;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;
using Storage.Repository.Common;
using ExportResource = DigitalPreservation.Common.Model.Export.Export;

namespace Storage.API.Tests.Features.Export;

/// <summary>
/// ExportValidation.Validate/Stamp replace the copy of this logic QueueExportHandler and
/// ExportMetsOnlyController each hand-rolled (issue #388 item 2.6): the ArchivalGroup
/// presence/path check, the destination-bucket check, and stamping
/// Created/CreatedBy/LastModified/LastModifiedBy. Neither copy had a test for the
/// ArchivalGroup-presence or repository-path checks before this.
/// </summary>
public class ExportValidationTests
{
    private const string DefaultBucket = "default-working-bucket";
    private const string ProfileBucket = "goobi-deposits";
    private static readonly Uri PermittedDestination = new($"s3://{DefaultBucket}/export-of-my-ag");

    private static AwsStorageOptions StorageOptions() => new() { DefaultWorkingBucket = DefaultBucket };

    private static IClientDirectory ClientDirectory()
    {
        var clientDirectory = A.Fake<IClientDirectory>();
        A.CallTo(() => clientDirectory.DepositBuckets).Returns([ProfileBucket]);
        return clientDirectory;
    }

    [Fact]
    public void Validate_Fails_WhenArchivalGroupIsNull()
    {
        // ArchivalGroup is `required Uri`, but JSON deserialisation of a request body with no
        // archivalGroup field bypasses that at runtime - null!, not a genuine compile-time case.
        var export = new ExportResource { ArchivalGroup = null!, Destination = PermittedDestination };

        var result = ExportValidation.Validate(export, StorageOptions(), ClientDirectory(), NullLogger.Instance);

        result.Failure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BadRequest);
        result.ErrorMessage.Should().Contain("Archival Group");
    }

    [Fact]
    public void Validate_Fails_WhenArchivalGroupIsNotUnderTheRepositoryRoot()
    {
        // "fcr:acl" is a Fedora-reserved name that SafeRepositoryPath refuses below segment 0
        // (pinned directly against known-good/bad paths in RepositoryPathTests) - unlike a literal
        // ".." segment, it survives Uri's own dot-segment normalisation unchanged, so this reaches
        // Validate exactly as written.
        var export = new ExportResource
        {
            ArchivalGroup = new Uri("https://storage.test/repository/cc/thing/fcr:acl"),
            Destination = PermittedDestination
        };

        var result = ExportValidation.Validate(export, StorageOptions(), ClientDirectory(), NullLogger.Instance);

        result.Failure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BadRequest);
        result.ErrorMessage.Should().Contain("repository root");
    }

    [Fact]
    public void Validate_Fails_WhenDestinationIsOutsideEveryDepositBucket()
    {
        var export = new ExportResource
        {
            ArchivalGroup = new Uri("https://storage.test/repository/cc/thing"),
            Destination = new Uri("s3://someone-elses-bucket/export-of-my-ag")
        };

        var result = ExportValidation.Validate(export, StorageOptions(), ClientDirectory(), NullLogger.Instance);

        result.Failure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BadRequest);
    }

    [Theory]
    [InlineData(DefaultBucket)]
    [InlineData(ProfileBucket)]
    public void Validate_Succeeds_WhenEverythingIsValid(string bucket)
    {
        var export = new ExportResource
        {
            ArchivalGroup = new Uri("https://storage.test/repository/cc/thing"),
            Destination = new Uri($"s3://{bucket}/export-of-my-ag")
        };

        var result = ExportValidation.Validate(export, StorageOptions(), ClientDirectory(), NullLogger.Instance);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Stamp_SetsCreatedAndLastModified_AndDefaultsCreatedByToTheCaller()
    {
        var export = new ExportResource
        {
            ArchivalGroup = new Uri("https://storage.test/repository/cc/thing"),
            Destination = PermittedDestination
        };
        var before = DateTime.UtcNow;

        ExportValidation.Stamp(export, MakeConverters(), "caller");

        export.Created.Should().NotBeNull().And.BeOnOrAfter(before);
        export.LastModified.Should().Be(export.Created);
        export.CreatedBy.Should().NotBeNull();
        export.LastModifiedBy.Should().Be(export.CreatedBy);
    }

    [Fact]
    public void Stamp_TrustsASuppliedCreatedBy()
    {
        var suppliedCreatedBy = new Uri("https://storage.test/agents/someone-else");
        var export = new ExportResource
        {
            ArchivalGroup = new Uri("https://storage.test/repository/cc/thing"),
            Destination = PermittedDestination,
            CreatedBy = suppliedCreatedBy
        };

        ExportValidation.Stamp(export, MakeConverters(), "caller");

        export.CreatedBy.Should().Be(suppliedCreatedBy);
        export.LastModifiedBy.Should().Be(suppliedCreatedBy);
    }

    private static Converters MakeConverters() => new(
        Options.Create(new FedoraOptions
        {
            Root = new Uri("https://fedora.test/fcrepo/rest/"),
            AdminUser = "admin",
            AdminPassword = "admin",
            Bucket = "fedora-bucket",
            OcflS3Prefix = ""
        }),
        Options.Create(new ConverterOptions { StorageRoot = new Uri("https://storage.test/") }));
}
