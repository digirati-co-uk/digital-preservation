using Preservation.API.Tests.TestingInfrastructure;

namespace Preservation.API.Tests.Integration;

/// <summary>
/// Placeholder membership of the database collection; real integration coverage
/// that exercises the <see cref="DatabaseFixture"/> lives in the Features tests.
/// </summary>
[Trait("Category", "Integration")]
[Collection(DatabaseCollection.CollectionName)]
public class DatabaseTest;