"""
Tests for the editability judge. Run from src/mets-editability/:

    python -m unittest tests -v

Two kinds: the acceptance table from CONTRACT.md over the repository's real sample corpus (the
measured #223 table re-expressed as verdicts - the .NET twin enforces the same table), and unit
cases over small synthetic documents for each native rule.
"""

import pathlib
import unittest

from lxml import etree, isoschematron

from app import judge, ncname, schematron

SAMPLES = pathlib.Path(__file__).resolve().parent.parent \
    / "DigitalPreservation" / "XmlGen.Tests" / "Samples"


def judge_sample(name: str) -> judge.Judgement:
    return judge.judge_file(SAMPLES / name)


def codes(findings) -> set[str]:
    return {finding.code for finding in findings}


class TheAcceptanceTable(unittest.TestCase):
    """CONTRACT.md's acceptance table, one test per row."""

    def test_simple_image_is_editable(self):
        judgement = judge_sample("simple-image.mets.xml")
        self.assertEqual(judgement.verdict, judge.EDITABLE)
        self.assertEqual(judgement.reasons, [])

    def test_wow_is_editable(self):
        judgement = judge_sample("wow.mets.xml")
        self.assertEqual(judgement.verdict, judge.EDITABLE)

    def test_legacy_platform_mets_is_editable_with_a_legacy_ids_note(self):
        judgement = judge_sample("path-fixture-spaces.xml")
        self.assertEqual(judgement.verdict, judge.EDITABLE)
        self.assertIn("LEGACY_IDS", codes(judgement.notes))

    def test_eprints_is_editable_with_normalisation(self):
        judgement = judge_sample("EPrints.10315.METS.xml")
        self.assertEqual(judgement.verdict, judge.EDITABLE_WITH_NORMALISATION)
        self.assertEqual(judgement.file_count, 4)
        self.assertEqual(codes(judgement.assumptions), {
            "UNTYPED_STRUCTMAP_ASSUMED_PHYSICAL", "UNTYPED_DIV_ASSUMED_ITEM",
            "IMPLIED_OBJECTS_DIV"})
        self.assertIn("METS_NAMESPACE_RECORD_INFO", codes(judgement.notes))

    def test_eprints_mutations_are_the_02e_contract_in_order(self):
        judgement = judge_sample("EPrints.10315.METS.xml")
        self.assertEqual(judgement.mutations, [
            'set TYPE="PHYSICAL" on the structMap',
            'set TYPE="Directory" on the root div',
            'set TYPE="Item" on 4 file div(s)',
            "materialise the objects Directory div (amdSec/techMD with premis:originalName) "
            "and re-parent 4 file div(s) under it",
            'consolidate 4 fileGrp(s) into one USE="OBJECTS" group',
            "wrap the payload of 4 mdWrap(s) in the mets:xmlData element the schema requires",
            "append the platform agent to metsHdr",
        ])

    def test_eprints_quirks_are_noted(self):
        judgement = judge_sample("EPrints.10315.METS.xml")
        self.assertIn("FOREIGN_DMDSEC", codes(judgement.notes))
        self.assertIn("NO_XMLDATA_WRAPPER", codes(judgement.notes))

    def test_archivematica_is_navigable_read_only(self):
        judgement = judge_sample(
            "archivematica-wc-METS.299eb16f-1e62-4bf6-b259-c82146153711.xml")
        self.assertEqual(judgement.verdict, judge.NAVIGABLE_READ_ONLY)
        self.assertEqual(judgement.file_count, 38)
        self.assertIn("DIRECTORY_DIV_NO_ADMID", codes(judgement.notes))
        self.assertIn("CASE_INSENSITIVE_STRUCTMAP_TYPE", codes(judgement.assumptions))

    def test_goobi_wellcome_is_navigable_read_only(self):
        # Relative paths (a bagged Wellcome item), but typed page divs, ALTO outside objects/,
        # no SHA256 - neither tier. The platform's living-editor policy sits on top of this
        # verdict; the judge only reports what the document shows.
        judgement = judge_sample("goobi-wc-b29356350.xml")
        self.assertEqual(judgement.verdict, judge.NAVIGABLE_READ_ONLY)

    def test_goobi_2026_is_not_editable(self):
        judgement = judge_sample("goobi-2026.xml")
        self.assertEqual(judgement.verdict, judge.NOT_EDITABLE)
        self.assertIn("HREF_NOT_DEPOSIT_RELATIVE", codes(judgement.reasons))


def build(struct_map="", file_sec="", amd_sec="", header=""):
    """A minimal METS document around the given sections."""
    xml = f"""<mets:mets xmlns:mets="http://www.loc.gov/METS/"
        xmlns:xlink="http://www.w3.org/1999/xlink"
        xmlns:premis="http://www.loc.gov/premis/v3"
        xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
      {header}{amd_sec}{file_sec}{struct_map}
    </mets:mets>"""
    return judge.judge(etree.fromstring(xml.encode()))


def eprints_like(href="objects/a.jpg", use="reference", file_id="eprint_1_1",
                 admid='ADMID="AMD_1"', algorithm="SHA256", digest="abc", fptr_id=None,
                 wrapped=False, root_attrs="", div_id="", extra=""):
    """
    The smallest document that reaches the EPrints tier, with one knob per test. `wrapped`
    puts the premis:object inside a proper mets:xmlData (EPrints itself does not); `extra`
    appends further sections (logical structMaps, structLinks, dmdSecs).
    """
    open_wrap = "<mets:xmlData>" if wrapped else ""
    close_wrap = "</mets:xmlData>" if wrapped else ""
    return build(
        header="""<mets:metsHdr><mets:agent ROLE="CREATOR" TYPE="OTHER" OTHERTYPE="SOFTWARE">
            <mets:name>EPrints 3.3.15</mets:name></mets:agent></mets:metsHdr>""",
        amd_sec=f"""<mets:amdSec ID="AMD_0"><mets:techMD ID="AMD_1">
            <mets:mdWrap MDTYPE="OTHER" MIMETYPE="text/xml">{open_wrap}<premis:object>
            <premis:objectCharacteristics><premis:fixity>
            <premis:messageDigestAlgorithm>{algorithm}</premis:messageDigestAlgorithm>
            <premis:messageDigest>{digest}</premis:messageDigest>
            </premis:fixity></premis:objectCharacteristics>
            </premis:object>{close_wrap}</mets:mdWrap></mets:techMD></mets:amdSec>""",
        file_sec=f"""<mets:fileSec><mets:fileGrp USE="{use}">
            <mets:file ID="{file_id}" {admid}>
            <mets:FLocat LOCTYPE="URL" xlink:type="simple" xlink:href="{href}"/>
            </mets:file></mets:fileGrp></mets:fileSec>""",
        struct_map=f"""<mets:structMap><mets:div {root_attrs}>
            <mets:div {div_id}><mets:fptr FILEID="{fptr_id or file_id}"/></mets:div>
            </mets:div></mets:structMap>{extra}""")


class TheNativeRules(unittest.TestCase):

    def test_the_smallest_eprints_document_reaches_the_tier(self):
        judgement = eprints_like()
        self.assertEqual(judgement.verdict, judge.EDITABLE_WITH_NORMALISATION)

    def test_an_unresolved_fileid_is_not_editable(self):
        judgement = eprints_like(fptr_id="nothing_declares_this")
        self.assertEqual(judgement.verdict, judge.NOT_EDITABLE)
        self.assertIn("FILEID_UNRESOLVED", codes(judgement.reasons))

    def test_an_absolute_href_fails_the_deposit_relative_guard(self):
        judgement = eprints_like(href="https://example.org/a.jpg")
        self.assertEqual(judgement.verdict, judge.NOT_EDITABLE)
        self.assertIn("HREF_NOT_DEPOSIT_RELATIVE", codes(judgement.reasons))

    def test_a_file_scheme_href_fails_the_guard(self):
        judgement = eprints_like(href="file:///usr/share/eprints/a.jpg")
        self.assertIn("HREF_NOT_DEPOSIT_RELATIVE", codes(judgement.reasons))

    def test_a_dotdot_segment_fails_the_guard(self):
        judgement = eprints_like(href="objects/../secrets.txt")
        self.assertIn("HREF_NOT_DEPOSIT_RELATIVE", codes(judgement.reasons))

    def test_a_rootward_href_fails_the_guard(self):
        judgement = eprints_like(href="/objects/a.jpg")
        self.assertIn("HREF_NOT_DEPOSIT_RELATIVE", codes(judgement.reasons))

    def test_a_duplicate_declared_id_is_a_blocker(self):
        judgement = build(
            file_sec="""<mets:fileSec><mets:fileGrp USE="reference">
                <mets:file ID="f1"><mets:FLocat xlink:href="objects/a.jpg"/></mets:file>
                <mets:file ID="f1"><mets:FLocat xlink:href="objects/b.jpg"/></mets:file>
                </mets:fileGrp></mets:fileSec>""",
            struct_map="""<mets:structMap><mets:div>
                <mets:div><mets:fptr FILEID="f1"/></mets:div>
                </mets:div></mets:structMap>""")
        self.assertEqual(judgement.verdict, judge.NOT_EDITABLE)
        self.assertIn("DUPLICATE_ID", codes(judgement.reasons))

    def test_two_files_claiming_one_path_is_a_blocker(self):
        judgement = build(
            file_sec="""<mets:fileSec><mets:fileGrp USE="reference">
                <mets:file ID="f1"><mets:FLocat xlink:href="objects/a.jpg"/></mets:file>
                <mets:file ID="f2"><mets:FLocat xlink:href="objects/./a.jpg"/></mets:file>
                </mets:fileGrp></mets:fileSec>""",
            struct_map="""<mets:structMap><mets:div>
                <mets:div><mets:fptr FILEID="f1"/></mets:div>
                <mets:div><mets:fptr FILEID="f2"/></mets:div>
                </mets:div></mets:structMap>""")
        self.assertIn("DUPLICATE_PATH", codes(judgement.reasons))

    def test_a_logical_only_document_has_no_physical_structmap(self):
        judgement = build(
            struct_map="""<mets:structMap TYPE="LOGICAL"><mets:div/></mets:structMap>""")
        self.assertEqual(judgement.verdict, judge.NOT_EDITABLE)
        self.assertIn("NO_PHYSICAL_STRUCTMAP", codes(judgement.reasons))

    def test_mixed_filegrp_use_fails_the_eprints_tier(self):
        judgement = build(
            header="""<mets:metsHdr><mets:agent><mets:name>EPrints</mets:name></mets:agent>
                </mets:metsHdr>""",
            file_sec="""<mets:fileSec>
                <mets:fileGrp USE="reference">
                <mets:file ID="f1"><mets:FLocat xlink:href="objects/a.jpg"/></mets:file>
                </mets:fileGrp>
                <mets:fileGrp USE="original">
                <mets:file ID="f2"><mets:FLocat xlink:href="objects/b.jpg"/></mets:file>
                </mets:fileGrp></mets:fileSec>""",
            struct_map="""<mets:structMap><mets:div>
                <mets:div><mets:fptr FILEID="f1"/></mets:div>
                <mets:div><mets:fptr FILEID="f2"/></mets:div>
                </mets:div></mets:structMap>""")
        self.assertEqual(judgement.verdict, judge.NAVIGABLE_READ_ONLY)
        self.assertIn("E_MIXED_FILEGRP_USE", codes(judgement.reasons))

    def test_missing_sha256_fails_the_eprints_tier(self):
        judgement = eprints_like(algorithm="MD5")
        self.assertEqual(judgement.verdict, judge.NAVIGABLE_READ_ONLY)
        self.assertIn("E_SHA256", codes(judgement.reasons))

    def test_sha_256_with_hyphen_and_lowercase_are_accepted(self):
        for spelling in ("SHA-256", "sha256", "sha-256"):
            with self.subTest(spelling=spelling):
                self.assertEqual(eprints_like(algorithm=spelling).verdict,
                                 judge.EDITABLE_WITH_NORMALISATION)

    def test_an_illegal_id_demotes_the_eprints_tier_to_read_only(self):
        # An EPrints-shaped document whose IDs need the #188 normalisation is not this
        # tier's to restructure: normalisation is a different, prior operation.
        judgement = eprints_like(file_id="eprint 1 1", fptr_id="eprint 1 1")
        self.assertEqual(judgement.verdict, judge.NAVIGABLE_READ_ONLY)
        self.assertIn("INVALID_IDS", codes(judgement.reasons))

    def test_a_foreign_storage_assertion_is_noted_not_read(self):
        judgement = build(
            header="""<mets:metsHdr><mets:agent><mets:name>EPrints 3.3.15</mets:name>
                </mets:agent></mets:metsHdr>""",
            amd_sec="""<mets:amdSec ID="AMD_0"><mets:techMD ID="AMD_1">
                <mets:mdWrap MDTYPE="OTHER" MIMETYPE="text/xml"><premis:object>
                <premis:storage><premis:contentLocation>
                <premis:contentLocationType>URL</premis:contentLocationType>
                <premis:contentLocationValue>file:///usr/share/eprints/a.jpg</premis:contentLocationValue>
                </premis:contentLocation></premis:storage>
                <premis:objectCharacteristics><premis:fixity>
                <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                <premis:messageDigest>abc</premis:messageDigest>
                </premis:fixity></premis:objectCharacteristics>
                </premis:object></mets:mdWrap></mets:techMD></mets:amdSec>""",
            file_sec="""<mets:fileSec><mets:fileGrp USE="reference">
                <mets:file ID="f1" ADMID="AMD_1">
                <mets:FLocat xlink:href="objects/a.jpg"/></mets:file>
                </mets:fileGrp></mets:fileSec>""",
            struct_map="""<mets:structMap><mets:div>
                <mets:div><mets:fptr FILEID="f1"/></mets:div>
                </mets:div></mets:structMap>""")
        self.assertEqual(judgement.verdict, judge.EDITABLE_WITH_NORMALISATION)
        self.assertIn("FOREIGN_STORAGE_LOCATION", codes(judgement.notes))

    def test_unparseable_xml_is_not_editable(self):
        judgement = judge.judge_file(pathlib.Path(__file__))  # this file is not XML
        self.assertEqual(judgement.verdict, judge.NOT_EDITABLE)
        self.assertEqual(codes(judgement.reasons), {"PARSE_FAILED"})


def platform_like(algorithm="SHA256", digest="abc"):
    """The smallest document that reaches the platform tier."""
    return build(
        header="""<mets:metsHdr><mets:agent ROLE="CREATOR" TYPE="OTHER" OTHERTYPE="SOFTWARE">
            <mets:name>University of Leeds Digital Library Infrastructure Project</mets:name>
            </mets:agent></mets:metsHdr>""",
        amd_sec=f"""<mets:amdSec ID="ADM_objects_x002F_a.jpg">
            <mets:techMD ID="TECH_objects_x002F_a.jpg">
            <mets:mdWrap MDTYPE="PREMIS:OBJECT"><mets:xmlData><premis:object>
            <premis:objectCharacteristics><premis:fixity>
            <premis:messageDigestAlgorithm>{algorithm}</premis:messageDigestAlgorithm>
            <premis:messageDigest>{digest}</premis:messageDigest>
            </premis:fixity></premis:objectCharacteristics>
            <premis:originalName>objects/a.jpg</premis:originalName>
            </premis:object></mets:xmlData></mets:mdWrap></mets:techMD></mets:amdSec>""",
        file_sec="""<mets:fileSec><mets:fileGrp USE="OBJECTS">
            <mets:file ID="FILE_objects_x002F_a.jpg" ADMID="ADM_objects_x002F_a.jpg">
            <mets:FLocat LOCTYPE="URL" xlink:type="simple" xlink:href="objects/a.jpg"/>
            </mets:file></mets:fileGrp></mets:fileSec>""",
        struct_map="""<mets:structMap TYPE="PHYSICAL">
            <mets:div ID="PHYS_ROOT" LABEL="__ROOT" TYPE="Directory">
            <mets:div ID="PHYS_objects_x002F_a.jpg" LABEL="a.jpg" TYPE="Item">
            <mets:fptr FILEID="FILE_objects_x002F_a.jpg"/></mets:div>
            </mets:div></mets:structMap>""")


class TheEditableSurface(unittest.TestCase):
    """
    Editability covers everything the platform can edit - logical structMaps (with time and
    region parts), file links, descriptive metadata - not just the physical tree. These
    synthesised documents extend the minimal EPrints shape one feature at a time: resolvable
    linkage keeps the tier ("I understand it and I can change it"); dangling linkage loses it.
    """

    def test_a_platform_style_file_link_with_a_role_keeps_the_tier(self):
        judgement = eprints_like(extra="""<mets:structLink>
            <mets:smLink xlink:from="eprint_1_1" xlink:to="eprint_1_1"
                xlink:arcrole="http://iiif.io/api/presentation/3#transcript"/>
            </mets:structLink>""")
        self.assertEqual(judgement.verdict, judge.EDITABLE_WITH_NORMALISATION)

    def test_a_goobi_style_div_link_that_resolves_keeps_the_tier(self):
        judgement = eprints_like(
            div_id='ID="PHYS_1"',
            extra="""<mets:structMap TYPE="LOGICAL">
                <mets:div ID="LOG_1" TYPE="Item" LABEL="The item"/>
                </mets:structMap>
                <mets:structLink>
                <mets:smLink xlink:from="LOG_1" xlink:to="PHYS_1"/>
                </mets:structLink>""")
        self.assertEqual(judgement.verdict, judge.EDITABLE_WITH_NORMALISATION)

    def test_a_dangling_link_end_demotes_to_read_only(self):
        judgement = eprints_like(extra="""<mets:structLink>
            <mets:smLink xlink:from="eprint_1_1" xlink:to="nothing_declares_this"
                xlink:arcrole="http://iiif.io/api/presentation/3#transcript"/>
            </mets:structLink>""")
        self.assertEqual(judgement.verdict, judge.NAVIGABLE_READ_ONLY)
        self.assertIn("C_SMLINK_TO_RESOLVES", codes(judgement.reasons))

    def test_a_logical_structmap_with_time_and_region_parts_keeps_the_tier(self):
        judgement = eprints_like(extra="""<mets:structMap TYPE="LOGICAL">
            <mets:div ID="LOG_0" TYPE="Collection" LABEL="All of it">
              <mets:div ID="LOG_1" TYPE="Item" LABEL="Whole file">
                <mets:fptr FILEID="eprint_1_1"/>
              </mets:div>
              <mets:div ID="LOG_2" TYPE="Item" LABEL="A time segment">
                <mets:fptr><mets:area FILEID="eprint_1_1" BETYPE="TIME"
                    BEGIN="00:00:10" END="00:01:00"/></mets:fptr>
              </mets:div>
              <mets:div ID="LOG_3" TYPE="Item" LABEL="An image region">
                <mets:fptr><mets:area FILEID="eprint_1_1" SHAPE="RECT"
                    COORDS="0,0,100,100"/></mets:fptr>
              </mets:div>
            </mets:div></mets:structMap>""")
        self.assertEqual(judgement.verdict, judge.EDITABLE_WITH_NORMALISATION)

    def test_a_logical_area_pointing_at_nothing_demotes_to_read_only(self):
        judgement = eprints_like(extra="""<mets:structMap TYPE="LOGICAL">
            <mets:div ID="LOG_1" TYPE="Item" LABEL="Broken segment">
              <mets:fptr><mets:area FILEID="nothing_declares_this" BETYPE="TIME"
                  BEGIN="00:00:10" END="00:01:00"/></mets:fptr>
            </mets:div></mets:structMap>""")
        self.assertEqual(judgement.verdict, judge.NAVIGABLE_READ_ONLY)
        self.assertIn("C_AREA_FILEID_RESOLVES", codes(judgement.reasons))

    def test_a_dangling_logical_fptr_demotes_to_read_only(self):
        judgement = eprints_like(extra="""<mets:structMap TYPE="LOGICAL">
            <mets:div ID="LOG_1" TYPE="Item" LABEL="Broken">
              <mets:fptr FILEID="nothing_declares_this"/>
            </mets:div></mets:structMap>""")
        self.assertEqual(judgement.verdict, judge.NAVIGABLE_READ_ONLY)
        self.assertIn("C_FILEID_RESOLVES", codes(judgement.reasons))

    def test_a_foreign_dmdsec_is_noted_and_never_edited(self):
        # The EPrints root dmdSec: claims MODS, holds no mods:mods. The note carries the rule -
        # an edit appends a platform dmdSec ID to the div's DMDID, theirs stays untouched.
        judgement = eprints_like(
            root_attrs='DMDID="DMD_eprint_1"',
            extra="""<mets:dmdSec ID="DMD_eprint_1"><mets:mdWrap MDTYPE="MODS">
                <mets:xmlData><mets:recordInfo>
                <mets:recordIdentifier source="EPrints">1</mets:recordIdentifier>
                </mets:recordInfo></mets:xmlData>
                </mets:mdWrap></mets:dmdSec>""")
        self.assertEqual(judgement.verdict, judge.EDITABLE_WITH_NORMALISATION)
        self.assertIn("FOREIGN_DMDSEC", codes(judgement.notes))

    def test_a_real_mods_dmdsec_is_not_foreign(self):
        judgement = eprints_like(
            root_attrs='DMDID="DMD_1"',
            extra="""<mets:dmdSec ID="DMD_1"><mets:mdWrap MDTYPE="MODS"><mets:xmlData>
                <mods:mods xmlns:mods="http://www.loc.gov/mods/v3">
                <mods:titleInfo><mods:title>Fine</mods:title></mods:titleInfo>
                </mods:mods></mets:xmlData></mets:mdWrap></mets:dmdSec>""")
        self.assertEqual(judgement.verdict, judge.EDITABLE_WITH_NORMALISATION)
        self.assertNotIn("FOREIGN_DMDSEC", codes(judgement.notes))

    def test_an_unwrapped_mdwrap_payload_is_noted_and_repaired_on_save(self):
        judgement = eprints_like()  # the builder is faithful to EPrints: no xmlData wrapper
        self.assertIn("NO_XMLDATA_WRAPPER", codes(judgement.notes))
        self.assertIn(
            "wrap the payload of 1 mdWrap(s) in the mets:xmlData element the schema requires",
            judgement.mutations)

    def test_a_wrapped_payload_needs_no_repair(self):
        judgement = eprints_like(wrapped=True)
        self.assertEqual(judgement.verdict, judge.EDITABLE_WITH_NORMALISATION)
        self.assertNotIn("NO_XMLDATA_WRAPPER", codes(judgement.notes))
        self.assertFalse([m for m in judgement.mutations if m.startswith("wrap the payload")])

    def test_the_smallest_platform_document_is_editable(self):
        self.assertEqual(platform_like().verdict, judge.EDITABLE)

    def test_a_platform_document_without_fixity_is_read_only(self):
        # Every edit ends in an import job, and import jobs require SHA256 - a platform-shape
        # document that has lost its digests cannot complete an edit-and-preserve.
        judgement = platform_like(algorithm="MD5")
        self.assertEqual(judgement.verdict, judge.NAVIGABLE_READ_ONLY)
        self.assertIn("P_SHA256", codes(judgement.reasons))

    def test_a_logical_structmap_without_a_root_id_demotes_to_read_only(self):
        # Logical structMaps are edited by address: replaced, reordered, removed by root div ID.
        # An ID-less one is present but unchangeable - which editable must not mean.
        judgement = eprints_like(extra="""<mets:structMap TYPE="LOGICAL">
            <mets:div TYPE="Item" LABEL="Unaddressable"/>
            </mets:structMap>""")
        self.assertEqual(judgement.verdict, judge.NAVIGABLE_READ_ONLY)
        self.assertIn("C_LOGICAL_ROOT_HAS_ID", codes(judgement.reasons))


class TheAdversarialReviewFindings(unittest.TestCase):
    """One test per fixed finding from the 2026-08-21 adversarial review of PR #238."""

    def test_a_physical_fptr_without_fileid_demotes(self):
        # The platform's physical walk calls GetRequiredFileId on every fptr; an area-only
        # fptr (legal in a logical map, where the platform itself writes them) crashes it.
        judgement = build(
            file_sec="""<mets:fileSec><mets:fileGrp USE="reference">
                <mets:file ID="f1"><mets:FLocat xlink:href="objects/a.jpg"/></mets:file>
                </mets:fileGrp></mets:fileSec>""",
            struct_map="""<mets:structMap><mets:div>
                <mets:div><mets:fptr><mets:area FILEID="f1" BETYPE="TIME"
                    BEGIN="00:00:01" END="00:00:02"/></mets:fptr></mets:div>
                </mets:div></mets:structMap>""")
        self.assertEqual(judgement.verdict, judge.NAVIGABLE_READ_ONLY)
        self.assertIn("C_PHYSICAL_FPTR_HAS_FILEID", codes(judgement.reasons))

    def test_a_file_with_two_flocats_demotes(self):
        # The platform's parser takes .Single() FLocat per file; two locations throw it.
        judgement = build(
            file_sec="""<mets:fileSec><mets:fileGrp USE="reference">
                <mets:file ID="f1">
                <mets:FLocat xlink:href="objects/a.jpg"/>
                <mets:FLocat xlink:href="objects/mirror/a.jpg"/>
                </mets:file></mets:fileGrp></mets:fileSec>""",
            struct_map="""<mets:structMap><mets:div>
                <mets:div><mets:fptr FILEID="f1"/></mets:div>
                </mets:div></mets:structMap>""")
        self.assertEqual(judgement.verdict, judge.NAVIGABLE_READ_ONLY)
        self.assertIn("C_ONE_FLOCAT", codes(judgement.reasons))

    def test_an_empty_digest_fails_the_fixity_rule(self):
        # An algorithm label with no digest value is a record of having lost the checksum.
        judgement = eprints_like(digest="")
        self.assertEqual(judgement.verdict, judge.NAVIGABLE_READ_ONLY)
        self.assertIn("E_SHA256", codes(judgement.reasons))

    def test_a_platform_file_with_an_empty_digest_is_read_only(self):
        judgement = platform_like(digest="")
        self.assertEqual(judgement.verdict, judge.NAVIGABLE_READ_ONLY)
        self.assertIn("P_SHA256", codes(judgement.reasons))

    def test_a_useless_first_filegrp_still_counts_as_mixed(self):
        # XPath != against a missing attribute is silently false; the second clause of
        # E_MIXED_FILEGRP_USE catches presence-mixing.
        judgement = build(
            header="""<mets:metsHdr><mets:agent><mets:name>EPrints</mets:name></mets:agent>
                </mets:metsHdr>""",
            file_sec="""<mets:fileSec>
                <mets:fileGrp>
                <mets:file ID="f1"><mets:FLocat xlink:href="objects/a.jpg"/></mets:file>
                </mets:fileGrp>
                <mets:fileGrp USE="original">
                <mets:file ID="f2"><mets:FLocat xlink:href="objects/b.jpg"/></mets:file>
                </mets:fileGrp></mets:fileSec>""",
            struct_map="""<mets:structMap><mets:div>
                <mets:div><mets:fptr FILEID="f1"/></mets:div>
                <mets:div><mets:fptr FILEID="f2"/></mets:div>
                </mets:div></mets:structMap>""")
        self.assertEqual(judgement.verdict, judge.NAVIGABLE_READ_ONLY)
        self.assertIn("E_MIXED_FILEGRP_USE", codes(judgement.reasons))

    def test_an_empty_document_gives_its_reason(self):
        # The platform's own freshly-created deposit skeleton: structure, no files yet.
        # A verdict must carry its reasons.
        judgement = build(
            header="""<mets:metsHdr><mets:agent><mets:name>University of Leeds Digital
                Library Infrastructure Project</mets:name></mets:agent></mets:metsHdr>""",
            file_sec="""<mets:fileSec><mets:fileGrp USE="OBJECTS"/></mets:fileSec>""",
            struct_map="""<mets:structMap TYPE="PHYSICAL">
                <mets:div ID="PHYS_ROOT" LABEL="__ROOT" TYPE="Directory">
                <mets:div ID="PHYS_objects" LABEL="objects" TYPE="Directory" ADMID="ADM_objects"/>
                </mets:div></mets:structMap>""")
        self.assertEqual(judgement.verdict, judge.NOT_EDITABLE)
        self.assertEqual(codes(judgement.reasons), {"NO_FILES"})

    def test_one_file_referenced_from_two_divs_is_not_a_duplicate_path(self):
        judgement = eprints_like(extra="")
        # Reference the same file from a second div via a second structMap-level div: use the
        # builder's own structMap plus a logical map pointing at the same file, and also a
        # physical second div sharing the file.
        judgement = build(
            header="""<mets:metsHdr><mets:agent><mets:name>EPrints 3.3.15</mets:name>
                </mets:agent></mets:metsHdr>""",
            amd_sec="""<mets:amdSec ID="AMD_0"><mets:techMD ID="AMD_1">
                <mets:mdWrap MDTYPE="OTHER" MIMETYPE="text/xml"><premis:object>
                <premis:objectCharacteristics><premis:fixity>
                <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                <premis:messageDigest>abc</premis:messageDigest>
                </premis:fixity></premis:objectCharacteristics>
                </premis:object></mets:mdWrap></mets:techMD></mets:amdSec>""",
            file_sec="""<mets:fileSec><mets:fileGrp USE="reference">
                <mets:file ID="f1" ADMID="AMD_1">
                <mets:FLocat xlink:href="objects/a.jpg"/></mets:file>
                </mets:fileGrp></mets:fileSec>""",
            struct_map="""<mets:structMap><mets:div>
                <mets:div><mets:fptr FILEID="f1"/></mets:div>
                <mets:div><mets:fptr FILEID="f1"/></mets:div>
                </mets:div></mets:structMap>""")
        self.assertNotIn("DUPLICATE_PATH", codes(judgement.reasons))
        self.assertEqual(judgement.verdict, judge.EDITABLE_WITH_NORMALISATION)
        self.assertEqual(judgement.file_count, 1)

    def test_an_unchosen_second_structmap_cannot_demote(self):
        # CONTRACT.md: with several candidates, the first is judged. A nested sibling map is
        # preserved as parsed, never edited - it must not fail the tier for the chosen one.
        judgement = eprints_like(extra="""<mets:structMap>
            <mets:div><mets:div><mets:div>
            <mets:fptr FILEID="eprint_1_1"/>
            </mets:div></mets:div></mets:div></mets:structMap>""")
        self.assertEqual(judgement.verdict, judge.EDITABLE_WITH_NORMALISATION)
        self.assertIn("MULTIPLE_PHYSICAL_CANDIDATES", codes(judgement.notes))

    def test_a_conformant_unchosen_map_cannot_carry_the_platform_tier(self):
        # The walked map and the validated map must be the same map: a lowercase-typed first
        # candidate is the chosen one, and a perfect TYPE="PHYSICAL" sibling cannot stand in.
        judgement = build(
            header="""<mets:metsHdr><mets:agent><mets:name>University of Leeds Digital
                Library Infrastructure Project</mets:name></mets:agent></mets:metsHdr>""",
            amd_sec="""<mets:amdSec ID="ADM_objects_x002F_a.jpg">
                <mets:techMD ID="TECH_objects_x002F_a.jpg">
                <mets:mdWrap MDTYPE="PREMIS:OBJECT"><mets:xmlData><premis:object>
                <premis:objectCharacteristics><premis:fixity>
                <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                <premis:messageDigest>abc</premis:messageDigest>
                </premis:fixity></premis:objectCharacteristics>
                </premis:object></mets:xmlData></mets:mdWrap></mets:techMD></mets:amdSec>""",
            file_sec="""<mets:fileSec><mets:fileGrp USE="OBJECTS">
                <mets:file ID="FILE_1" ADMID="ADM_objects_x002F_a.jpg">
                <mets:FLocat xlink:href="objects/a.jpg"/></mets:file>
                </mets:fileGrp></mets:fileSec>""",
            struct_map="""<mets:structMap TYPE="physical"><mets:div>
                <mets:div><mets:div><mets:fptr FILEID="FILE_1"/></mets:div></mets:div>
                </mets:div></mets:structMap>
                <mets:structMap TYPE="PHYSICAL">
                <mets:div ID="PHYS_ROOT" TYPE="Directory">
                <mets:div ID="PHYS_1" TYPE="Item"><mets:fptr FILEID="FILE_1"/></mets:div>
                </mets:div></mets:structMap>""")
        self.assertEqual(judgement.verdict, judge.NAVIGABLE_READ_ONLY)
        self.assertIn("P_PHYSICAL_STRUCTMAP", codes(judgement.reasons))

    def test_a_missing_file_is_a_judgement_not_a_crash(self):
        judgement = judge.judge_file(pathlib.Path("does-not-exist.xml"))
        self.assertEqual(judgement.verdict, judge.NOT_EDITABLE)
        self.assertEqual(codes(judgement.reasons), {"PARSE_FAILED"})

    def test_an_id_with_a_trailing_newline_is_not_legal(self):
        self.assertFalse(ncname.is_valid_id("FILE_1\n"))

    def test_the_platform_agent_string_is_pinned_to_the_platform(self):
        # PLATFORM_AGENT is a copy of Constants.MetsCreatorAgent; like ncname_ranges.json,
        # the copy must not be able to drift silently.
        constants = (pathlib.Path(__file__).parent.parent / "DigitalPreservation"
                     / "DigitalPreservation.Mets" / "Constants.cs").read_text(encoding="utf-8")
        self.assertIn(f'MetsCreatorAgent = "{judge.PLATFORM_AGENT}"', constants)


class TheSharedAuthorities(unittest.TestCase):
    """The pins that keep the copies honest."""

    def test_the_ncname_ranges_are_the_migration_tools_ranges(self):
        # One authority (XmlConvert, pinned by NCNameRangesTests.cs), two consumers. If the
        # migration tool's file changes, this copy must change with it.
        ours = pathlib.Path(__file__).parent / "app" / "ncname_ranges.json"
        theirs = pathlib.Path(__file__).parent.parent \
            / "mets-id-migration" / "app" / "ncname_ranges.json"
        self.assertEqual(ours.read_bytes(), theirs.read_bytes())

    def test_the_compiled_xslt_behaves_like_the_schematron_source(self):
        # The .NET judge runs schematron/compiled/*.xsl; regenerating is a manual step
        # (tools/compile_schematron.py), so prove behavioural equivalence rather than trusting
        # the checkout: both forms must fail the same asserts on a real document.
        document = etree.parse(
            str(SAMPLES / "archivematica-wc-METS.299eb16f-1e62-4bf6-b259-c82146153711.xml"))
        for name in ("common", "eprints-tier", "platform-tier"):
            with self.subTest(tier=name):
                direct = {code for code, _ in schematron.failures(
                    f"{name}.sch", document.getroot())}
                compiled = etree.XSLT(etree.parse(
                    str(schematron.SCHEMATRON_DIR / "compiled" / f"{name}.xsl")))
                svrl = compiled(document)
                via_compiled = {
                    failed.get("id") for failed in svrl.getroot().iter(
                        f"{{{schematron.SVRL_NS}}}failed-assert")}
                self.assertEqual(direct, via_compiled)

    def test_ncname_agrees_with_the_platform_on_the_known_edge_cases(self):
        # The fourth/fifth edition gap: XmlConvert (the platform) rejects these letters.
        for char in ("Ĳ", "ĳ", "ſ"):
            self.assertFalse(ncname.is_valid_id(f"a{char}"), repr(char))
        self.assertTrue(ncname.is_valid_id("eprint_10315_370441"))
        self.assertFalse(ncname.is_valid_id("PHYS_objects/a.tif"))
        self.assertFalse(ncname.is_valid_id("FILE a b"))
        self.assertFalse(ncname.is_valid_id(""))


if __name__ == "__main__":
    unittest.main()
