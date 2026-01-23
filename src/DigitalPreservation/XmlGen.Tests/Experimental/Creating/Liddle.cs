using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Storage.Repository.Common.Mets;
using Storage.Repository.Common.Mets.StorageImpl;

namespace XmlGen.Tests.Experimental;

public class Liddle
{
    private readonly IMetsManager metsManager;
    private readonly MetsParser parser;

    public Liddle()
    {
        var serviceProvider = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        var factory = serviceProvider.GetService<ILoggerFactory>();
        var parserLogger = factory!.CreateLogger<MetsParser>();
        parser = new MetsParser(new FileSystemMetsLoader(), parserLogger);
        var storage = new FileSystemMetsStorage(parser);
        metsManager = new MetsManager(parser, storage, new MetadataManager());
    }

    [Fact(Skip = "Experimental")]
    public async Task Extended_Mets()
    {
        var mets = await Basic_Liddle();

        //await metsManager.WriteMets(mets);

        // The archivist doesn't assign any identifier to the root of the object
        // because it doesn't correspond to any particular archival record; it's neither a collection nor an item
        // (This may not happen IRL, it may be a business rule that a unit of preservation (the AG) always
        // corresponds to some archival record, whatever the level)

        // or...
        // The archivist assigns LIDDLE/WW1/TAPES/1-2 to the root of the object
        metsManager.SetRecordIdentifier(mets, "objects", "identity-service", "a8n9e4c2");
        metsManager.SetRecordIdentifier(mets, "objects", "EMu", "LIDDLE/WW1/TAPES/1-2");
        // This pair of tapes (i.e., physical objects) is LIDDLE/WW1/TAPES/1-2 

        // apply access condition and rights statement to origin, though
        metsManager.SetAccessRestrictions(mets, "objects", ["Level1"]);
        metsManager.SetRightsStatement(mets, "objects", new Uri("http://rightsstatements.org/vocab/InC/1.0/"));



        // The archivist creates a logical structure over the raw files for each interview, aligned with EMu archival description.
        var logSm = new LogicalRange
        {
            Id = "LOG_0000",
            Name = "Tapes 1 and 2",  // Unlikely
            Type = "Collection",     // ...but maybe?
            Ranges =
            [
                new LogicalRange
                {
                    Id = "LOG_0001",
                    Type = "Item",
                    Name = "ADDAMS-WILLIAMS, DONALD ARTHUR",
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
                                Value = "LIDDLE/WW1/XXX/1"
                            }
                        ]
                    },
                    Files = [
                        new FilePointer
                        {
                            LocalPath = "objects/tape1side1.wav",
                            BeginTime = 15.0,
                            EndTime = 2109.5
                        }
                    ]
                },
                new LogicalRange
                {
                    Id = "LOG_0002",
                    Type = "Item",
                    Name = "AITCHISON, BERTRAM STEWART",
                    RecordInfo = new RecordInfo
                    {
                        RecordIdentifiers = [
                            new RecordIdentifier
                            {
                                Source = "identity-service",
                                Value = "vvdd4433"
                            },
                            new RecordIdentifier
                            {
                                Source = "EMu",
                                Value = "LIDDLE/WW1/XXX/2"
                            }
                        ]
                    },
                    Files = [                     // spans sides 1 and 2
                        new FilePointer
                        {
                            LocalPath = "objects/tape1side1.wav",
                            BeginTime = 2200,
                            EndTime = 2700  // end of side 1
                        },
                        new FilePointer
                        {
                            LocalPath = "objects/tape1side2.wav",
                            BeginTime = 9.2, // start of side 2
                            EndTime = 1209  
                        }
                    ]
                },
                new LogicalRange
                {
                    Id = "LOG_0003",
                    Type = "Item",
                    Name = "ALEXANDER, C",
                    RecordInfo = new RecordInfo
                    {
                        RecordIdentifiers = [
                            new RecordIdentifier
                            {
                                Source = "identity-service",
                                Value = "adfa234d"
                            },
                            new RecordIdentifier
                            {
                                Source = "EMu",
                                Value = "LIDDLE/WW1/XXX/3"
                            }
                        ]
                    },
                    Files = [   
                        new FilePointer
                        {
                            LocalPath = "objects/tape1side2.wav",
                            BeginTime = 1250,
                            EndTime = 1900  // rest of side 2 blank
                        }
                    ]
                },
                new LogicalRange
                {
                    Id = "LOG_0004",
                    Type = "Item",
                    Name = "ALLANSON, CECIL JOHN LYONS",
                    RecordInfo = new RecordInfo
                    {
                        RecordIdentifiers = [
                            new RecordIdentifier
                            {
                                Source = "identity-service",
                                Value = "e34e2ads"
                            },
                            new RecordIdentifier
                            {
                                Source = "EMu",
                                Value = "LIDDLE/WW1/XXX/4"
                            }
                        ]
                    },
                    Files = [   
                        new FilePointer
                        {
                            LocalPath = "objects/tape2side1.wav",
                            BeginTime = 13.9,
                            EndTime = 2690.54  
                        },   
                        new FilePointer
                        {
                            LocalPath = "objects/tape2side2.wav",
                            BeginTime = 20.6,
                            EndTime = 1387.51  
                        }
                    ]
                }
            ]
        };
        metsManager.SetStructMap(mets, logSm);

        await metsManager.WriteMets(mets);
    }

    public async Task<FullMets> Basic_Liddle()
    {
        var name = "Liddle Tapes 1 and 2";
        var metsFi = new FileInfo("C:\\git\\uol-dlip\\design\\complex-mets\\liddle.mets.xml");
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
            LocalPath = "objects/tape1side1.wav",
            Digest = "abcd1234",
            ContentType = "audio/x-wav",
            Name = "Tape 1 Side 1",
            Size = 500000,
            Metadata = [
                new FileFormatMetadata
                {
                    Source = "Brunnhilde",
                    Digest = "abcd1234",
                    ContentType = "audio/x-wav",
                    FormatName = "Broadcast WAVE 0 Generic",
                    PronomKey = "fmt/1",
                    Size = 500000
                },
                new ExtentMetadata
                {
                    Source = "FFProbe",
                    Duration = 2704.7
                }
            ]
        });        
        metsManager.AddToMets(mets, new WorkingFile
        {
            LocalPath = "objects/tape1side2.wav",
            Digest = "3e421bb1",
            ContentType = "audio/x-wav",
            Name = "Tape 1 Side 2",
            Size = 500001,
            Metadata = [
                new FileFormatMetadata
                {
                    Source = "Brunnhilde",
                    Digest = "3e421bb1",
                    ContentType = "audio/x-wav",
                    FormatName = "Broadcast WAVE 0 Generic",
                    PronomKey = "fmt/1",
                    Size = 500001
                },
                new ExtentMetadata
                {
                    Source = "FFProbe",
                    Duration = 2720.1
                }
            ]
        });        
        metsManager.AddToMets(mets, new WorkingFile
        {
            LocalPath = "objects/tape2side1.wav",
            Digest = "d4d4e3e3",
            ContentType = "audio/x-wav",
            Name = "Tape 2 Side 1",
            Size = 499999,
            Metadata = [
                new FileFormatMetadata
                {
                    Source = "Brunnhilde",
                    Digest = "d4d4e3e3",
                    ContentType = "audio/x-wav",
                    FormatName = "Broadcast WAVE 0 Generic",
                    PronomKey = "fmt/1",
                    Size = 499999
                }
            ]
        });        
        metsManager.AddToMets(mets, new WorkingFile
        {
            LocalPath = "objects/tape2side2.wav",
            Digest = "a2d3e4f5",
            ContentType = "audio/x-wav",
            Name = "Tape 2 Side 2",
            Size = 500100,
            Metadata = [
                new FileFormatMetadata
                {
                    Source = "Brunnhilde",
                    Digest = "a2d3e4f5",
                    ContentType = "audio/x-wav",
                    FormatName = "Broadcast WAVE 0 Generic",
                    PronomKey = "fmt/1",
                    Size = 500100
                }
            ]
        });
        return mets;
    }

}