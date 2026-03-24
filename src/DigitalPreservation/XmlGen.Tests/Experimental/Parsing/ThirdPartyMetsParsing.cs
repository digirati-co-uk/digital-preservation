using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XmlGen.Tests.Experimental.Parsing;

public class ThirdPartyMetsParsing
{
    private readonly MetsParser parser;
    
    public ThirdPartyMetsParsing()
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
    public async Task Can_Parse_Goobi_Mixed()
    {
        // goobi-mixed.xml: 2-page Archive item.
        // PHYS_0001 → LOG_0000 only ("Open with advisory")
        // PHYS_0002 → LOG_0000 AND LOG_0001 ("Restricted files"); deepest-only → goes to LOG_0001
        var goobiMets = new FileInfo("Samples/goobi-mixed.xml");
        var result = await parser.GetMetsFileWrapper(new Uri(goobiMets.FullName));

        result.Success.Should().BeTrue();
        var mets = result.Value!;

        // Physical structure
        var phys = mets.PhysicalStructure!;
        phys.Directories.Should().HaveCount(1);
        var objects = phys.Directories.Single(d => d.Name == "objects");
        objects.Files.Should().HaveCount(2);

        // No ALTO fileGrp → no transcript file links
        objects.Files[0].LocalPath.Should().Be("objects/b33061592_0001.jp2");
        objects.Files[1].LocalPath.Should().Be("objects/b33061592_0002.jp2");
        objects.Files[0].Links.Should().BeEmpty();
        objects.Files[1].Links.Should().BeEmpty();

        // Logical structure
        mets.LogicalStructures.Should().HaveCount(1);
        var logical = mets.LogicalStructures.Single();
        logical.Type.Should().Be("Archive");
        logical.Name.Should().Be("Woman on sofa obscured by c-shape scotoma with black");
        logical.AccessRestrictions.Should().HaveCount(1);
        logical.AccessRestrictions![0].Should().Be("Open with advisory");
        logical.EffectiveAccessRestrictions[0].Should().Be("Open with advisory");
        logical.RecordInfo!.RecordIdentifiers[0].Source.Should().Be("gbv-ppn");
        logical.RecordInfo.RecordIdentifiers[0].Value.Should().Be("b33061592");

        logical.Ranges.Should().HaveCount(1);
        var back = logical.Ranges[0];
        back.Type.Should().Be("Back");
        back.AccessRestrictions.Should().HaveCount(1);
        back.AccessRestrictions![0].Should().Be("Restricted files");
        back.EffectiveAccessRestrictions[0].Should().Be("Restricted files");
        // No own RecordInfo, inherits from parent
        back.RecordInfo.Should().BeNull();
        back.EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("b33061592");

        // Deepest-only file assignment
        logical.Files.Should().HaveCount(1);
        logical.Files[0].LocalPath.Should().Be("objects/b33061592_0001.jp2");
        back.Files.Should().HaveCount(1);
        back.Files[0].LocalPath.Should().Be("objects/b33061592_0002.jp2");

        // Effective access on physical files
        // File 0001 assigned to LOG_0000 → "Open with advisory"
        objects.Files[0].EffectiveAccessRestrictions.Should().HaveCount(1);
        objects.Files[0].EffectiveAccessRestrictions[0].Should().Be("Open with advisory");
        objects.Files[0].EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("b33061592");
        // File 0002 assigned to LOG_0001 → "Restricted files", RecordInfo inherited from LOG_0000
        objects.Files[1].EffectiveAccessRestrictions.Should().HaveCount(1);
        objects.Files[1].EffectiveAccessRestrictions[0].Should().Be("Restricted files");
        objects.Files[1].EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("b33061592");
    }

    [Fact]
    public async Task Can_Parse_Goobi_Mixed_2()
    {
        // goobi-mixed-2.xml: 266-page Physician Superintendent's Report Book.
        // LOG_0000 (Archive, "Requires registration") claims all 266 pages.
        // 57 child ranges (LOG_0001–LOG_0046, LOG_0048–LOG_0058) each have "Restricted files"
        //   and claim 93 pages total between them.
        // LOG_0047 has no DMDID (no own access condition) and claims PHYS_0220 and PHYS_0221;
        //   it inherits "Requires registration" from LOG_0000.
        // Deepest-only: 93 pages go to "Restricted files" children, 2 go to LOG_0047, 171 stay in LOG_0000.
        // Net file access counts: 173 "Requires registration", 93 "Restricted files".
        var goobiMets = new FileInfo("Samples/goobi-mixed-2.xml");
        var result = await parser.GetMetsFileWrapper(new Uri(goobiMets.FullName));

        result.Success.Should().BeTrue();
        var mets = result.Value!;

        // Physical structure: 266 files, no ALTO
        var phys = mets.PhysicalStructure!;
        phys.Directories.Should().HaveCount(1);
        var objects = phys.Directories.Single(d => d.Name == "objects");
        objects.Files.Should().HaveCount(266);
        objects.Files.Should().OnlyContain(f => f.Links.Count == 0); // no transcript links

        // Logical structure: 1 root, 58 child ranges
        mets.LogicalStructures.Should().HaveCount(1);
        var logical = mets.LogicalStructures.Single();
        logical.Type.Should().Be("Archive");
        logical.AccessRestrictions.Should().HaveCount(1);
        logical.AccessRestrictions![0].Should().Be("Requires registration");
        logical.EffectiveAccessRestrictions[0].Should().Be("Requires registration");
        logical.RecordInfo!.RecordIdentifiers[0].Source.Should().Be("gbv-ppn");
        logical.RecordInfo.RecordIdentifiers[0].Value.Should().Be("b22659067");
        logical.Ranges.Should().HaveCount(58);

        // LOG_0047 (index 46) has no DMDID — no own access, inherits from root
        var log0047 = logical.Ranges[46];
        log0047.AccessRestrictions.Should().BeNull();
        log0047.EffectiveAccessRestrictions[0].Should().Be("Requires registration");
        log0047.Files.Should().HaveCount(2);
        log0047.Files[0].LocalPath.Should().Be("objects/DGH1_2_3_2_5_0220.JP2");
        log0047.Files[1].LocalPath.Should().Be("objects/DGH1_2_3_2_5_0221.JP2");

        // All other 57 child ranges have "Restricted files"
        var restrictedRanges = logical.Ranges.Where(r => r != log0047).ToList();
        restrictedRanges.Should().HaveCount(57);
        restrictedRanges.Should().OnlyContain(r =>
            r.AccessRestrictions != null &&
            r.EffectiveAccessRestrictions.Count == 1 &&
            r.EffectiveAccessRestrictions[0] == "Restricted files");

        // Deepest-only: LOG_0000 gets 171 directly (266 - 95 claimed by any child)
        logical.Files.Should().HaveCount(171);

        // Spot checks: file 0001 stays in LOG_0000 → "Requires registration"
        objects.Files[0].LocalPath.Should().Be("objects/DGH1_2_3_2_5_0001.JP2");
        objects.Files[0].EffectiveAccessRestrictions.Should().HaveCount(1);
        objects.Files[0].EffectiveAccessRestrictions[0].Should().Be("Requires registration");

        // File 0007 is claimed by a child range → "Restricted files"
        objects.Files[6].LocalPath.Should().Be("objects/DGH1_2_3_2_5_0007.JP2");
        objects.Files[6].EffectiveAccessRestrictions.Should().HaveCount(1);
        objects.Files[6].EffectiveAccessRestrictions[0].Should().Be("Restricted files");

        // Files 0220 and 0221 are in LOG_0047 → "Requires registration" (inherited, not own)
        objects.Files[219].LocalPath.Should().Be("objects/DGH1_2_3_2_5_0220.JP2");
        objects.Files[219].EffectiveAccessRestrictions[0].Should().Be("Requires registration");
        objects.Files[220].LocalPath.Should().Be("objects/DGH1_2_3_2_5_0221.JP2");
        objects.Files[220].EffectiveAccessRestrictions[0].Should().Be("Requires registration");

        // Aggregate counts
        var requiresReg = objects.Files
            .Count(f => f.EffectiveAccessRestrictions.Count == 1 &&
                        f.EffectiveAccessRestrictions[0] == "Requires registration");
        var restricted = objects.Files
            .Count(f => f.EffectiveAccessRestrictions.Count == 1 &&
                        f.EffectiveAccessRestrictions[0] == "Restricted files");
        requiresReg.Should().Be(173);
        restricted.Should().Be(93);

        // All files inherit the root RecordInfo since no child range has its own
        objects.Files.Should().OnlyContain(f =>
            f.EffectiveRecordInfo != null &&
            f.EffectiveRecordInfo.RecordIdentifiers[0].Source == "gbv-ppn" &&
            f.EffectiveRecordInfo.RecordIdentifiers[0].Value == "b22659067");
    }

    [Fact]
    public async Task Can_Parse_Goobi_MoH()
    {
        var goobiMets = new FileInfo("Samples/goobi-wc-b29356350.xml");
        var result = await parser.GetMetsFileWrapper(new Uri(goobiMets.FullName));

        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Self.Should().NotBeNull();
        result.Value.Self!.Digest.Should().NotBeEmpty();
        var phys = result.Value!.PhysicalStructure;
        phys!.Files.Should().Contain(f => f.Name == "goobi-wc-b29356350.xml");
        
        result.Value.Name.Should().Be("[Report 1960] /");
        phys.Directories.Should().HaveCount(2);
        var objects = phys.Directories.Single(d => d.Name == "objects");
        var alto = phys.Directories.Single(d => d.Name == "alto");
        objects.Name.Should().Be(FolderNames.Objects);
        objects.Files.Should().HaveCount(32);
        alto.Files.Should().HaveCount(32);
        
        
        // Access restrictions and rights set on the LOGICAL ROOT
        phys.EffectiveAccessRestrictions.Should().BeEmpty();
        phys.RightsStatement.Should().BeNull();
        phys.EffectiveRightsStatement.Should().BeNull();
        phys.RecordInfo.Should().BeNull();
        phys.EffectiveRecordInfo.Should().BeNull();
        objects.AccessRestrictions.Should().BeNull();
        objects.EffectiveAccessRestrictions.Should().BeEmpty();
        objects.RightsStatement.Should().BeNull();
        objects.EffectiveRightsStatement.Should().BeNull();
        objects.RecordInfo.Should().BeNull();
        objects.EffectiveRecordInfo.Should().BeNull();
        alto.AccessRestrictions.Should().BeNull();
        alto.EffectiveAccessRestrictions.Should().BeEmpty();
        alto.RightsStatement.Should().BeNull();
        alto.EffectiveRightsStatement.Should().BeNull();
        alto.RecordInfo.Should().BeNull();
        alto.EffectiveRecordInfo.Should().BeNull();
        

        result.Value.LogicalStructures.Should().HaveCount(1);
        var logical =  result.Value.LogicalStructures.Single();
        logical.Type.Should().Be("Monograph");
        logical.Name.Should().Be("[Report 1960] /");
        logical.AccessRestrictions.Should().HaveCount(1);
        logical.AccessRestrictions![0].Should().Be("Open");
        logical.EffectiveAccessRestrictions[0].Should().Be("Open");
        logical.RightsStatement.Should().BeNull();
        logical.EffectiveRightsStatement.Should().BeNull();
        logical.RecordInfo.Should().NotBeNull();
        logical.RecordInfo!.RecordIdentifiers.Should().HaveCount(1);
        logical.RecordInfo.RecordIdentifiers[0].Source.Should().Be("gbv-ppn");
        logical.RecordInfo.RecordIdentifiers[0].Value.Should().Be("b29356350");
        logical.EffectiveRecordInfo.Should().NotBeNull();
        logical.EffectiveRecordInfo!.RecordIdentifiers.Should().HaveCount(1);
        logical.EffectiveRecordInfo.RecordIdentifiers[0].Source.Should().Be("gbv-ppn");
        logical.EffectiveRecordInfo.RecordIdentifiers[0].Value.Should().Be("b29356350");

        logical.Ranges.Should().HaveCount(3);
        logical.Ranges[0].Type.Should().Be("Cover");
        logical.Ranges[1].Type.Should().Be("TitlePage");
        logical.Ranges[2].Type.Should().Be("Cover");
        logical.Ranges[0].Ranges.Should().BeEmpty();
        logical.Ranges[1].Ranges.Should().BeEmpty();
        logical.Ranges[2].Ranges.Should().BeEmpty();

        for (var i = 0; i < 3; i++)
        {
            logical.Ranges[i].AccessRestrictions.Should().BeNull();
            logical.Ranges[i].EffectiveAccessRestrictions.Should().HaveCount(1);
            logical.Ranges[i].EffectiveAccessRestrictions[0].Should().Be("Open");
            logical.Ranges[i].RightsStatement.Should().BeNull();
            logical.Ranges[i].RightsStatement.Should().BeNull();
            logical.Ranges[i].RecordInfo.Should().BeNull();
            logical.Ranges[i].EffectiveRecordInfo.Should().NotBeNull();
            logical.Ranges[i].EffectiveRecordInfo!.RecordIdentifiers.Should().HaveCount(1);
            logical.Ranges[i].EffectiveRecordInfo!.RecordIdentifiers[0].Source.Should().Be("gbv-ppn");
            logical.Ranges[i].EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("b29356350");
        }
        
        
        // after this point we need new functionality in METS parser

        // In a Goobi METS file, the association of files with logical structure is done by the structLink section,
        // rather than the logical divs having mets:fptr elements pointing directly at files. Instead,
        // mets:smLink elements link the logical section ID to the mets:div ID of the physical page. We detect
        // links from logical IDs to physical div IDs in smLinks, resolve each physical div's primary (non-ALTO)
        // files, and populate LogicalRange.Files accordingly.
        //
        // "Deepest only" rule: when multiple logical ranges claim the same physical div (e.g. LOG_0000 and its
        // child LOG_0001 both link to PHYS_0001), files are assigned only to the deepest (most specific) range.
        // This produces a clean non-overlapping hierarchy suited to IIIF manifest building.
        //
        // LOG_0000 → PHYS_0001..0032 (all 32 pages), but PHYS_0001, PHYS_0003 and PHYS_0032 are
        // claimed more specifically by LOG_0001, LOG_0002 and LOG_0003 respectively, leaving LOG_0000
        // with 29 files directly.
        logical.Files.Should().HaveCount(29);
        logical.Files[0].LocalPath.Should().Be("objects/b29356350_0002.jp2");
        logical.Files[28].LocalPath.Should().Be("objects/b29356350_0031.jp2");
        logical.Ranges[0].Files.Should().HaveCount(1); // Cover → PHYS_0001
        logical.Ranges[0].Files[0].LocalPath.Should().Be("objects/b29356350_0001.jp2");
        logical.Ranges[1].Files.Should().HaveCount(1); // TitlePage → PHYS_0003
        logical.Ranges[1].Files[0].LocalPath.Should().Be("objects/b29356350_0003.jp2");
        logical.Ranges[2].Files.Should().HaveCount(1); // Back Cover → PHYS_0032
        logical.Ranges[2].Files[0].LocalPath.Should().Be("objects/b29356350_0032.jp2");
        
        // The phys root, objects and alto WorkingDirectory have neither AccessRestrictions nor
        // EffectiveAccessRestrictions, because they are not included in any logical section that 
        // declares these. Only files are included, via the indirect smlink method described in the 
        // comments above. But once we get into files, we should start seeing properties inherited via
        // inclusion in logical sections:

        string[] folders = ["objects", "alto"];
        for (var i = 0; i < 32; i++)
        {
            foreach (var folderSlug in folders)
            {
                var folder = phys.Directories.Single(d => d.Name == folderSlug);
                folder.Files[i].AccessRestrictions.Should().BeNull();
                folder.Files[i].EffectiveAccessRestrictions.Should().HaveCount(1);
                folder.Files[i].EffectiveAccessRestrictions[0].Should().Be("Open");
                folder.Files[i].RightsStatement.Should().BeNull();
                folder.Files[i].EffectiveRightsStatement.Should().BeNull();
                folder.Files[i].RecordInfo.Should().BeNull();
                folder.Files[i].EffectiveRecordInfo.Should().Be(logical.RecordInfo);
            }
        }
        
        // In Goobi METS, the relationships between files that we model with the FileLink class are
        // determined by the "USE" attribute of their containing mets:fileGrp. This attribute should be
        // read as what the files are used for, rather than as an imperative "USE!"
        // If one fileGrp has USE="OBJECTS" and another has USE="ALTO", and in the physical structMap
        // a single mets:div has two mets:fptr elements, each pointing to a file in each of the two groups,
        // then we can infer that there is a relationship between the two files. We'll need rules based on
        // the value of the USE attribute, but initially our one rule is that if a mets:div has pointers to
        // two files, and one is in USE=OBJECTS and one is in USE=ALTO, then the one in ALTO is a transcript
        // of the one in objects (because ALTO is an OCR file format). Therefore:
        var transcript = FileLinkRoles.FromIiifProvides("transcript");

        for (var i = 0; i < 32; i++)
        {
            // Files are numbered 0001–0032; i is 0-based so use i+1
            var numSuffix = (i + 1).ToString().PadLeft(4, '0');
            objects.Files[i].LocalPath.Should().Be($"objects/b29356350_{numSuffix}.jp2");
            alto.Files[i].LocalPath.Should().Be($"alto/b29356350_{numSuffix}.xml");
            objects.Files[i].Links.Should().HaveCount(1);
            objects.Files[i].Links[0].To.Should().Be(alto.Files[i].LocalPath);
            objects.Files[i].Links[0].Role.Should().Be(transcript);
            alto.Files[i].Links.Should().BeEmpty();
        }
    }
}