using FluentAssertions;

namespace XmlGen.Tests.Parsing;

/// <summary>
/// The parser's half of issue #433: every <c>mods:accessCondition</c> of a type other than the two
/// chosen ones (and Goobi's status) is reported under <c>DerivedAccessConditions</c>, keyed by type,
/// and inherits per type down the physical tree, falling back to the logical range the way access does.
/// </summary>
public class DerivedAccessConditionsParsingTests : MetsParserTestBase
{
    private static string Mets(string objectsMods, string fileMods = "", string? logical = null) => $$"""
        <mets:mets xmlns:mets="http://www.loc.gov/METS/"
                   xmlns:xlink="http://www.w3.org/1999/xlink"
                   xmlns:premis="http://www.loc.gov/premis/v3"
                   xmlns:mods="http://www.loc.gov/mods/v3">
          <mets:dmdSec ID="DMD_objects">
            <mets:mdWrap MDTYPE="MODS"><mets:xmlData><mods:mods>{{objectsMods}}</mods:mods></mets:xmlData></mets:mdWrap>
          </mets:dmdSec>
          <mets:dmdSec ID="DMD_file">
            <mets:mdWrap MDTYPE="MODS"><mets:xmlData><mods:mods>{{fileMods}}</mods:mods></mets:xmlData></mets:mdWrap>
          </mets:dmdSec>
          <mets:dmdSec ID="DMD_log">
            <mets:mdWrap MDTYPE="MODS"><mets:xmlData><mods:mods>
              <mods:accessCondition type="tier">4</mods:accessCondition>
            </mods:mods></mets:xmlData></mets:mdWrap>
          </mets:dmdSec>
          <mets:amdSec ID="ADM_objects">
            <mets:techMD ID="TECH_objects"><mets:mdWrap MDTYPE="PREMIS:OBJECT"><mets:xmlData>
              <premis:object><premis:objectCharacteristics/><premis:originalName>objects</premis:originalName></premis:object>
            </mets:xmlData></mets:mdWrap></mets:techMD>
          </mets:amdSec>
          <mets:amdSec ID="ADM_tape">
            <mets:techMD ID="TECH_tape"><mets:mdWrap MDTYPE="PREMIS:OBJECT"><mets:xmlData>
              <premis:object><premis:objectCharacteristics><premis:fixity>
                <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm><premis:messageDigest>abc</premis:messageDigest>
              </premis:fixity></premis:objectCharacteristics></premis:object>
            </mets:xmlData></mets:mdWrap></mets:techMD>
          </mets:amdSec>
          <mets:fileSec><mets:fileGrp>
            <mets:file ID="FILE_tape" MIMETYPE="audio/x-wav"><mets:FLocat xlink:href="objects/tape.wav"/></mets:file>
          </mets:fileGrp></mets:fileSec>
          <mets:structMap TYPE="PHYSICAL">
            <mets:div ID="PHYS_objects" TYPE="Directory" LABEL="objects" DMDID="DMD_objects" ADMID="ADM_objects">
              <mets:div ID="PHYS_objects/tape.wav" TYPE="Item" {{(fileMods.Length > 0 ? "DMDID=\"DMD_file\"" : "")}}>
                <mets:fptr FILEID="FILE_tape"/>
              </mets:div>
            </mets:div>
          </mets:structMap>
          {{logical ?? ""}}
        </mets:mets>
        """;

    private const string Tier3 = """<mods:accessCondition type="tier">3</mods:accessCondition>""";
    private const string Pa1 = """<mods:accessCondition type="restriction on access">PA1</mods:accessCondition>""";
    private const string Rights = """<mods:accessCondition type="use and reproduction">http://rightsstatements.org/vocab/InC/1.0/</mods:accessCondition>""";

    [Fact]
    public void Other_typed_conditions_are_reported_by_type_and_the_chosen_ones_are_not()
    {
        var mets = Parse(Mets(Pa1 + Rights + Tier3 + """<mods:accessCondition type="dz">CC-BY-NC</mods:accessCondition>"""));
        var objects = mets.PhysicalStructure!.Directories.Single(d => d.Name == "objects");

        objects.AccessRestrictions.Should().Equal("PA1");
        objects.RightsStatement.Should().Be(new Uri("http://rightsstatements.org/vocab/InC/1.0/"));
        objects.DerivedAccessConditions.Should().Equal(new Dictionary<string, string> { ["tier"] = "3", ["dz"] = "CC-BY-NC" });
        objects.EffectiveDerivedAccessConditions.Should().Equal(objects.DerivedAccessConditions);
    }

    [Fact]
    public void A_file_inherits_derived_conditions_per_type_from_its_physical_parent()
    {
        var mets = Parse(Mets(
            Tier3 + """<mods:accessCondition type="dz">CC-BY-NC</mods:accessCondition>""",
            fileMods: """<mods:accessCondition type="tier">4</mods:accessCondition>"""));
        var file = mets.Files.Single();

        file.DerivedAccessConditions.Should().Equal(new Dictionary<string, string> { ["tier"] = "4" });
        file.EffectiveDerivedAccessConditions.Should().Equal(new Dictionary<string, string> { ["tier"] = "4", ["dz"] = "CC-BY-NC" },
            "own tier overrides the parent's, the parent's dz is inherited");
    }

    [Fact]
    public void A_file_with_a_silent_physical_chain_falls_back_to_its_whole_file_logical_range()
    {
        var logical = """
            <mets:structMap TYPE="LOGICAL">
              <mets:div ID="LOG_root" TYPE="Item" DMDID="DMD_log">
                <mets:fptr FILEID="FILE_tape"/>
              </mets:div>
            </mets:structMap>
            """;
        var mets = Parse(Mets("", logical: logical));

        mets.LogicalStructures.Single().DerivedAccessConditions.Should().Equal(new Dictionary<string, string> { ["tier"] = "4" });
        mets.Files.Single().DerivedAccessConditions.Should().BeNull();
        mets.Files.Single().EffectiveDerivedAccessConditions.Should().Equal(new Dictionary<string, string> { ["tier"] = "4" });
    }

    [Fact]
    public void Nothing_typed_means_empty_not_null_effective_dictionaries()
    {
        var mets = Parse(Mets(Pa1));
        mets.PhysicalStructure!.Directories.Single().DerivedAccessConditions.Should().BeNull();
        mets.PhysicalStructure!.Directories.Single().EffectiveDerivedAccessConditions.Should().BeEmpty();
        mets.Files.Single().EffectiveDerivedAccessConditions.Should().BeEmpty();
    }
}
