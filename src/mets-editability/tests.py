"""
Tests for the editability judge. Run from src/mets-editability/:

    python -m unittest tests -v

Three kinds: the acceptance table from CONTRACT.md over the repository's real sample corpus (the
measured #223 table re-expressed as verdicts); the golden judgements in fixtures/judgements.json,
one curated document per rule and parity edge; and unit cases over small synthetic documents.
The .NET twin (EditabilityJudgeTests.cs) asserts the same table and the same golden file.
"""

import json
import pathlib
import tempfile
import threading
import unittest

from lxml import etree

from app import judge, ncname, schematron
from tools import compile_schematron

HERE = pathlib.Path(__file__).resolve().parent
SAMPLES = HERE.parent / "DigitalPreservation" / "XmlGen.Tests" / "Samples"
FIXTURES = HERE / "fixtures"


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

    def test_wow_is_read_only_for_its_placeholder_digests(self):
        # A hand-made platform-shaped fixture whose "SHA256" digests are 8-character
        # placeholders: no import job could ever match them.
        judgement = judge_sample("wow.mets.xml")
        self.assertEqual(judgement.verdict, judge.NAVIGABLE_READ_ONLY)
        self.assertIn("P_SHA256", codes(judgement.reasons))

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
            'set LABEL="__ROOT" on the root div',
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

    def test_no_goobi_document_in_the_corpus_is_ever_editable(self):
        # The judge judges structure, and the living-editor policy (Goobi still edits its own
        # documents) is the platform's overlay - but the structural verdict alone must never
        # assert editability for any real Goobi output we hold. All seven samples, pinned.
        goobi_samples = sorted(SAMPLES.glob("goobi*.xml"))
        self.assertGreaterEqual(len(goobi_samples), 7)
        for sample in goobi_samples:
            with self.subTest(sample=sample.name):
                verdict = judge.judge_file(sample).verdict
                self.assertNotIn(verdict, (judge.EDITABLE, judge.EDITABLE_WITH_NORMALISATION))


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
                 admid='ADMID="AMD_1"', algorithm="SHA256", digest="ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", fptr_id=None,
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
                <mets:file ID="f2"><mets:FLocat xlink:href="objects/a.jpg"/></mets:file>
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
                <premis:messageDigest>ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad</premis:messageDigest>
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


def platform_like(algorithm="SHA256", digest="ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"):
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


class TheReviewFindings(unittest.TestCase):
    """Tests for findings fixed after the adversarial reviews of PR #238."""

    def test_a_physical_fptr_without_fileid_is_not_editable(self):
        # The parser calls GetRequiredFileId on every physical fptr; an area-only fptr (legal in
        # a logical map, where the platform itself writes them) crashes it - so the platform
        # cannot open the document at all.
        judgement = build(
            file_sec="""<mets:fileSec><mets:fileGrp USE="reference">
                <mets:file ID="f1"><mets:FLocat xlink:href="objects/a.jpg"/></mets:file>
                </mets:fileGrp></mets:fileSec>""",
            struct_map="""<mets:structMap><mets:div>
                <mets:div><mets:fptr><mets:area FILEID="f1" BETYPE="TIME"
                    BEGIN="00:00:01" END="00:00:02"/></mets:fptr></mets:div>
                </mets:div></mets:structMap>""")
        self.assertEqual(judgement.verdict, judge.NOT_EDITABLE)
        self.assertEqual(codes(judgement.reasons), {"FPTR_NO_FILEID"})

    def test_a_referenced_file_with_two_flocats_is_not_editable(self):
        # The parser takes .Single() FLocat of every file the physical map references.
        judgement = build(
            file_sec="""<mets:fileSec><mets:fileGrp USE="reference">
                <mets:file ID="f1">
                <mets:FLocat xlink:href="objects/a.jpg"/>
                <mets:FLocat xlink:href="objects/mirror/a.jpg"/>
                </mets:file></mets:fileGrp></mets:fileSec>""",
            struct_map="""<mets:structMap><mets:div>
                <mets:div><mets:fptr FILEID="f1"/></mets:div>
                </mets:div></mets:structMap>""")
        self.assertEqual(judgement.verdict, judge.NOT_EDITABLE)
        self.assertEqual(codes(judgement.reasons), {"FILE_SEVERAL_FLOCATS"})

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

    def test_the_platforms_empty_skeleton_is_editable(self):
        # The platform's own freshly-created deposit skeleton: structure, no files yet. Once the
        # verdict replaces the agent gate, a skeleton that was not editable could never receive
        # its first file - so no files is a note, not a blocker.
        judgement = build(
            header="""<mets:metsHdr><mets:agent><mets:name>University of Leeds Digital
                Library Infrastructure Project</mets:name></mets:agent></mets:metsHdr>""",
            amd_sec="""<mets:amdSec ID="ADM_objects"><mets:techMD ID="TECH_objects">
                <mets:mdWrap MDTYPE="PREMIS:OBJECT"><mets:xmlData><premis:object>
                <premis:originalName>objects</premis:originalName>
                </premis:object></mets:xmlData></mets:mdWrap></mets:techMD></mets:amdSec>""",
            file_sec="""<mets:fileSec><mets:fileGrp USE="OBJECTS"/></mets:fileSec>""",
            struct_map="""<mets:structMap TYPE="PHYSICAL">
                <mets:div ID="PHYS_ROOT" LABEL="__ROOT" TYPE="Directory">
                <mets:div ID="PHYS_objects" LABEL="objects" TYPE="Directory" ADMID="ADM_objects"/>
                </mets:div></mets:structMap>""")
        self.assertEqual(judgement.verdict, judge.EDITABLE)
        self.assertEqual(codes(judgement.notes), {"NO_FILES"})

    def test_one_file_referenced_from_two_divs_is_not_editable(self):
        # The parser makes one entry per physical fptr, then indexes them by path - so the
        # second reference to one file throws. It is not a duplicate PATH (one file, one path),
        # but the platform cannot open the document.
        judgement = build(
            header="""<mets:metsHdr><mets:agent><mets:name>EPrints 3.3.15</mets:name>
                </mets:agent></mets:metsHdr>""",
            amd_sec="""<mets:amdSec ID="AMD_0"><mets:techMD ID="AMD_1">
                <mets:mdWrap MDTYPE="OTHER" MIMETYPE="text/xml"><premis:object>
                <premis:objectCharacteristics><premis:fixity>
                <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                <premis:messageDigest>ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad</premis:messageDigest>
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
        self.assertEqual(judgement.verdict, judge.NOT_EDITABLE)
        self.assertEqual(codes(judgement.reasons), {"FILE_REFERENCED_TWICE"})
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
                <premis:messageDigest>ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad</premis:messageDigest>
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

    def test_a_labelless_directory_div_is_not_editable(self):
        # The parser throws on a Directory div without a LABEL, so the platform cannot open
        # the document at all (and adding into a LABEL-less parent would crash it too).
        judgement = build(
            header="""<mets:metsHdr><mets:agent><mets:name>University of Leeds Digital
                Library Infrastructure Project</mets:name></mets:agent></mets:metsHdr>""",
            amd_sec="""<mets:amdSec ID="ADM_1"><mets:techMD ID="TECH_1">
                <mets:mdWrap MDTYPE="PREMIS:OBJECT"><mets:xmlData><premis:object>
                <premis:objectCharacteristics><premis:fixity>
                <premis:messageDigestAlgorithm>SHA256</premis:messageDigestAlgorithm>
                <premis:messageDigest>ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad</premis:messageDigest>
                </premis:fixity></premis:objectCharacteristics>
                <premis:originalName>objects/a.jpg</premis:originalName>
                </premis:object></mets:xmlData></mets:mdWrap></mets:techMD></mets:amdSec>""",
            file_sec="""<mets:fileSec><mets:fileGrp USE="OBJECTS">
                <mets:file ID="FILE_1" ADMID="ADM_1">
                <mets:FLocat xlink:href="objects/a.jpg"/></mets:file>
                </mets:fileGrp></mets:fileSec>""",
            struct_map="""<mets:structMap TYPE="PHYSICAL">
                <mets:div ID="PHYS_ROOT" LABEL="__ROOT" TYPE="Directory">
                <mets:div ID="PHYS_objects" TYPE="Directory" ADMID="ADM_1">
                <mets:div ID="PHYS_1" LABEL="a.jpg" TYPE="Item">
                <mets:fptr FILEID="FILE_1"/></mets:div>
                </mets:div></mets:div></mets:structMap>""")
        self.assertEqual(judgement.verdict, judge.NOT_EDITABLE)
        self.assertEqual(codes(judgement.reasons), {"DIRECTORY_NO_LABEL"})

    def test_an_unnormalised_href_is_a_blocker(self):
        # The platform's path cache does not collapse empty segments, so normalising here
        # would make the judge more tolerant than the platform.
        for href in ("objects//a.jpg", "objects/./a.jpg", "objects/a.jpg/"):
            with self.subTest(href=href):
                judgement = eprints_like(href=href)
                self.assertEqual(judgement.verdict, judge.NOT_EDITABLE)
                self.assertIn("HREF_NOT_NORMALISED", codes(judgement.reasons))

    def test_a_shared_editable_dmdsec_demotes(self):
        # Editing metadata on one div rewrites the shared section in place, silently changing
        # the other div's metadata.
        judgement = eprints_like(
            root_attrs='DMDID="DMD_1"', div_id='DMDID="DMD_1" ID="PHYS_1"',
            extra="""<mets:dmdSec ID="DMD_1"><mets:mdWrap MDTYPE="MODS"><mets:xmlData>
                <mods:mods xmlns:mods="http://www.loc.gov/mods/v3">
                <mods:titleInfo><mods:title>Shared</mods:title></mods:titleInfo>
                </mods:mods></mets:xmlData></mets:mdWrap></mets:dmdSec>""")
        self.assertEqual(judgement.verdict, judge.NAVIGABLE_READ_ONLY)
        self.assertIn("SHARED_DMDSEC", codes(judgement.reasons))

    def test_a_shared_foreign_dmdsec_is_fine(self):
        # The platform never edits a foreign dmdSec (it appends alongside), so sharing one
        # is safe - only a shared MODS-editable section demotes.
        judgement = eprints_like(
            root_attrs='DMDID="DMD_eprint_1"', div_id='DMDID="DMD_eprint_1" ID="PHYS_1"',
            extra="""<mets:dmdSec ID="DMD_eprint_1"><mets:mdWrap MDTYPE="MODS">
                <mets:xmlData><mets:recordInfo>
                <mets:recordIdentifier source="EPrints">1</mets:recordIdentifier>
                </mets:recordInfo></mets:xmlData></mets:mdWrap></mets:dmdSec>""")
        self.assertEqual(judgement.verdict, judge.EDITABLE_WITH_NORMALISATION)
        self.assertNotIn("SHARED_DMDSEC", codes(judgement.reasons))

    def test_admid_on_a_filegrp_is_noted(self):
        # The platform's deletion reasoning does not consult these.
        judgement = eprints_like(
            use='reference" ADMID="AMD_0')  # smuggle the attr onto the fileGrp
        self.assertEqual(judgement.verdict, judge.EDITABLE_WITH_NORMALISATION)
        self.assertIn("ADMID_OUTSIDE_SURVIVAL_INDEX", codes(judgement.notes))

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

    def test_the_compiled_rules_are_the_schematron_source_compiled(self):
        # Both judges run schematron/compiled/*.xsl, and the .sch files are the source people
        # edit. Recompile in memory: every committed file must be what the source compiles to
        # (line endings aside - a Windows checkout has CRLF), and nothing else may be there.
        # CI checks the same on the tree.
        expected = compile_schematron.compiled_rules()
        committed = {path.name: path.read_bytes().replace(b"\r\n", b"\n")
                     for path in schematron.COMPILED_DIR.glob("*.xsl")}
        self.assertEqual(sorted(committed), sorted(expected))
        for name, content in expected.items():
            with self.subTest(rules=name):
                self.assertEqual(committed[name], content)

    def test_ncname_agrees_with_the_platform_on_the_known_edge_cases(self):
        # The fourth/fifth edition gap: XmlConvert (the platform) rejects these letters.
        for char in ("Ĳ", "ĳ", "ſ"):
            self.assertFalse(ncname.is_valid_id(f"a{char}"), repr(char))
        self.assertTrue(ncname.is_valid_id("eprint_10315_370441"))
        self.assertFalse(ncname.is_valid_id("PHYS_objects/a.tif"))
        self.assertFalse(ncname.is_valid_id("FILE a b"))
        self.assertFalse(ncname.is_valid_id(""))


class TheGoldenJudgements(unittest.TestCase):
    """
    fixtures/judgements.json: the expected judgement of one curated document per rule and
    parity edge. The .NET suite asserts the same file, so the two judges are tested to agree.
    """

    def test_every_fixture_has_a_golden_judgement(self):
        golden = {entry["document"] for entry in _golden()}
        self.assertEqual(golden, {path.name for path in FIXTURES.glob("*.xml")})

    def test_every_fixture_is_judged_as_the_golden_file_says(self):
        for expected in _golden():
            with self.subTest(document=expected["document"]):
                judgement = judge.judge_file(FIXTURES / expected["document"])
                self.assertEqual(_as_golden(judgement), {
                    key: expected[key] for key in
                    ("verdict", "fileCount", "reasons", "assumptions", "notes", "mutations")})


def _golden():
    return json.loads((FIXTURES / "judgements.json").read_text(encoding="utf-8"))["judgements"]


def _as_golden(judgement):
    def listed(findings):
        return [f.code if f.count == 1 else f"{f.code} x{f.count}" for f in findings]
    return {"verdict": judgement.verdict, "fileCount": judgement.file_count,
            "reasons": listed(judgement.reasons), "assumptions": listed(judgement.assumptions),
            "notes": listed(judgement.notes), "mutations": judgement.mutations}


class TheRobustness(unittest.TestCase):
    """Whatever it is given, the judge returns a judgement - and the same one every time."""

    def test_judging_from_many_threads_gives_the_single_threaded_answers(self):
        documents = [FIXTURES / name for name in (
            "platform-minimal.xml", "eprints-minimal.xml", "logical-root-empty-id.xml",
            "platform-digest-not-hex.xml", "two-root-divs.xml")]
        expected = {path: judge.judge_file(path).as_dict() for path in documents}
        wrong = []

        def work():
            for _ in range(40):
                for path in documents:
                    if judge.judge_file(path).as_dict() != expected[path]:
                        wrong.append(path.name)

        threads = [threading.Thread(target=work) for _ in range(8)]
        for thread in threads:
            thread.start()
        for thread in threads:
            thread.join()
        self.assertEqual(wrong, [])

    def test_a_very_deep_document_is_refused_not_walked(self):
        with tempfile.TemporaryDirectory() as folder:
            path = pathlib.Path(folder) / "deep.xml"
            path.write_text('<mets:mets xmlns:mets="http://www.loc.gov/METS/">'
                            + "<mets:div>" * 20000 + "</mets:div>" * 20000 + "</mets:mets>")
            judgement = judge.judge_file(path)
        self.assertEqual(judgement.verdict, judge.NOT_EDITABLE)
        self.assertEqual(codes(judgement.reasons), {"TOO_DEEP"})

    def test_a_url_is_not_fetched(self):
        judgement = judge.judge_file("http://127.0.0.1:9/mets.xml")
        self.assertEqual(judgement.verdict, judge.NOT_EDITABLE)
        self.assertEqual(codes(judgement.reasons), {"PARSE_FAILED"})

    def test_a_folder_is_a_judgement_not_a_crash(self):
        judgement = judge.judge_file(FIXTURES)
        self.assertEqual(judgement.verdict, judge.NOT_EDITABLE)
        self.assertEqual(codes(judgement.reasons), {"PARSE_FAILED"})


if __name__ == "__main__":
    unittest.main()
