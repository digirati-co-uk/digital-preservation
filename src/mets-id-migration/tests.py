"""
Tests for the decision this tool makes: does this METS need migrating?

    python -m unittest tests -v

Stdlib unittest, so there is nothing to install beyond the tool's own requirements. What is checked
here is only the predicate - the rewriting itself is the platform's, and is tested in
XmlGen.Tests/MetsIdNormalisationTests.cs against the same sample documents.
"""

import glob
import os
import shutil
import sqlite3
import tempfile
import unittest
from contextlib import closing

from lxml import etree

from app import ids
from app.ledger import CANDIDATE, DEPLOYMENT, Ledger, WrongDeployment

SAMPLES = os.path.join(os.path.dirname(__file__), "..", "DigitalPreservation", "XmlGen.Tests", "Samples")


def mets(body: str) -> bytes:
    return f"""<?xml version="1.0" encoding="utf-8"?>
<mets:mets xmlns:mets="http://www.loc.gov/METS/" xmlns:xlink="http://www.w3.org/1999/xlink">
  <mets:metsHdr><mets:agent ROLE="CREATOR" TYPE="OTHER">
    <mets:name>{ids.METS_CREATOR_AGENT}</mets:name>
  </mets:agent></mets:metsHdr>
  {body}
</mets:mets>""".encode("utf-8")


class NCNameTests(unittest.TestCase):
    """An xs:ID must be an XML NCName. The boundary matters in both directions."""

    def test_legal(self):
        for value in ["PHYS_ROOT", "DMD_PHYS_ROOT", "LOG_0001", "a", "_x", "a.b-c",
                      "FILE_objects_x002F_my_x0020_file.pdf",
                      "ADM_objects_x002F_résumé.pdf"]:  # an accented letter is legal
            self.assertTrue(ids.is_valid_id(value), value)

    def test_illegal(self):
        for value in ["", "PHYS_objects/my file.pdf", "AT&T", "a b", "2020", "a:b", "-leading"]:
            self.assertFalse(ids.is_valid_id(value), value)


class XmlConvertAgreementTests(unittest.TestCase):
    """
    The survey's verdict has to be the platform's verdict. .NET's XmlConvert, which is what the
    platform enforces, implements XML 1.0 FOURTH edition name rules - enumerated letter tables with
    gaps in them - so the modern NameStartChar production is too generous. Getting this wrong in
    that direction is the dangerous one: the survey would call an Archival Group conforming and the
    campaign would skip a document that is still invalid.

    XmlGen.Tests/NCNameRangesTests.cs checks the generated ranges against XmlConvert itself. These
    check that this module actually uses them.
    """

    def test_letters_the_fifth_edition_allows_and_the_platform_does_not(self):
        for excluded in ("Ĳ", "ĳ", "ſ"):
            self.assertFalse(ids.is_valid_id(excluded + "abc"),
                             f"U+{ord(excluded):04X} is not legal to XmlConvert")

    def test_an_accented_letter_is_still_legal(self):
        self.assertTrue(ids.is_valid_id("café"))
        self.assertTrue(ids.is_valid_id("Ärger_1"))

    def test_the_ordinary_rules_still_hold(self):
        self.assertTrue(ids.is_valid_id("FILE_objects_x002F_a.pdf"))
        self.assertFalse(ids.is_valid_id("1leading-digit"))
        self.assertFalse(ids.is_valid_id("has space"))
        self.assertFalse(ids.is_valid_id(""))


class IdrefsAmbiguityTests(unittest.TestCase):
    """
    The hard case, and the reason this is not a whitespace split: a legacy ID can contain spaces,
    so one reference and several references look identical. Issue #213.
    """

    def test_a_genuine_list_of_legal_ids_is_not_flagged(self):
        document = ids.parse(mets("""
          <mets:amdSec ID="ADM_a"/>
          <mets:amdSec ID="ADM_b"/>
          <mets:structMap><mets:div ID="PHYS_ROOT" ADMID="ADM_a ADM_b"/></mets:structMap>"""))
        self.assertEqual(ids.invalid_ids(document), [])

    def test_one_legacy_id_containing_spaces_is_flagged(self):
        # The whole value names an amdSec that exists, so it is one ID, not two tokens.
        document = ids.parse(mets("""
          <mets:amdSec ID="ADM_objects/my file.pdf"/>
          <mets:structMap>
            <mets:div ID="PHYS_ROOT" ADMID="ADM_objects/my file.pdf"/>
          </mets:structMap>"""))
        self.assertIn("ADM_objects/my file.pdf", ids.invalid_ids(document))

    def test_a_spaced_id_with_no_slash_is_still_flagged(self):
        # Splitting on whitespace would leave 'ADM_my' and 'notes', both legal, and the document
        # would be missed entirely. This is the case that decides the whole approach.
        document = ids.parse(mets("""
          <mets:amdSec ID="ADM_my notes"/>
          <mets:structMap><mets:div ID="PHYS_ROOT" ADMID="ADM_my notes"/></mets:structMap>"""))
        self.assertEqual(ids.invalid_ids(document), ["ADM_my notes"])

    def test_a_dangling_reference_is_flagged_on_its_own_merits(self):
        # The template writes DMDID onto folder divs before the dmdSec exists, so this names
        # nothing - but it is still not a legal xs:ID, and the document does not conform.
        document = ids.parse(mets("""
          <mets:structMap>
            <mets:div ID="PHYS_metadata" DMDID="DMD_metadata/ad-hoc"/>
          </mets:structMap>"""))
        self.assertEqual(ids.invalid_ids(document), ["DMD_metadata/ad-hoc"])


class OtherIdSitesTests(unittest.TestCase):
    def test_smlink_ends_are_checked(self):
        document = ids.parse(mets("""
          <mets:structLink>
            <mets:smLink xlink:from="FILE_objects/a.tif" xlink:to="FILE_objects/b.tif"/>
          </mets:structLink>"""))
        self.assertEqual(len(ids.invalid_ids(document)), 2)

    def test_smlocatorlink_fragment_is_checked_without_its_hash(self):
        document = ids.parse(mets("""
          <mets:structLink><mets:smLinkGrp>
            <mets:smLocatorLink xlink:href="#FILE_objects/a.tif"/>
          </mets:smLinkGrp></mets:structLink>"""))
        self.assertEqual(ids.invalid_ids(document), ["FILE_objects/a.tif"])

    def test_content_that_looks_like_an_id_is_not_an_id(self):
        # Only ID-typed attributes are examined. A file name is content.
        document = ids.parse(mets("""
          <mets:fileSec><mets:fileGrp><mets:file ID="FILE_ok">
            <mets:FLocat xlink:href="objects/my file.pdf"/>
          </mets:file></mets:fileGrp></mets:fileSec>"""))
        self.assertEqual(ids.invalid_ids(document), [])


class OffendingCharacterTests(unittest.TestCase):
    def test_reports_the_characters_that_matter(self):
        self.assertEqual(ids.offending_characters(["A_b/c", "A_d e"]), "SPACE /")

    def test_a_leading_digit_is_reported_even_though_the_digit_is_legal_later(self):
        self.assertEqual(ids.offending_characters(["2020_files"]), "2")


class SampleCorpusTests(unittest.TestCase):
    """
    The real documents, which is where the agreement between the two criteria was measured: no
    third-party METS has an invalid ID at all, and everything the platform wrote before #214 does.
    """

    def _parse(self, name):
        with open(os.path.join(SAMPLES, name), "rb") as handle:
            return ids.parse(handle.read())

    def test_our_own_legacy_documents_are_candidates(self):
        for name in ["path-fixture-spaces.xml", "liddle.mets.xml", "response-book.mets.xml",
                     "simple-image.mets.xml", "wow.mets.xml", "mets-sample-001.xml"]:
            document = self._parse(name)
            self.assertEqual(ids.creator_agent(document), ids.METS_CREATOR_AGENT, name)
            self.assertNotEqual(ids.invalid_ids(document), [], name)

    def test_third_party_documents_are_excluded_twice_over(self):
        # Not ours by agent, and legal anyway - the two criteria agree across the whole corpus.
        for name in ["EPrints.10315.METS.xml", "goobi-2026.xml", "goobi-wc-b29356350.xml",
                     "archivematica-wc-METS.299eb16f-1e62-4bf6-b259-c82146153711.xml"]:
            document = self._parse(name)
            self.assertNotEqual(ids.creator_agent(document), ids.METS_CREATOR_AGENT, name)
            self.assertEqual(ids.invalid_ids(document), [], name)

    def test_every_sample_the_platform_wrote_is_only_broken_by_expected_characters(self):
        for path in glob.glob(os.path.join(SAMPLES, "*.xml")):
            with open(path, "rb") as handle:
                content = handle.read()
            try:
                document = ids.parse(content)
            except etree.XMLSyntaxError:
                continue
            if ids.creator_agent(document) != ids.METS_CREATOR_AGENT:
                continue
            offenders = set(ids.offending_characters(ids.invalid_ids(document)).split())
            self.assertTrue(offenders <= {"/", "SPACE", "(", ")", "&"},
                            f"{os.path.basename(path)} has unexpected offenders: {offenders}")


class LedgerDeploymentTests(unittest.TestCase):
    """
    One ledger, one deployment. Rows are keyed by Archival Group path, and development and
    production share paths - so a shared ledger would let a verdict reached on one stand for the
    other, silently, because `survey` skips paths it already knows.
    """

    DEV = "https://dev.example.ac.uk"
    PROD = "https://prod.example.ac.uk"

    def setUp(self):
        self.directory = tempfile.mkdtemp()
        self.path = os.path.join(self.directory, "ledger.sqlite")

    def tearDown(self):
        shutil.rmtree(self.directory, ignore_errors=True)

    def _open(self, deployment):
        return closing(Ledger(self.path, deployment))

    def test_a_new_ledger_takes_the_deployment_it_is_opened_against(self):
        with self._open(self.DEV) as ledger:
            self.assertEqual(self.DEV, ledger.get_meta(DEPLOYMENT))

    def test_reopening_against_the_same_deployment_is_fine(self):
        with self._open(self.DEV) as ledger:
            ledger.record("cc-test/1000001", CANDIDATE)
        with self._open(self.DEV) as ledger:
            self.assertEqual({"cc-test/1000001"}, ledger.known_paths())

    def test_a_trailing_slash_is_not_a_different_deployment(self):
        with self._open(self.DEV):
            pass
        with self._open(self.DEV + "/") as ledger:
            self.assertEqual(self.DEV, ledger.get_meta(DEPLOYMENT))

    def test_opening_against_another_deployment_is_refused(self):
        with self._open(self.DEV) as ledger:
            ledger.record("cc-test/1000001", CANDIDATE)
        with self.assertRaises(WrongDeployment) as refused:
            Ledger(self.path, self.PROD)
        self.assertIn(self.DEV, str(refused.exception))
        self.assertIn(self.PROD, str(refused.exception))

    def test_a_ledger_with_rows_but_no_stamp_is_not_adopted(self):
        # A ledger written before this check existed. Nothing in the file says where its rows came
        # from, so adopting it for whatever is configured now is exactly the mistake being guarded.
        with self._open(self.DEV) as ledger:
            ledger.record("cc-test/1000001", CANDIDATE)
        with closing(sqlite3.connect(self.path)) as connection:
            connection.execute("DELETE FROM meta")
            connection.commit()
        with self.assertRaises(WrongDeployment):
            Ledger(self.path, self.PROD)

    def test_an_empty_ledger_with_no_stamp_is_adopted(self):
        # Nothing to get wrong: no rows means no verdicts that could belong to somewhere else.
        with self._open(self.DEV):
            pass
        with closing(sqlite3.connect(self.path)) as connection:
            connection.execute("DELETE FROM meta")
            connection.commit()
        with self._open(self.PROD) as ledger:
            self.assertEqual(self.PROD, ledger.get_meta(DEPLOYMENT))


if __name__ == "__main__":
    unittest.main()
