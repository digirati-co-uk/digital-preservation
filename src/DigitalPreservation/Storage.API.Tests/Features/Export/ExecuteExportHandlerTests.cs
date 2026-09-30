using Amazon.S3;
using DigitalPreservation.Common.Model.Results;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Storage.API.Fedora;
using Storage.API.Features.Export;
using Storage.API.Features.Export.Requests;
using ExportResource = DigitalPreservation.Common.Model.Export.Export;

namespace Storage.API.Tests.Features.Export;

/// <summary>
/// /exportMetsOnly calls ExecuteExport with a null identifier, so export.Id stays null too. The
/// catch block's error id was built as new Uri(export.Id + "#error") - a bare "#error" is a relative
/// URI, and Uri's constructor throws for it, so an exception during a METS-only export escaped this
/// handler as a 500 instead of the errors array every other export failure is reported in
/// (issue #273 item 2).
/// </summary>
public class ExecuteExportHandlerTests
{
    [Fact]
    public async Task A_Failure_With_No_Identifier_Is_Reported_In_Errors_With_A_Null_Id_Not_Thrown()
    {
        var storageMapper = A.Fake<IStorageMapper>();
        A.CallTo(() => storageMapper.GetStorageMap(A<Uri>._, A<string?>._))
            .Throws(new InvalidOperationException("no such storage map"));
        var handler = new ExecuteExportHandler(
            storageMapper, A.Fake<IAmazonS3>(), A.Fake<IExportResultStore>(), NullLogger<ExecuteExportHandler>.Instance);
        var export = new ExportResource
        {
            ArchivalGroup = new Uri("https://storage.test/repository/cc/thing"),
            Destination = new Uri("s3://export-bucket/somewhere/")
        };

        var act = () => handler.Handle(new ExecuteExport(null, export, metsOnly: true), CancellationToken.None);

        var result = await act.Should().NotThrowAsync();
        result.Subject.Success.Should().BeTrue(
            "an export failure is reported in the result's errors, not as a failed Result");
        result.Subject.Value!.Errors.Should().ContainSingle();
        result.Subject.Value.Errors![0].Id.Should().BeNull();
        result.Subject.Value.Errors![0].Message.Should().Be("no such storage map");
    }
}
