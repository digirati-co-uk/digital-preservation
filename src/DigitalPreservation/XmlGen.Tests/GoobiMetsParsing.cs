using System.Xml;
using System.Xml.Serialization;
using DigitalPreservation.XmlGen.Extensions;
using DigitalPreservation.XmlGen.Mets;
using DigitalPreservation.XmlGen.Mods.V3;
using DigitalPreservation.XmlGen.Premis.V3;
using FluentAssertions;
using File = DigitalPreservation.XmlGen.Premis.V3.File;

namespace XmlGen.Tests;

public class GoobiMetsParsing
{
    public required Mets GoobiMets { get; set; }

    public GoobiMetsParsing()
    {
        var goobiMetsFile = "Samples/goobi-wc-b29356350.xml";
        var serializer = new XmlSerializer(typeof(Mets));
        using XmlReader reader = XmlReader.Create(goobiMetsFile);
        GoobiMets = (Mets) serializer.Deserialize(reader)!;
    }
    
    [Fact]
    public void Can_Load_Goobi_METS()
    {
        // arrange
        // covered in constructor
        
        // assert
        GoobiMets.MetsHdr.Agent[0].Note[0].Value.Should().Be("Goobi");
    }

    [Fact]
    public void Can_Understand_Logical_StructMap_From_Goobi_METS()
    {
        GoobiMets.StructMap.Should().HaveCount(2);
        var logicalSM = GoobiMets.StructMap.Single(sm => sm.Type == "LOGICAL"); // NB this type is not a controlled vocab
        logicalSM.Should().NotBeNull();
        logicalSM.Div.Type.Should().Be("Monograph");
        logicalSM.Div.Label.Should().Be("[Report 1960] /");
        logicalSM.Div.Id.Should().Be("LOG_0000");
        logicalSM.Div.Admid.Should().HaveCount(1).And.Contain("AMD");
        logicalSM.Div.Dmdid.Should().HaveCount(1).And.Contain("DMDLOG_0000");
        logicalSM.Div.Div.Should().HaveCount(3);
        logicalSM.Div.Div[0].Id.Should().Be("LOG_0001");
        logicalSM.Div.Div[0].Type.Should().Be("Cover");
        logicalSM.Div.Div[1].Id.Should().Be("LOG_0002");
        logicalSM.Div.Div[1].Type.Should().Be("TitlePage");
        logicalSM.Div.Div[2].Id.Should().Be("LOG_0003");
        logicalSM.Div.Div[2].Type.Should().Be("Cover");
    }
    
    
    [Fact]
    public void Can_Understand_Physcial_StructMap_From_Goobi_METS()
    {
        var physicalSM = GoobiMets.StructMap.Single(sm => sm.Type == "PHYSICAL"); // NB this type is not a controlled vocab
        physicalSM.Should().NotBeNull();
        physicalSM.Div.Type.Should().Be("physSequence");
        physicalSM.Div.Id.Should().Be("PHYS_0000");
        physicalSM.Div.Dmdid.Should().HaveCount(1).And.Contain("DMDPHYS_0000");
        physicalSM.Div.Div.Should().HaveCount(32);
        // sample a few
        physicalSM.Div.Div[0].Id.Should().Be("PHYS_0001");
        physicalSM.Div.Div[0].Admid.Should().HaveCount(1).And.Contain("AMD_0001");
        physicalSM.Div.Div[0].Order.Should().Be("1");
        physicalSM.Div.Div[0].Orderlabel.Should().Be(" - ");
        physicalSM.Div.Div[0].Type.Should().Be("page");
        physicalSM.Div.Div[0].Fptr.Should().HaveCount(2);
        physicalSM.Div.Div[0].Fptr[0].Fileid.Should().Be("FILE_0001_OBJECTS");
        physicalSM.Div.Div[0].Fptr[1].Fileid.Should().Be("FILE_0001_ALTO");
        
        physicalSM.Div.Div[20].Id.Should().Be("PHYS_0021");
        physicalSM.Div.Div[20].Admid.Should().HaveCount(1).And.Contain("AMD_0021");
        physicalSM.Div.Div[20].Order.Should().Be("21");
        physicalSM.Div.Div[20].Orderlabel.Should().Be("19");
        physicalSM.Div.Div[20].Type.Should().Be("page");
        physicalSM.Div.Div[20].Fptr.Should().HaveCount(2);
        physicalSM.Div.Div[20].Fptr[0].Fileid.Should().Be("FILE_0021_OBJECTS");
        physicalSM.Div.Div[20].Fptr[1].Fileid.Should().Be("FILE_0021_ALTO");
    }
    
    [Fact]
    public void Can_Read_MODS_DmdSec_From_Goobi_METS()
    {
        // This example has dmdSecs for the logical and physical structmaps
        GoobiMets.DmdSec.Should().HaveCount(2);
        var logicalDmdSecMdWrap = GoobiMets.DmdSec[0].MdWrap;
        logicalDmdSecMdWrap.Mdtype.Should().Be(MdSecTypeMdWrapMdtype.Mods);
        logicalDmdSecMdWrap.XmlData.Should().NotBeNull();

        XmlElement logicalModsXml = logicalDmdSecMdWrap.XmlData.Any[0];
        logicalModsXml.Name.Should().Be("mods:mods");
        
        // Now turn it into typed MODS
        var mods = logicalModsXml.ToMods()!;
        // Note that this is Type1 not Type; Type is reserved?
        mods.Name[0].Type1.Should().Be(NameDefinitionType.Corporate);
        
        // This is not what is in the file - we don't seem to be able to get at the nested construction there. 
        // Is it even valid?
        mods.Name[0].NamePart[0].Value.Trim().Should().Be("Chester-le-Street (England)");

        mods.OriginInfo[0].Place[0].PlaceTerm[0].Type.Should().Be(CodeOrText.Code);
        mods.OriginInfo[0].Place[0].PlaceTerm[0].Value.Should().Be("enk");
        mods.OriginInfo[0].DateIssued[0].Encoding.Should().Be(DateDefinitionEncoding.W3Cdtf);
        mods.OriginInfo[0].DateIssued[0].KeyDate.Should().Be(Yes.Yes);
        mods.OriginInfo[0].DateIssued[0].Value.Should().Be("1960");

        mods.TitleInfo[0].Title[0].Value.Should().Be("[Report 1960] /");

        mods.Note[0].Type.Should().Be("leader6");
        mods.Note[0].Value.Should().Be("a");
        
        mods.AccessCondition.Count.Should().Be(3);
        mods.AccessCondition[0].Type.Should().Be("dz");
        mods.AccessCondition[0].Text[0].Should().Be("CC-BY");
        mods.AccessCondition[2].Type.Should().Be("status");
        mods.AccessCondition[2].Text[0].Should().Be("Open");
        
        // A second originInfo appears later on
        mods.OriginInfo[1].Place[0].PlaceTerm[0].Type.Should().Be(CodeOrText.Text);
        mods.OriginInfo[1].Place[0].PlaceTerm[0].Value.Should().Be("Wellcome Trust");
        mods.OriginInfo[1].Publisher[0].Value.Should().Be("Wellcome Trust");
        mods.OriginInfo[1].Edition[0].Value.Should().Be("[Electronic ed.]");
        mods.OriginInfo[1].DateCaptured[0].Value.Should().Be("2012");
        mods.OriginInfo[1].DateCaptured[0].Encoding.Should().Be(DateDefinitionEncoding.W3Cdtf);

        mods.PhysicalDescription[0].DigitalOrigin[0].Should().Be(DigitalOriginDefinition.ReformattedDigital);
    }

    [Fact]
    public void Can_Read_MODS_StructLink_From_Goobi_METS()
    {
        GoobiMets.StructLink.SmLink.Should().HaveCount(35);
        GoobiMets.StructLink.SmLink[0].From.Should().Be("LOG_0000");
        GoobiMets.StructLink.SmLink[0].To.Should().Be("PHYS_0001");
        GoobiMets.StructLink.SmLink[34].From.Should().Be("LOG_0003");
        GoobiMets.StructLink.SmLink[34].To.Should().Be("PHYS_0032");
    }
    
    
    [Fact]
    public void Can_Read_MODS_FileSec_From_Goobi_METS()
    {
        GoobiMets.FileSec.FileGrp.Should().HaveCount(2);
        
        var objects = GoobiMets.FileSec.FileGrp.Single(fg => fg.Use == "OBJECTS");
        
        objects.File.Should().HaveCount(32);
        
        objects.File[0].Id.Should().Be("FILE_0001_OBJECTS");
        objects.File[0].Mimetype.Should().Be("image/jp2");
        objects.File[0].FLocat.Should().HaveCount(1);
        objects.File[0].FLocat[0].Loctype.Should().Be(FileTypeFLocatLoctype.Url);
        objects.File[0].FLocat[0].Href.Should().Be("objects/b29356350_0001.jp2");
        
        objects.File[31].Id.Should().Be("FILE_0032_OBJECTS");
        objects.File[31].Mimetype.Should().Be("image/jp2");
        objects.File[31].FLocat.Should().HaveCount(1);
        objects.File[31].FLocat[0].Loctype.Should().Be(FileTypeFLocatLoctype.Url);
        objects.File[31].FLocat[0].Href.Should().Be("objects/b29356350_0032.jp2");
        
        
        var alto = GoobiMets.FileSec.FileGrp.Single(fg => fg.Use == "ALTO");
        
        alto.File.Should().HaveCount(32);
        
        alto.File[0].Id.Should().Be("FILE_0001_ALTO");
        alto.File[0].Mimetype.Should().Be("application/xml");
        alto.File[0].FLocat.Should().HaveCount(1);
        alto.File[0].FLocat[0].Loctype.Should().Be(FileTypeFLocatLoctype.Url);
        alto.File[0].FLocat[0].Href.Should().Be("alto/b29356350_0001.xml");
        
        alto.File[31].Id.Should().Be("FILE_0032_ALTO");
        alto.File[31].Mimetype.Should().Be("application/xml");
        alto.File[31].FLocat.Should().HaveCount(1);
        alto.File[31].FLocat[0].Loctype.Should().Be(FileTypeFLocatLoctype.Url);
        alto.File[31].FLocat[0].Href.Should().Be("alto/b29356350_0032.xml");
    }


    [Fact]
    public void Can_Read_Premis_From_Goobi_METS()
    {
        GoobiMets.AmdSec.Should().HaveCount(1);
        var amdSecTechMds = GoobiMets.AmdSec[0].TechMd;
        amdSecTechMds.Should().HaveCount(32);

        var firstAmdWrap = amdSecTechMds[0].MdWrap;
        firstAmdWrap.Mdtype.Should().Be(MdSecTypeMdWrapMdtype.Other);
        firstAmdWrap.XmlData.Should().NotBeNull();

        XmlElement premisXml = firstAmdWrap.XmlData.Any[0];
        
        // In Goobi's case, yes
        premisXml.Name.Should().Be("premis:object");
        
        // Now turn it into typed PremisComplexObject
        var premis = premisXml.GetPremisComplexObject()!;
        premis.Should().NotBeNull();

        var premisFile = premis.Object[0] as File;
        premisFile.Should().NotBeNull();
        
        // partial exploration
        premisFile!.ObjectIdentifier[0].ObjectIdentifierType.Value.Should().Be("local");
        premisFile.ObjectIdentifier[0].ObjectIdentifierValue.Should().Be("b29356350_0001.jp2");

        premisFile.SignificantProperties.Should().HaveCount(2);
        var height = premisFile.SignificantProperties.Single(sp => sp.SignificantPropertiesType.Value == "ImageHeight");
        height.SignificantPropertiesValue.Should().HaveCount(1).And.Contain("4325");
        var width = premisFile.SignificantProperties.Single(sp => sp.SignificantPropertiesType.Value == "ImageWidth");
        width.SignificantPropertiesValue.Should().HaveCount(1).And.Contain("2513");

        premisFile.ObjectCharacteristics.Should().HaveCount(1);
        premisFile.ObjectCharacteristics[0].Fixity[0].MessageDigestAlgorithm.Value.Should().Be("sha256");
        premisFile.ObjectCharacteristics[0].Fixity[0].MessageDigest.Should().Be("6eb6c17cd93e392fed8e1cb4d9de5617b8a9b4de");
        premisFile.ObjectCharacteristics[0].Size.Should().Be(1348420);

        var format = premisFile.ObjectCharacteristics[0].Format[0];
        format.FormatDesignation.FormatName.Value.Should().Be("JP2 (JPEG 2000 part 1)");
        format.FormatRegistry[0].FormatRegistryName.Value.Should().Be("PRONOM");
        format.FormatRegistry[0].FormatRegistryKey.Value.Should().Be("x-fmt/392");
    }

}