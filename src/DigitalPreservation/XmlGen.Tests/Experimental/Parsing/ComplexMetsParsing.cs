using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.XmlGen.Mets;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Storage.Repository.Common.Mets;
using Storage.Repository.Common.Mets.StorageImpl;

namespace XmlGen.Tests.Experimental;

public class ComplexMetsParsing
{
    private readonly MetsParser parser;
    
    public ComplexMetsParsing()
    {
        var serviceProvider = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        var factory = serviceProvider.GetService<ILoggerFactory>();
        var parserLogger = factory!.CreateLogger<MetsParser>();
        var metsLoader = new FileSystemMetsLoader();
        parser = new MetsParser(metsLoader, parserLogger);
    }

    [Fact]
    public async Task Can_Parse_Women_Of_Westminster()
    {
        var wowMets = new FileInfo("Samples/wow.mets.xml");
        var result = await parser.GetMetsFileWrapper(new Uri(wowMets.FullName));
        
        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Self.Should().NotBeNull();
        result.Value.Self!.Digest.Should().NotBeEmpty();
        var phys = result.Value!.PhysicalStructure;
        phys!.Files.Should().Contain(f => f.Name == "wow.mets.xml");
        
        
        result.Value.Name.Should().Be("Women of Westminster");
        phys.Directories.Should().HaveCount(1);
        var objects = phys.Directories[0];
        objects.Name.Should().Be(FolderNames.Objects);
        objects.Files.Should().HaveCount(5);
        
        // Explicitly set access restrictions on objects folder
        objects.AccessRestrictions.Should().HaveCount(1);
        objects.AccessRestrictions[0].Should().Be("Level1");
        // ... and these are matched by effective access restrictions
        objects.EffectiveAccessRestrictions.Should().HaveCount(1);
        objects.EffectiveAccessRestrictions[0].Should().Be("Level1");
        var inCopyright = new Uri("http://rightsstatements.org/vocab/InC/1.0/");
        
        // and similarly for rights:
        objects.RightsStatement.Should().Be(inCopyright);
        objects.EffectiveRightsStatement.Should().Be(inCopyright);
        
        // ... and recordInfo:
        objects.RecordInfo.Should().NotBeNull();
        objects.RecordInfo!.RecordIdentifiers.Should().HaveCount(2);
        objects.RecordInfo.RecordIdentifiers[0].Source.Should().Be("identity-service");
        objects.RecordInfo.RecordIdentifiers[0].Value.Should().Be("b6n9e4c2");
        objects.RecordInfo.RecordIdentifiers[1].Source.Should().Be("EMu");
        objects.RecordInfo.RecordIdentifiers[1].Value.Should().Be("MS 2249");
        objects.EffectiveRecordInfo.Should().NotBeNull();
        objects.EffectiveRecordInfo!.RecordIdentifiers.Should().HaveCount(2);
        objects.EffectiveRecordInfo.RecordIdentifiers[0].Source.Should().Be("identity-service");
        objects.EffectiveRecordInfo.RecordIdentifiers[0].Value.Should().Be("b6n9e4c2");
        objects.EffectiveRecordInfo.RecordIdentifiers[1].Source.Should().Be("EMu");
        objects.EffectiveRecordInfo.RecordIdentifiers[1].Value.Should().Be("MS 2249");
        
        
        // physical files
        objects.Files[0].LocalPath.Should().Be("objects/amber-rudd.m4a");
        objects.Files[0].ContentType.Should().Be("audio/m4a");
        objects.Files[1].LocalPath.Should().Be("objects/amber-rudd.docx");
        objects.Files[1].ContentType.Should().Be("application/msword");
        objects.Files[1].Metadata.OfType<FileFormatMetadata>().Single().PronomKey.Should().Be("fmt/200");
        objects.Files[1].Metadata.OfType<FileFormatMetadata>().Single().FormatName.Should().Be("Microsoft Word");
        objects.Files[2].LocalPath.Should().Be("objects/angela-eagle.m4a");
        objects.Files[3].LocalPath.Should().Be("objects/angela-eagle-redacted.m4a");
        objects.Files[4].LocalPath.Should().Be("objects/angela-eagle-transcript.docx");

        // links between files
        var supplementing = new Uri("http://iiif.io/api/presentation/3#supplementing");
        objects.Files[0].Links.Should().HaveCount(1);
        objects.Files[0].Links[0].To.Should().Be("objects/amber-rudd.docx");
        objects.Files[0].Links[0].Role.Should().Be(supplementing);
        objects.Files[1].Links.Should().HaveCount(0);
        objects.Files[2].Links.Should().HaveCount(0);
        objects.Files[3].Links[0].To.Should().Be("objects/angela-eagle-transcript.docx");
        objects.Files[3].Links[0].Role.Should().Be(supplementing);
        objects.Files[4].Links.Should().HaveCount(0);

        // logical structmap
        result.Value.LogicalStructures.Should().HaveCount(1);
        var logsm = result.Value.LogicalStructures[0];
        logsm.Type.Should().Be("Collection");
        logsm.Name.Should().Be("Women of Westminster");
        // No direct file children
        logsm.Files.Should().HaveCount(0);
        
        // Two child ranges:
        logsm.Ranges.Should().HaveCount(2);
        
        // First range:
        logsm.Ranges[0].Type.Should().Be("Item");
        // mods:title from DMD, fall back to label "Amber Rudd" - need another test
        logsm.Ranges[0].Name.Should().Be("Interview with Amber Rudd"); 
        logsm.Ranges[0].RecordInfo.Should().NotBeNull();
        logsm.Ranges[0].RecordInfo!.RecordIdentifiers.Should().HaveCount(2);
        logsm.Ranges[0].RecordInfo!.RecordIdentifiers[0].Source.Should().Be("identity-service");
        logsm.Ranges[0].RecordInfo!.RecordIdentifiers[0].Value.Should().Be("mg56cva7");
        logsm.Ranges[0].RecordInfo!.RecordIdentifiers[1].Source.Should().Be("EMu");
        logsm.Ranges[0].RecordInfo!.RecordIdentifiers[1].Value.Should().Be("MS 2249/1");
        // Effective should match explicit
        logsm.Ranges[0].EffectiveRecordInfo.Should().NotBeNull();
        logsm.Ranges[0].EffectiveRecordInfo!.RecordIdentifiers.Should().HaveCount(2);
        logsm.Ranges[0].EffectiveRecordInfo!.RecordIdentifiers[0].Source.Should().Be("identity-service");
        logsm.Ranges[0].EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("mg56cva7");
        logsm.Ranges[0].EffectiveRecordInfo!.RecordIdentifiers[1].Source.Should().Be("EMu");
        logsm.Ranges[0].EffectiveRecordInfo!.RecordIdentifiers[1].Value.Should().Be("MS 2249/1");
        // Child file pointers
        logsm.Ranges[0].Files.Should().HaveCount(2);
        logsm.Ranges[0].Files[0].LocalPath.Should().Be("objects/amber-rudd.m4a");
        logsm.Ranges[0].Files[0].BeginTime.Should().BeNull();
        logsm.Ranges[0].Files[0].EndTime.Should().BeNull();
        logsm.Ranges[0].Files[0].Region.Should().BeNull();
        logsm.Ranges[0].Files[1].LocalPath.Should().Be("objects/amber-rudd.docx");
        // No child ranges
        logsm.Ranges[0].Ranges.Should().HaveCount(0);
        // No explicit access or rights, just recordinfo above
        logsm.Ranges[0].AccessRestrictions.Should().HaveCount(0);
        logsm.Ranges[0].RightsStatement.Should().BeNull();
        // TODO: Does a logical range inherit from the physical structMap?
        // ONLY if there is something declared on the physical structmap root (DMD_PHYS_ROOT), which is not the case here
        logsm.Ranges[0].EffectiveAccessRestrictions.Should().HaveCount(0);
        logsm.Ranges[0].EffectiveRightsStatement.Should().BeNull();
        
        // Second range:
        logsm.Ranges[1].Type.Should().Be("Item");
        logsm.Ranges[1].Name.Should().Be("Interview with Angela Eagle");
        logsm.Ranges[1].RecordInfo.Should().NotBeNull();
        logsm.Ranges[1].RecordInfo!.RecordIdentifiers.Should().HaveCount(2);
        logsm.Ranges[1].RecordInfo!.RecordIdentifiers[0].Source.Should().Be("identity-service");
        logsm.Ranges[1].RecordInfo!.RecordIdentifiers[0].Value.Should().Be("hh43pd32");
        logsm.Ranges[1].RecordInfo!.RecordIdentifiers[1].Source.Should().Be("EMu");
        logsm.Ranges[1].RecordInfo!.RecordIdentifiers[1].Value.Should().Be("MS 2249/2");
        logsm.Ranges[1].EffectiveRecordInfo!.RecordIdentifiers.Should().HaveCount(2);
        logsm.Ranges[1].EffectiveRecordInfo!.RecordIdentifiers[0].Source.Should().Be("identity-service");
        logsm.Ranges[1].EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("hh43pd32");
        logsm.Ranges[1].EffectiveRecordInfo!.RecordIdentifiers[1].Source.Should().Be("EMu");
        logsm.Ranges[1].EffectiveRecordInfo!.RecordIdentifiers[1].Value.Should().Be("MS 2249/2");
        // Child file pointers
        logsm.Ranges[1].Files.Should().HaveCount(2);
        logsm.Ranges[1].Files[0].LocalPath.Should().Be("objects/angela-eagle-redacted.m4a");
        logsm.Ranges[1].Files[1].LocalPath.Should().Be("objects/angela-eagle-transcript.docx");
        logsm.Ranges[1].Files[1].BeginTime.Should().BeNull();
        logsm.Ranges[1].Files[1].EndTime.Should().BeNull();
        logsm.Ranges[1].Files[1].Region.Should().BeNull();
        // No child ranges
        logsm.Ranges[1].Ranges.Should().HaveCount(0);
        // No explicit access or rights, just recordinfo above
        logsm.Ranges[1].AccessRestrictions.Should().HaveCount(0);
        logsm.Ranges[1].RightsStatement.Should().BeNull();
        // see comment above - nothing to inherit at this level
        logsm.Ranges[0].EffectiveAccessRestrictions.Should().HaveCount(0);
        logsm.Ranges[0].EffectiveRightsStatement.Should().BeNull();
        

        // gather the references to the files
        var ruddAudio = phys.FindFile("objects/amber-rudd.m4a")!;
        var ruddTranscript = phys.FindFile("objects/amber-rudd.docx")!;
        var eagleRedactedAudio = phys.FindFile("objects/angela-eagle-redacted.m4a")!;
        var eagleTranscript = phys.FindFile("objects/angela-eagle-transcript.docx")!;
        var eagleAudio = phys.FindFile("objects/angela-eagle")!;
        
        // 4 of the files have no explicit descriptive metadata - they inherit it
        ruddAudio.RecordInfo.Should().BeNull();
        ruddAudio.AccessRestrictions.Should().HaveCount(0);
        ruddAudio.RightsStatement.Should().BeNull();
        
        ruddTranscript.RecordInfo.Should().BeNull();
        ruddTranscript.AccessRestrictions.Should().HaveCount(0);
        ruddTranscript.RightsStatement.Should().BeNull();
        
        eagleRedactedAudio.RecordInfo.Should().BeNull();
        eagleRedactedAudio.AccessRestrictions.Should().HaveCount(0);
        eagleRedactedAudio.RightsStatement.Should().BeNull();
        
        eagleTranscript.RecordInfo.Should().BeNull();
        eagleTranscript.AccessRestrictions.Should().HaveCount(0);
        eagleTranscript.RightsStatement.Should().BeNull();
        
        eagleAudio.RecordInfo.Should().BeNull();
        // but Eagle Audio does have an explicit access and rights assignment:
        eagleAudio.AccessRestrictions.Should().HaveCount(1);
        eagleAudio.AccessRestrictions[0].Should().Be("Closed");
        // TODO: how to override inherited? Effective will be null, correctly, but we can't tell directly that there is an explicit null assignment here
        eagleAudio.RightsStatement.Should().BeNull();

        // verify their effective (inherited or direct) properties:
        // inherited from div in logical structMap
        ruddAudio.EffectiveRecordInfo.Should().NotBeNull();
        ruddAudio.EffectiveRecordInfo!.RecordIdentifiers.Should().HaveCount(2);
        ruddAudio.EffectiveRecordInfo!.RecordIdentifiers[0].Source.Should().Be("identity-service");
        ruddAudio.EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("mg56cva7");
        ruddAudio.EffectiveRecordInfo!.RecordIdentifiers[1].Source.Should().Be("EMu");
        ruddAudio.EffectiveRecordInfo!.RecordIdentifiers[1].Value.Should().Be("MS 2249/1");
        // inherited from physical structMap objects/ div
        ruddAudio.EffectiveAccessRestrictions.Should().HaveCount(1);
        ruddAudio.EffectiveAccessRestrictions[0].Should().Be("Level1");
        ruddAudio.EffectiveRightsStatement.Should().Be(inCopyright);
        
        // rudd transcript should have same effective properties as ruddAudio
        // inherited from div in logical structMap
        ruddTranscript.EffectiveRecordInfo.Should().NotBeNull();
        ruddTranscript.EffectiveRecordInfo!.RecordIdentifiers.Should().HaveCount(2);
        ruddTranscript.EffectiveRecordInfo!.RecordIdentifiers[0].Source.Should().Be("identity-service");
        ruddTranscript.EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("mg56cva7");
        ruddTranscript.EffectiveRecordInfo!.RecordIdentifiers[1].Source.Should().Be("EMu");
        ruddTranscript.EffectiveRecordInfo!.RecordIdentifiers[1].Value.Should().Be("MS 2249/1");
        // inherited from physical structMap objects/ div
        ruddTranscript.EffectiveAccessRestrictions.Should().HaveCount(1);
        ruddTranscript.EffectiveAccessRestrictions[0].Should().Be("Level1");
        ruddTranscript.EffectiveRightsStatement.Should().Be(inCopyright);
        
        // inherited from div in logical structMap
        eagleRedactedAudio.EffectiveRecordInfo.Should().NotBeNull();
        eagleRedactedAudio.EffectiveRecordInfo!.RecordIdentifiers.Should().HaveCount(2);
        eagleRedactedAudio.EffectiveRecordInfo!.RecordIdentifiers[0].Source.Should().Be("identity-service");
        eagleRedactedAudio.EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("hh43pd32");
        eagleRedactedAudio.EffectiveRecordInfo!.RecordIdentifiers[1].Source.Should().Be("EMu");
        eagleRedactedAudio.EffectiveRecordInfo!.RecordIdentifiers[1].Value.Should().Be("MS 2249/2");
        // inherited from physical structMap objects/ div
        eagleRedactedAudio.EffectiveAccessRestrictions.Should().HaveCount(1);
        eagleRedactedAudio.EffectiveAccessRestrictions[0].Should().Be("Level1");
        eagleRedactedAudio.EffectiveRightsStatement.Should().Be(inCopyright);
        
        // Eagle transcript should have same effective properties as eagleRedactedAudio
        // inherited from div in logical structMap
        eagleTranscript.EffectiveRecordInfo.Should().NotBeNull();
        eagleTranscript.EffectiveRecordInfo!.RecordIdentifiers.Should().HaveCount(2);
        eagleTranscript.EffectiveRecordInfo!.RecordIdentifiers[0].Source.Should().Be("identity-service");
        eagleTranscript.EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("hh43pd32");
        eagleTranscript.EffectiveRecordInfo!.RecordIdentifiers[1].Source.Should().Be("EMu");
        eagleTranscript.EffectiveRecordInfo!.RecordIdentifiers[1].Value.Should().Be("MS 2249/2");
        // inherited from physical structMap objects/ div
        eagleTranscript.EffectiveAccessRestrictions.Should().HaveCount(1);
        eagleTranscript.EffectiveAccessRestrictions[0].Should().Be("Level1");
        eagleTranscript.EffectiveRightsStatement.Should().Be(inCopyright);

        // The original, unredacted Eagle audio is (for the purposes of this experiment) not part of MS 2249/2
        // inherited from physical structMap objects/ div
        eagleAudio.EffectiveRecordInfo.Should().NotBeNull();
        eagleAudio.EffectiveRecordInfo!.RecordIdentifiers.Should().HaveCount(2);
        eagleAudio.EffectiveRecordInfo.RecordIdentifiers[0].Source.Should().Be("identity-service");
        eagleAudio.EffectiveRecordInfo.RecordIdentifiers[0].Value.Should().Be("b6n9e4c2");
        eagleAudio.EffectiveRecordInfo.RecordIdentifiers[1].Source.Should().Be("EMu");
        eagleAudio.EffectiveRecordInfo.RecordIdentifiers[1].Value.Should().Be("MS 2249");
        // directly asserted (therefore also effective)
        eagleAudio.EffectiveAccessRestrictions.Should().HaveCount(1);
        eagleAudio.EffectiveAccessRestrictions[0].Should().Be("Closed");
        eagleAudio.EffectiveRightsStatement.Should().Be(null);

        // divIds, other props we might be interested in
        // This needs more work to be consistent.
        eagleAudio.MetsExtensions!.DivId.Should().Be("PHYS_objects/angela-eagle.m4a");
        eagleAudio.MetsExtensions!.AdmId.Should().Be("ADM_objects/angela-eagle.m4a");

        // locate files by ID and localpath
        // This is the ID of the file's mets:div in the physical structMap. Do we want to be able to get by mets:file ID property? 
        var ruddAudioById = phys.Files.SingleOrDefault(f => f.MetsExtensions!.DivId == "PHYS_objects/amber-rudd.m4a");
        ruddAudioById.Should().NotBeNull();
        


    }
}