using Amazon.S3;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Storage.Repository.Common.Mets;

namespace XmlGen.Tests.Experimental;

public class ExtendedMets
{
    
    private readonly IMetsManager metsManager;
    private readonly MetsParser parser;
    
    public ExtendedMets()
    {
        var serviceProvider = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        var factory = serviceProvider.GetService<ILoggerFactory>();
        var parserLogger = factory!.CreateLogger<MetsParser>();
        var s3Client = new Mock<IAmazonS3>().Object;
        parser = new MetsParser(s3Client, parserLogger);
        metsManager = new MetsManager(parser, s3Client);
    }

    [Fact]
    public async Task Extended_Mets()
    {
        var mets = await Basic_Women_of_Westminster();
        
        
        // The archivist assigns MS 2249 to the root of the object
        metsManager.SetRecordIdentifier(mets, "objects", "identity-service", "b6n9e4c2");
        metsManager.SetRecordIdentifier(mets, "objects", "EMu", "MS 2249");
        
        
        // apply access condition and rights statement to origin
        metsManager.SetAccessRestrictions(mets, "objects", ["Level1"]);
        metsManager.SetRightsStatement(mets, "objects", new Uri("http://rightsstatements.org/vocab/InC/1.0/"));
        
        
        // The archivist marks some files as not for publication
        metsManager.SetAccessRestrictions(mets, "objects/angela-eagle-redacted.m4a", ["Level2"]);
        metsManager.SetRightsStatement(mets, "objects/angela-eagle-redacted.m4a", null); //???
        
        
        // The archivist creates a "presentation" structure over the raw files, aligned with EMu archival description.
        var logSm = new LogicalRange
        {
            Id = "LOG_0000",
            Name = "Women of Westminster",
            Type = "Collection",
            Ranges =
            [
                new LogicalRange
                {
                    Id = "LOG_0001",
                    Type = "Item",
                    Name = "Amber Rudd",
                    RecordInfo = new RecordInfo
                    {
                        RecordIdentifiers = [
                            new RecordIdentifier
                            {
                                Source = "identity-service",
                                Value = "mg56cva7"
                            },
                            new RecordIdentifier
                            {
                                Source = "EMu",
                                Value = "MS 2249/1"
                            }
                        ]
                    },
                    Files = [
                        new FilePointer { LocalPath = "objects/amber-rudd.m4a" },
                        new FilePointer { LocalPath = "objects/amber-rudd.docx" }
                    ]
                },
                new LogicalRange
                {
                    Id = "LOG_0002",
                    Type = "Item",
                    Name = "Angela Eagle",
                    RecordInfo = new RecordInfo
                    {
                        RecordIdentifiers = [
                            new RecordIdentifier
                            {
                                Source = "identity-service",
                                Value = "hh43pd32"
                            },
                            new RecordIdentifier
                            {
                                Source = "EMu",
                                Value = "MS 2249/2"
                            }
                        ]
                    },
                    Files = [
                        new FilePointer { LocalPath = "objects/angela-eagle-redacted.m4a" },
                        new FilePointer { LocalPath = "objects/angela-eagle-transcript.docx" }
                    ]
                }
            ]
        };
        metsManager.SetStructMap(mets, logSm);
        // I have just set the whole structMap in one go.
        // How do I patch it? e.g.,
        //  - add a third section
        //  - set the recordInfo on that section in a separate operation
        
        
        // The archivist asserts that the transcripts are _supplementing_ the audio files
        var supplementing = new Uri("http://iiif.io/api/presentation/3#supplementing"); // this will be a const somewhere
        metsManager.LinkFile(mets, "objects/amber-rudd.m4a", "objects/amber-rudd.docx", supplementing);
        metsManager.LinkFile(mets, "objects/angela-eagle-redacted.m4a", "objects/angela-eagle-transcript.docx", supplementing);

        await metsManager.WriteMets(mets);
    }
    
    public async Task<FullMets> Basic_Women_of_Westminster()
    {
        var name = "Women of Westminster";
        var metsFi = new FileInfo("C:\\git\\uol-dlip\\design\\complex-mets\\wow.example.mets.xml");
        var metsUri = new Uri(metsFi.FullName);
        var result = await metsManager.CreateStandardMets(new Uri(metsFi.FullName), name);

        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNull();
        var metsWrapper = result.Value!;
        var metsResult = await metsManager.GetFullMets(metsUri, metsWrapper.ETag!);
        var mets = metsResult.Value!;
        mets.Should().NotBeNull();

        metsManager.AddToMets(mets, new WorkingFile
        {
            LocalPath = "objects/amber-rudd.m4a",
            Digest = "abcd1234",
            ContentType = "audio/m4a",
            Name = "Amber Rudd.m4a",
            Size = 1000,
            Metadata = [
                new FileFormatMetadata
                {
                    Source = "Brunnhilde",
                    Digest = "abcd1234",
                    ContentType = "audio/m4a",
                    FormatName = "M4A Audio",
                    PronomKey = "fmt/100",
                    Size = 1000
                }
            ]
        });
        metsManager.AddToMets(mets, new WorkingFile
        {
            LocalPath = "objects/amber-rudd.docx",
            Digest = "1234abcd",
            ContentType = "application/msword",
            Name = "Amber Rudd Transcript.docx",
            Size = 500,
            Metadata = [
                new FileFormatMetadata
                {
                    Source = "Brunnhilde",
                    Digest = "1234abcd",
                    ContentType = "application/msword",
                    FormatName = "Microsoft Word",
                    PronomKey = "fmt/200",
                    Size = 500
                }
            ]
        });

        metsManager.AddToMets(mets, new WorkingFile
        {
            LocalPath = "objects/angela-eagle.m4a",
            Digest = "aabbccdd",
            ContentType = "audio/m4a",
            Name = "Angela Eagle.m4a",
            Size = 2000,
            Metadata = [
                new FileFormatMetadata
                {
                    Source = "Brunnhilde",
                    Digest = "aabbccdd",
                    ContentType = "audio/m4a",
                    FormatName = "M4A Audio",
                    PronomKey = "fmt/100",
                    Size = 2000
                }
            ]
        });
        metsManager.AddToMets(mets, new WorkingFile
        {
            LocalPath = "objects/angela-eagle-redacted.m4a",
            Digest = "99887766",
            ContentType = "audio/m4a",
            Name = "Angela Eagle - redacted.m4a",
            Size = 1500,
            Metadata = [
                new FileFormatMetadata
                {
                    Source = "Brunnhilde",
                    Digest = "99887766",
                    ContentType = "audio/m4a",
                    FormatName = "M4A Audio",
                    PronomKey = "fmt/100",
                    Size = 1500
                }
            ]
        });
        metsManager.AddToMets(mets, new WorkingFile
        {
            LocalPath = "objects/angela-eagle-transcript.docx",
            Digest = "a1b2c3d4",
            ContentType = "application/msword",
            Name = "Angela Eagle redacted transcript.docx",
            Size = 600,
            Metadata = [
                new FileFormatMetadata
                {
                    Source = "Brunnhilde",
                    Digest = "a1b2c3d4",
                    ContentType = "application/msword",
                    FormatName = "Microsoft Word",
                    PronomKey = "fmt/200",
                    Size = 600
                }
            ]
        });

        return mets;
    }

}