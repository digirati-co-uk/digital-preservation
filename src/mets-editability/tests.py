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
            "append the platform agent to metsHdr",
        ])

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
                 admid='ADMID="AMD_1"', algorithm="SHA256", fptr_id=None):
    """The smallest document that reaches the EPrints tier, with one knob per test."""
    return build(
        header="""<mets:metsHdr><mets:agent ROLE="CREATOR" TYPE="OTHER" OTHERTYPE="SOFTWARE">
            <mets:name>EPrints 3.3.15</mets:name></mets:agent></mets:metsHdr>""",
        amd_sec=f"""<mets:amdSec ID="AMD_0"><mets:techMD ID="AMD_1">
            <mets:mdWrap MDTYPE="OTHER" MIMETYPE="text/xml"><premis:object>
            <premis:objectCharacteristics><premis:fixity>
            <premis:messageDigestAlgorithm>{algorithm}</premis:messageDigestAlgorithm>
            <premis:messageDigest>abc</premis:messageDigest>
            </premis:fixity></premis:objectCharacteristics>
            </premis:object></mets:mdWrap></mets:techMD></mets:amdSec>""",
        file_sec=f"""<mets:fileSec><mets:fileGrp USE="{use}">
            <mets:file ID="{file_id}" {admid}>
            <mets:FLocat LOCTYPE="URL" xlink:type="simple" xlink:href="{href}"/>
            </mets:file></mets:fileGrp></mets:fileSec>""",
        struct_map=f"""<mets:structMap><mets:div>
            <mets:div><mets:fptr FILEID="{fptr_id or file_id}"/></mets:div>
            </mets:div></mets:structMap>""")


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
        for name in ("eprints-tier", "platform-tier"):
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
