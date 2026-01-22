using Amazon.S3;
using FakeItEasy;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Storage.Repository.Common;

namespace Storage.API.Tests.S3;

public class StorageTests
{
    private Repository.Common.Storage storage;

    private IAmazonS3 s3Client;
    private IOptions<AwsStorageOptions> options;
    private ILogger<Repository.Common.Storage> logger;

    public StorageTests()
    {
        s3Client = A.Fake<IAmazonS3>();
        options = A.Fake<IOptions<AwsStorageOptions>>();
        logger = A.Fake<ILogger<Repository.Common.Storage>>();
        storage = new Repository.Common.Storage(s3Client, options, logger);
    }

    [Fact]
    public async Task InitialiseObject()
    {
        storage.Should().NotBeNull();
    }

    //TODO: Add tests


}
