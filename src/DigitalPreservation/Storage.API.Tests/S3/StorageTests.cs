using Amazon.Runtime.Internal.Util;
using Amazon.S3;
using Amazon.S3.Model;
using Azure.Core;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Transit;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Storage.Repository.Common;
using System.Net;
using System.Text;
using System.Text.Json;


namespace Storage.API.Tests.S3;

public class StorageTests
{
    private readonly IAmazonS3 s3Client;
    private readonly IOptions<AwsStorageOptions> options;
    private readonly ILogger<Repository.Common.Storage> logger;
    private readonly IStorage storage;

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
        
        storage.Should().NotBeNull(); }

    [Fact] public void GetWorkingFilesLocation_FileExistsConflict()
    {
        var expectedPath = @"C:\temp\working";
        A.CallTo(() => options.Value).Returns(new AwsStorageOptions
        {
            DefaultWorkingBucket = expectedPath
        });


        //File exists
        A.CallTo(() => s3Client.GetObjectAsync(
                A<GetObjectRequest>.Ignored, CancellationToken.None))
            .Returns(Task.FromResult(new GetObjectResponse()
            {
                HttpStatusCode = HttpStatusCode.OK,
            }));

        // Provide required parameters for GetWorkingFilesLocation
        var idPart = "testId";
        var templateType = TemplateType.None; 
        var resultTask = storage.GetWorkingFilesLocation(idPart, templateType);
        var result = resultTask.GetAwaiter().GetResult();

        // Adjust assertion as needed based on actual return type
        result.Failure.Should().BeTrue();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("Conflict");

    }


    [Fact]
    public void GetWorkingFilesLocation_Saves3File()
    {
        var expectedPath = @"C:\temp\working";
        _ = A.CallTo(() => options.Value).Returns(new AwsStorageOptions
        {
            DefaultWorkingBucket = expectedPath
        });


        //File doesn't exist
        A.CallTo(() => s3Client.GetObjectAsync(
                A<GetObjectRequest>.Ignored, CancellationToken.None))
            .Throws(new AmazonS3Exception("Not found")
            {
                ErrorCode = "NotFound",
                StatusCode = HttpStatusCode.NotFound
            });


        // Simulate successful PutObjectAsync
        A.CallTo(() => s3Client.PutObjectAsync(A<PutObjectRequest>.Ignored, CancellationToken.None))
            .Returns(Task.FromResult(new PutObjectResponse()
            {
                HttpStatusCode = HttpStatusCode.Created,
            }));
        
        // Provide required parameters for GetWorkingFilesLocation
        var idPart = "testId";
        var templateType = TemplateType.RootLevel;
        var resultTask = storage.GetWorkingFilesLocation(idPart, templateType);
        var result = resultTask.GetAwaiter().GetResult();

        // Adjust assertion as needed based on actual return type
        result.Failure.Should().BeFalse();
        result.Success.Should().BeTrue();
    }


    [Fact]
    public void GetWorkingFilesLocation_Saves3FileBagIt()
    {
        var expectedPath = @"C:\temp\working";
        _ = A.CallTo(() => options.Value).Returns(new AwsStorageOptions
        {
            DefaultWorkingBucket = expectedPath
        });


        //File doesn't exist
        A.CallTo(() => s3Client.GetObjectAsync(
                A<GetObjectRequest>.Ignored, CancellationToken.None))
            .Throws(new AmazonS3Exception("Not found")
            {
                ErrorCode = "NotFound",
                StatusCode = HttpStatusCode.NotFound
            });
        
        // Simulate successful PutObjectAsync
        A.CallTo(() => s3Client.PutObjectAsync(A<PutObjectRequest>.Ignored, CancellationToken.None))
            .Returns(Task.FromResult(new PutObjectResponse()
            {
                HttpStatusCode = HttpStatusCode.Created,
            }));

        // Provide required parameters for GetWorkingFilesLocation
        var idPart = "testId";
        var templateType = TemplateType.BagIt;
        var resultTask = storage.GetWorkingFilesLocation(idPart, templateType);
        var result = resultTask.GetAwaiter().GetResult();

        // Adjust assertion as needed based on actual return type
        result.Failure.Should().BeFalse();
        result.Success.Should().BeTrue();
    }


    [Fact]
    public async Task GetListing()
    {
        A.CallTo(() => s3Client.ListObjectsV2Async(
            A<ListObjectsV2Request>.Ignored, CancellationToken.None))
            .Returns(Task.FromResult(new ListObjectsV2Response()
            {
                S3Objects = new List<S3Object>
                {
                    new S3Object { Key = "testId/file1.txt" },
                    new S3Object { Key = "testId/file2.txt" }
                }
            }));

        var uri = new Uri("https://example-bucket.s3.amazonaws.com/mets/mets.xml");
        var subpath = "/mets/mets/mets";
        var result = await storage.GetListing(uri, subpath);

        result.Count.Should().Be(2);
    }

    [Fact]
    public async Task Exists()
    {
        //File exists
        A.CallTo(() => s3Client.GetObjectAsync(
                A<GetObjectRequest>.Ignored, CancellationToken.None))
            .Returns(Task.FromResult(new GetObjectResponse()
            {
                HttpStatusCode = HttpStatusCode.OK,
            }));
        
        var uri = new Uri("https://example-bucket.s3.amazonaws.com/mets/mets.xml");
        var result = await storage.Exists(uri);
        result.Should().BeTrue();

    }

    [Fact] 
    public async Task NotExists()
    {
        //File doesn't exist
        A.CallTo(() => s3Client.GetObjectAsync(
                A<GetObjectRequest>.Ignored, CancellationToken.None))
            .Throws(new AmazonS3Exception("Not found")
            {
                ErrorCode = "NotFound",
                StatusCode = HttpStatusCode.NotFound
            });
        
        var uri = new Uri("https://example-bucket.s3.amazonaws.com/mets/mets.xml");
        var result = await storage.Exists(uri);
        result.Should().BeFalse();
    }


    [Fact]
    public async Task AddToDepositFileSystem_Success()
    {
        var dir = new WorkingDirectory
        {
            LocalPath = "/some/local/path",
            Name = "name.txt",
            Modified = DateTime.Now
        };

        var serializedDir = JsonSerializer.Serialize(dir);

        //File exists
        A.CallTo(() => s3Client.GetObjectAsync(
                A<GetObjectRequest>.Ignored, CancellationToken.None))
            .Returns(Task.FromResult(new GetObjectResponse()
            {
                HttpStatusCode = HttpStatusCode.OK,
                ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(serializedDir))
                
            }));


        // Simulate successful PutObjectAsync
        A.CallTo(() => s3Client.PutObjectAsync(A<PutObjectRequest>.Ignored, CancellationToken.None))
            .Returns(Task.FromResult(new PutObjectResponse()
            {
                HttpStatusCode = HttpStatusCode.Created,
            }));

        var uri = new UriBuilder("s3://folder/folder/somefile.txt").Uri;


  
        var resultTask = storage.AddToDepositFileSystem(uri, dir, CancellationToken.None);
        var result = resultTask.GetAwaiter().GetResult();

        result.Failure.Should().BeFalse();
        result.Success.Should().BeTrue();
    }



    [Fact]
    public async Task AddToDepositFileSystem_BadWorkingDir()
    {
        var dir = new WorkingDirectory
        {
            LocalPath = "/some/local/path",
            Name = "name.txt",
            Modified = DateTime.Now
        };

        var uri = new UriBuilder("s3://folder/folder/somefile.txt").Uri;

        //File exists
        A.CallTo(() => s3Client.GetObjectAsync(
                A<GetObjectRequest>.Ignored, CancellationToken.None))
            .Returns(Task.FromResult(new GetObjectResponse()
            {
                HttpStatusCode = HttpStatusCode.OK,
                ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes("bad"))

            }));


        // Simulate successful PutObjectAsync
        A.CallTo(() => s3Client.PutObjectAsync(A<PutObjectRequest>.Ignored, CancellationToken.None))
            .Returns(Task.FromResult(new PutObjectResponse()
            {
                HttpStatusCode = HttpStatusCode.Created,
            }));

       
        var resultTask = storage.AddToDepositFileSystem(uri, dir, CancellationToken.None);
        var result = resultTask.GetAwaiter().GetResult();

        result.Failure.Should().BeTrue();
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task AddToDepositFileSystem_SavesNewFile()
    {
        var dir = new WorkingDirectory
        {
            LocalPath = "/some/local/path",
            Name = "name.txt",
            Modified = DateTime.Now
        };

        var file = new WorkingFile()
        {
            LocalPath = "/some/local/path/file.txt",
            Name = "file.txt",
            Modified = DateTime.Now,
            Size = 1234,
            ContentType = "text/plain"
        };

        var serializedDir = JsonSerializer.Serialize(dir);
        var uri = new UriBuilder("s3://folder/folder/somefile.txt").Uri;

        //File exists
        A.CallTo(() => s3Client.GetObjectAsync(
                A<GetObjectRequest>.Ignored, CancellationToken.None))
            .Returns(Task.FromResult(new GetObjectResponse()
            {
                HttpStatusCode = HttpStatusCode.OK,
                ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(serializedDir))

            }));


        // Simulate successful PutObjectAsync
        A.CallTo(() => s3Client.PutObjectAsync(A<PutObjectRequest>.Ignored, CancellationToken.None))
            .Returns(Task.FromResult(new PutObjectResponse()
            {
                HttpStatusCode = HttpStatusCode.Created,
            }));

        var resultTask = storage.AddToDepositFileSystem(uri, file, CancellationToken.None);
        var result = resultTask.GetAwaiter().GetResult();

        result.Failure.Should().BeFalse(); 
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task AddToDepositFileSystem_SavesNewFile_Error()
    {
        var file = new WorkingFile()
        {
            LocalPath = "/some/local/path/file.txt",
            Name = "file.txt",
            Modified = DateTime.Now,
            Size = 1234,
            ContentType = "text/plain"
        };

        var uri = new UriBuilder("s3://folder/folder/somefile.txt").Uri;
        var resultTask = storage.AddToDepositFileSystem(uri, file, CancellationToken.None);
        var result = resultTask.GetAwaiter().GetResult();

        result.Failure.Should().BeTrue();
        result.Success.Should().BeFalse();
    }


    [Fact]
    public async Task DeleteFromDepositFileSystem()
    {
        var uri = new UriBuilder("s3://b/b/b/b.rxt").Uri;
        var path = "/src/";
        var errorFound = false;

        var dir = new WorkingDirectory
        {
            LocalPath = "/some/local/path",
            Name = "name.txt",
            Modified = DateTime.Now
        };

        var serializedDir = JsonSerializer.Serialize(dir);


        //File exists
        A.CallTo(() => s3Client.GetObjectAsync(
                A<GetObjectRequest>.Ignored, CancellationToken.None))
            .Returns(Task.FromResult(new GetObjectResponse()
            {
                HttpStatusCode = HttpStatusCode.OK,
                ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(serializedDir))

            }));
        
        var resultTask = storage.DeleteFromDepositFileSystem(uri, path, errorFound , CancellationToken.None);
        var result = resultTask.GetAwaiter().GetResult();

        result.Failure.Should().BeFalse();
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task GenerateDepositFileSystem_CreatesNewFileSystem()
    {
        var uri = new UriBuilder("s3://b/b/b/b.rxt").Uri;

        A.CallTo(() => s3Client.ListObjectsV2Async(
                A<ListObjectsV2Request>.Ignored, CancellationToken.None))
            .Returns(Task.FromResult(new ListObjectsV2Response()
            {
                S3Objects = new List<S3Object>
                {
                    new S3Object { Key = "testId/file1.txt" },
                    new S3Object { Key = "testId/file2.txt" }
                }
            }));

        var resultTask = storage.GenerateDepositFileSystem(uri, false, null, CancellationToken.None);
        var result = resultTask.GetAwaiter().GetResult();
        
        result.Failure.Should().BeFalse();
        result.Success.Should().BeTrue();
    }

}
