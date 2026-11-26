using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Storage;
using FakeItEasy;
using Storage.API.Fedora;
using Storage.API.Features.Import;

namespace Storage.API.Tests.Features.Import;

/// <summary>
/// Defaults for the calls issue #444 and #459 added around the commit, so tests written before them
/// keep exercising what they were written for.
/// </summary>
internal static class ImportFakes
{
    /// <summary>The store accepts the commit-started record (issue #444).</summary>
    public static void AcceptsCommitRecord(IImportJobResultStore store) =>
        A.CallTo(() => store.RecordCommitStarted(A<string>._, A<ImportJobResult>._, A<Uri>._, A<CancellationToken>._))
            .Returns(Result.OkNotNull(true));

    /// <summary>
    /// Fedora lists as many versions as the head version the latest S3 read returned, so the commit
    /// check's Fedora side (issue #459) agrees with S3.
    /// </summary>
    public static void FedoraAgreesWithS3(IFedoraClient fedoraClient) =>
        A.CallTo(() => fedoraClient.GetFedoraVersionCount(A<string?>._))
            .ReturnsLazily(async () =>
            {
                var lastRead = Fake.GetCalls(fedoraClient)
                    .LastOrDefault(call => call.Method.Name == nameof(IFedoraClient.GetArchivalGroupVersion));
                var version = lastRead?.ReturnValue is Task<Result<ObjectVersion>> task ? (await task).Value?.OcflVersion : null;
                return version != null && int.TryParse(version.TrimStart('v'), out var number)
                    ? Result.OkNotNull(number)
                    : Result.FailNotNull<int>(ErrorCodes.NotFound, "no versions");
            });
}
