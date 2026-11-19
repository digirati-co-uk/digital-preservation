using Microsoft.Extensions.Options;
using Preservation.API.Mutation;

namespace Preservation.API.Tests.TestingInfrastructure;

/// <summary>
/// The same <c>new ResourceMutator(Options.Create(new MutatorOptions{...}))</c> was written out
/// about 14 times across this project, always with the same two URLs (issue #388 item 5.2).
/// </summary>
public static class TestMutator
{
    public static ResourceMutator Make(
        string storage = "https://storage.test",
        string preservation = "https://preservation.test") =>
        new(Options.Create(new MutatorOptions { Storage = storage, Preservation = preservation }));
}
