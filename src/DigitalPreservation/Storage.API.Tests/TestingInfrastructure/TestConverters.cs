using Microsoft.Extensions.Options;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;

namespace Storage.API.Tests.TestingInfrastructure;

/// <summary>
/// MakeConverters was copied into QueueImportJobHandlerTests, ExportMetsOnlyControllerTests,
/// QueueExportHandlerTests and RepositoryPathTests, and it had already diverged:
/// QueueImportJobHandlerTests' own constructor built a Converters with StorageRoot
/// "https://storage.test/repository/", while its private MakeConverters() (used by two of its own
/// tests) built one with "https://storage.test/" - no trailing path - without anyone having
/// noticed or reconciled it (issue #388 item 5.2). The parameters here are optional precisely so
/// that divergence, and RepositoryPathTests' own deliberately different Fedora root/bucket/prefix,
/// can be stated explicitly at the call site instead of hidden in a second private copy.
/// </summary>
public static class TestConverters
{
    public static Converters Make(
        Uri? fedoraRoot = null,
        Uri? storageRoot = null,
        string adminUser = "admin",
        string adminPassword = "admin",
        string bucket = "fedora-bucket",
        string ocflS3Prefix = "") =>
        new(
            Options.Create(new FedoraOptions
            {
                Root = fedoraRoot ?? new Uri("https://fedora.test/fcrepo/rest/"),
                AdminUser = adminUser,
                AdminPassword = adminPassword,
                Bucket = bucket,
                OcflS3Prefix = ocflS3Prefix
            }),
            Options.Create(new ConverterOptions { StorageRoot = storageRoot ?? new Uri("https://storage.test/") }));
}
