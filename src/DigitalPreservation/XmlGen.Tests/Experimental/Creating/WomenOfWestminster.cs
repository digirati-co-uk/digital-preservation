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

public class WomenOfWestminster
{
    private readonly IMetsManager metsManager;
    private readonly MetsParser parser;

    private const string MetsFilePath = "C:\\git\\uol-dlip\\design\\complex-mets\\wow.example.mets.xml";

    public WomenOfWestminster()
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
        var metsFi = new FileInfo(MetsFilePath);
        var metsUri = new Uri(metsFi.FullName);
        var mets = await Basic_Women_of_Westminster(metsUri);


        // The archivist assigns MS 2249 to the root of the object
        metsManager.SetRecordIdentifier(mets, "objects", "identity-service", "b6n9e4c2");
        metsManager.SetRecordIdentifier(mets, "objects", "EMu", "MS 2249");


        // apply access condition and rights statement to origin
        metsManager.SetAccessRestrictions(mets, "objects", ["Level1"]);
        metsManager.SetRightsStatement(mets, "objects", new Uri("http://rightsstatements.org/vocab/InC/1.0/"));


        // The archivist marks some files as not for publication
        metsManager.SetAccessRestrictions(mets, "objects/angela-eagle-redacted.m4a", ["Closed"]);
        metsManager.SetRightsStatement(mets, "objects/angela-eagle-redacted.m4a", null); //???


        // The archivist creates a "presentation" structure over the raw files, aligned with EMu archival description.
        var logSm = new LogicalRange
        {
            Id = "LOG_0000", // Should we assign these? Or should MetsManager mint them? 
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



        // The archivist asserts that the transcripts are _supplementing_ the audio files
        var supplementing = new Uri("http://iiif.io/api/presentation/3#supplementing"); // this will be a const somewhere
        metsManager.LinkFile(mets, "objects/amber-rudd.m4a", "objects/amber-rudd.docx", supplementing);
        metsManager.LinkFile(mets, "objects/angela-eagle-redacted.m4a", "objects/angela-eagle-transcript.docx", supplementing);

        //
        await metsManager.WriteMets(mets);

        // Later
        var metsResult = await metsManager.GetFullMets(metsUri, null);
        mets = metsResult.Value!;

        // I have just set the whole structMap in one go.
        // How do I patch it? e.g.,
        //  - add a third section
        //  - set the recordInfo on that section in a separate operation

        // here's me addressing a logical div to apply a rights statement
        metsManager.SetRightsStatement(mets, "LOG_0002", new Uri("http://rightsstatements.org/vocab/InC/1.0/"));

        // OK let's randomly add a folder
        metsManager.AddToMets(mets, new WorkingDirectory
        {
            LocalPath = "objects/child-folder",
            Name = "Child Folder"
        });
        metsManager.AddToMets(mets, new WorkingFile
        {
            LocalPath = "objects/child-folder/bercow-notes.txt",
            Digest = "b42a6e9c",
            ContentType = "text/plain",
            Name = "Bercow notes.txt",
            Size = 200,
            Metadata = [
                new FileFormatMetadata
                {
                    Source = "Brunnhilde",
                    Digest = "b42a6e9c",
                    ContentType = "text/plain",
                    FormatName = "Text File",
                    PronomKey = "fmt/101",
                    Size = 200
                }
            ]
        });

        // but now I want to manipulate a logical struct map and update it

        // going to need to save it before it can be parsed
        await metsManager.WriteMets(mets);
        // That's where it feels a bit off - like I shouldn't have to do that - but then:
        var parseResult = await parser.GetMetsFileWrapper(metsUri, true);
        var pMets = parseResult.Value;

        // Should MetsManager be holding a parsed METS?
        // ...that it keeps up-to-date on every operation, so you can always obtain it after any changing operation
        // ...and carry on making changes without a cycle of saving?

        // How is a UI going to deal with this?
        // Compare iiif-vault, for binding a IIIF representation to UI.
        // We could do the same for METS - but it that overkill?

        // We are passing in our simplified structures to IMetsManager operations, which 
        // under the hood is turning them into the XmlGen classes that are the "real" METS 
        // (because we don't want to deal with those classes at the API level)
        // This is where it differs from iiif-vault, where there is only the IIIF model, 
        // both bound to the UI and serialised to JSON.

        // So in our world, you can make a series of changes to a METS file via IMetsManager operations,
        // but any originally acquired representation is static, it won't change; you need to re-acquire it:

        LogicalRange reloadedLogSm = pMets!.LogicalStructures[0];

        // right now, this should be true:
        reloadedLogSm.Should().BeEquivalentTo(logSm);

        reloadedLogSm.Ranges.Add(new LogicalRange
        {
            Id = "LOG_000Bercow",
            Type = "Item",
            Name = "Bercow notes",
            RecordInfo = new RecordInfo
            {
                RecordIdentifiers =
                [
                    new RecordIdentifier
                    {
                        Source = "identity-service",
                        Value = "a2c4e5b1"
                    },
                    new RecordIdentifier
                    {
                        Source = "EMu",
                        Value = "MS 2249/B"
                    }
                ]
            },
            Files =
            [
                new FilePointer { LocalPath = "objects/child-folder/bercow-notes.txt" }
            ]
        });

        // That was a "parsing" session, now I need a "managing" session"
        var metsResult2 = await metsManager.GetFullMets(metsUri, null);
        mets = metsResult2.Value!;

        // apply the whole edited structmap - not patch it. Is that ok?
        metsManager.SetStructMap(mets, reloadedLogSm);
        await metsManager.WriteMets(mets);
    }

    public async Task<FullMets> Basic_Women_of_Westminster(Uri metsUri)
    {
        var name = "Women of Westminster";
        var result = await metsManager.CreateStandardMets(metsUri, name);

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