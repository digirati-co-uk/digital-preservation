using Storage.API.Fedora;
using Test.Helpers;

namespace Storage.API.Tests.Fedora;

public class OcflTests : IClassFixture<DigitalPreservationAppFactory<Program>>
{
    private IStorageMapper storageMapper;
    
    public OcflTests(DigitalPreservationAppFactory<Program> factory)
    {
        storageMapper = factory.Services.GetService(typeof(IStorageMapper)) as IStorageMapper;
    }
    
    [Fact]
    public async Task Repository_Path()
    {
        var fedoraUri = new Uri("https://fedora-dev.dlip.digirati.io/fcrepo/rest/import-tests/28-10-24/ag-5");
        
        var path = storageMapper.GetArchivalGroupOrigin(fedoraUri);

        path.Should().Be("initial/e39/a0a/fe2/e39a0afe2f7c65bd06bd4fcdb18b1b9247caa2e45417f9d6084fa3ba8cd1fcd5");
    }
    
    // public string? GetArchivalGroupOrigin(Uri archivalGroupUri)
    // {
    //     var idPart = GetResourcePathPart(archivalGroupUri);
    //     if (idPart == null)
    //     {
    //         return null;
    //     }
    //     return RepositoryPath.RelativeToRoot("initial/", idPart);
    // }
    //
    // public string? GetResourcePathPart(Uri fedoraOrStorageUri)
    // {
    //     var s = fedoraOrStorageUri.ToString();
    //     if (s.StartsWith(fedoraRoot))
    //     {
    //         return s.RemoveStart(fedoraRoot)!;
    //     }
    //     if (s.StartsWith(repositoryRoot))
    //     {
    //         return s.RemoveStart(repositoryRoot)!;
    //     }
    //     return null;
    // }
}