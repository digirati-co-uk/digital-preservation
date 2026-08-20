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
from unittest import mock

import requests

from lxml import etree

from app import api, ids, settings, survey
from app.ledger import CANDIDATE, DEPLOYMENT, FOREIGN, SKIPPED, Ledger, WrongDeployment

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


def _response(status_code: int, text: str = "") -> mock.Mock:
    return mock.Mock(spec=requests.Response, status_code=status_code, text=text,
                     ok=200 <= status_code < 300)


@mock.patch.object(settings, "HTTP_RETRY_PAUSE_SECONDS", 0)
@mock.patch.object(settings, "HTTP_RETRIES", 2)
@mock.patch.object(settings, "DISABLE_AUTH", True)
class RequestRetryTests(unittest.TestCase):
    """
    What one failed HTTP request turns into. A survey of thousands of Archival Groups will meet a
    timeout eventually, and what matters is that the failure says what was being attempted (so it
    can be investigated), that a safe request is retried, and that an unsafe one never is.
    """

    def test_a_get_that_times_out_is_retried_and_can_then_succeed(self):
        happy = _response(200)
        with mock.patch.object(requests, "request",
                               side_effect=[requests.ReadTimeout("read timed out"), happy]) as sent:
            result = api._request("GET", "/deposits", "Could not list deposits")
        self.assertIs(happy, result)
        self.assertEqual(2, sent.call_count)

    def test_a_get_that_keeps_failing_reports_what_it_was_doing_and_where(self):
        with mock.patch.object(requests, "request",
                               side_effect=requests.ReadTimeout("read timed out")) as sent:
            with self.assertRaises(api.ApiError) as failed:
                api._request("GET", "/repository/cc/thing", "Could not read METS for cc/thing")
        self.assertEqual(3, sent.call_count, "one try plus HTTP_RETRIES")
        message = str(failed.exception)
        self.assertIn("Could not read METS for cc/thing", message)
        self.assertIn("ReadTimeout", message)
        self.assertIn("GET", message)
        self.assertIn("/repository/cc/thing", message)
        self.assertIsNone(failed.exception.status_code, "there was no response to have a status")

    def test_a_gateway_error_on_a_get_is_retried(self):
        with mock.patch.object(requests, "request",
                               side_effect=[_response(503), _response(200)]) as sent:
            api._request("GET", "/deposits", "Could not list deposits")
        self.assertEqual(2, sent.call_count)

    def test_a_post_is_never_retried(self):
        # A timed-out POST may have been processed anyway; repeating it could preserve a version
        # twice. One attempt, and a report that says everything needed to investigate first.
        with mock.patch.object(requests, "request",
                               side_effect=requests.ConnectionError("connection lost")) as sent:
            with self.assertRaises(api.ApiError):
                api._request("POST", "/deposits", "Could not create deposit")
        self.assertEqual(1, sent.call_count)

    def test_a_refusal_carries_the_status_and_the_body(self):
        with mock.patch.object(requests, "request", return_value=_response(404, "no such thing")):
            with self.assertRaises(api.ApiError) as failed:
                api._request("GET", "/repository/cc/gone", "Could not read METS for cc/gone")
        self.assertEqual(404, failed.exception.status_code)
        self.assertIn("no such thing", str(failed.exception))


class SurveyReadFailureTests(unittest.TestCase):
    """
    A failed read is not a verdict. The ledger skips paths it already knows, so recording a
    timeout would make the transient permanent - and three failures in a row means the platform
    is down, at which point carrying on is a retry-and-timeout per group for nothing.
    """

    def setUp(self):
        self.directory = tempfile.mkdtemp()
        self.ledger = Ledger(os.path.join(self.directory, "ledger.sqlite"), "https://dev.example")

    def tearDown(self):
        self.ledger.close()
        shutil.rmtree(self.directory, ignore_errors=True)

    def test_a_timeout_records_nothing_so_a_rerun_tries_again(self):
        with mock.patch.object(api, "get_archival_group_mets",
                               side_effect=api.ApiError("Could not read METS", "ReadTimeout")):
            survey.survey(self.ledger, paths=["cc/timed-out"])
        self.assertEqual(set(), self.ledger.known_paths())

    def test_a_404_is_a_verdict_and_is_recorded(self):
        error = api.ApiError("Could not read METS", "HTTP 404", status_code=404)
        with mock.patch.object(api, "get_archival_group_mets", side_effect=error):
            survey.survey(self.ledger, paths=["cc/no-mets"])
        self.assertEqual({"cc/no-mets"}, self.ledger.known_paths())

    def test_a_run_of_failures_stops_the_survey(self):
        paths = [f"cc/down-{n}" for n in range(10)]
        with mock.patch.object(api, "get_archival_group_mets",
                               side_effect=api.ApiError("Could not read METS", "down")) as read:
            survey.survey(self.ledger, paths=paths)
        self.assertEqual(survey._UNREAD_RUN_LIMIT, read.call_count,
                         "the survey should stop at the limit, not fail through all ten")
        self.assertEqual(set(), self.ledger.known_paths())


class DepositWalkScopeTests(unittest.TestCase):
    def test_the_walk_asks_for_every_deposit_not_only_archived_ones(self):
        # Archived=true is a FILTER (only archived), not an include. Passing it - as this tool
        # originally did - silently hid every Archival Group whose deposits had not been tidied
        # away yet. The query must name no Archived term at all.
        with mock.patch.object(api, "_request", return_value=mock.Mock(json=lambda: {})) as sent:
            api.list_deposits(1, 100)
        params = sent.call_args.kwargs["params"]
        self.assertNotIn("Archived", params)
        self.assertEqual("true", params["ShowAll"])

    def test_the_resume_cursor_is_passed_server_side(self):
        with mock.patch.object(api, "_request", return_value=mock.Mock(json=lambda: {})) as sent:
            api.list_deposits(1, 100, created_after="2026-02-17T10:29:12.130231Z")
        self.assertEqual("2026-02-17T10:29:12.130231Z",
                         sent.call_args.kwargs["params"]["CreatedAfter"])


class SurveyLedgerTestCase(unittest.TestCase):
    """Shared scaffolding: a real (temporary) ledger and a stubbed deposits walk."""

    def setUp(self):
        self.directory = tempfile.mkdtemp()
        self.ledger = Ledger(os.path.join(self.directory, "ledger.sqlite"), "https://dev.example")

    def tearDown(self):
        self.ledger.close()
        shutil.rmtree(self.directory, ignore_errors=True)

    @staticmethod
    def _recording(state=FOREIGN):
        """A stand-in for _survey_one that records a verdict, as the real one always does."""
        def survey_one(ledger, path):
            ledger.record(path, state, agent="test")
            return True
        return survey_one


class DepositorSkipTests(SurveyLedgerTestCase):
    EPRINTS = "https://preservation.library.leeds.ac.uk/agents/eprints-migration-app"

    def test_a_skipped_depositors_group_is_recorded_without_reading_its_mets(self):
        rows = [("cc/eprints-thing", "2026-01-01T00:00:00Z", "eprints-migration-app",
                 "2026-01-01T01:00:00Z")]
        with mock.patch.object(survey, "deposit_rows", return_value=iter(rows)), \
             mock.patch.object(api, "get_archival_group_mets") as read:
            survey.survey(self.ledger, skip_creators=[self.EPRINTS])
        read.assert_not_called()
        row = self.ledger.get("cc/eprints-thing")
        self.assertEqual(SKIPPED, row["state"])
        self.assertEqual("2026-01-01T01:00:00Z", row["preserved"],
                         "the preserved date is free knowledge even for a skipped group")

    def test_the_skip_matches_the_identity_however_it_is_spelt(self):
        # The same agent's URI base differs between environments; a bare id, the dev URI and the
        # prod URI must all mean the same thing.
        for spelling in ("eprints-migration-app", self.EPRINTS,
                         "https://preservation-api-dev.library.leeds.ac.uk/agents/eprints-migration-app"):
            self.assertEqual(frozenset({"eprints-migration-app"}), survey._skip_slugs([spelling]))

    def test_a_deposit_by_anyone_else_upgrades_a_skipped_group_to_a_real_verdict(self):
        rows = [("cc/mixed", "2026-01-01T00:00:00Z", "eprints-migration-app", "2026-01-01T01:00:00Z"),
                ("cc/mixed", "2026-01-02T00:00:00Z", "some-human", "2026-01-02T01:00:00Z")]
        with mock.patch.object(survey, "deposit_rows", return_value=iter(rows)), \
             mock.patch.object(survey, "_survey_one", side_effect=self._recording()) as surveyed:
            survey.survey(self.ledger, skip_creators=["eprints-migration-app"])
        surveyed.assert_called_once_with(self.ledger, "cc/mixed")
        self.assertEqual(FOREIGN, self.ledger.get("cc/mixed")["state"])

    def test_nothing_is_skipped_unless_asked(self):
        rows = [("cc/eprints-thing", "2026-01-01T00:00:00Z", "eprints-migration-app", None)]
        with mock.patch.object(survey, "deposit_rows", return_value=iter(rows)), \
             mock.patch.object(survey, "_survey_one", side_effect=self._recording()) as surveyed:
            survey.survey(self.ledger, skip_creators=[])
        surveyed.assert_called_once()


class PreservedDateTests(SurveyLedgerTestCase):
    def test_the_latest_preserved_date_across_deposits_wins(self):
        rows = [("cc/thing", "2026-01-01T00:00:00Z", "human", "2026-01-01T01:00:00Z"),
                ("cc/thing", "2026-01-02T00:00:00Z", "human", "2026-03-05T12:00:00Z"),
                ("cc/thing", "2026-01-03T00:00:00Z", "human", None)]  # never preserved: no regress
        with mock.patch.object(survey, "deposit_rows", return_value=iter(rows)), \
             mock.patch.object(survey, "_survey_one", side_effect=self._recording()):
            survey.survey(self.ledger)
        self.assertEqual("2026-03-05T12:00:00Z", self.ledger.get("cc/thing")["preserved"])

    def test_variable_length_fractions_compare_by_time_not_by_string(self):
        # '...12.13Z' vs '...12.130231Z': as strings the shorter sorts LATER ('Z' beats any
        # digit), which would let an earlier date overwrite a later one.
        self.ledger.record("cc/thing", FOREIGN, agent="test")
        self.ledger.note_preserved("cc/thing", "2026-01-01T00:00:12.130231Z")
        self.ledger.note_preserved("cc/thing", "2026-01-01T00:00:12.13Z")
        self.assertEqual("2026-01-01T00:00:12.130231Z", self.ledger.get("cc/thing")["preserved"])


class ResumeCursorTests(SurveyLedgerTestCase):
    ROWS = [("cc/one", "2026-01-01T00:00:00Z", "human", None),
            ("cc/two", "2026-01-02T00:00:00Z", "human", None),
            ("cc/three", "2026-01-03T00:00:00Z", "human", None)]

    def test_a_full_walk_leaves_the_cursor_at_the_last_deposit_dealt_with(self):
        with mock.patch.object(survey, "deposit_rows", return_value=iter(self.ROWS)), \
             mock.patch.object(survey, "_survey_one", side_effect=self._recording()):
            survey.survey(self.ledger)
        self.assertEqual("2026-01-03T00:00:00Z", self.ledger.get_meta(survey._CURSOR))

    def test_the_next_full_walk_resumes_from_the_cursor(self):
        self.ledger.set_meta(survey._CURSOR, "2026-01-03T00:00:00Z")
        with mock.patch.object(survey, "deposit_rows", return_value=iter([])) as walk:
            survey.survey(self.ledger)
        walk.assert_called_once_with(False, "2026-01-03T00:00:00Z")

    def test_the_cursor_never_passes_an_unrecorded_group(self):
        outcomes = {"cc/one": True, "cc/two": False, "cc/three": True}

        def survey_one(ledger, path):
            if outcomes[path]:
                ledger.record(path, FOREIGN, agent="test")
            return outcomes[path]

        with mock.patch.object(survey, "deposit_rows", return_value=iter(self.ROWS)), \
             mock.patch.object(survey, "_survey_one", side_effect=survey_one):
            survey.survey(self.ledger)
        self.assertEqual("2026-01-01T00:00:00Z", self.ledger.get_meta(survey._CURSOR),
                         "cc/two was not recorded, so the cursor must stop before it")

    def test_narrowed_and_unordered_runs_leave_the_cursor_alone(self):
        with mock.patch.object(survey, "deposit_rows", return_value=iter(self.ROWS)), \
             mock.patch.object(survey, "_survey_one", side_effect=self._recording()):
            survey.survey(self.ledger, path_prefix="cc/")
            survey.survey(self.ledger, newest_first=True, rescan=True)
            survey.survey(self.ledger, paths=["cc/four"])
        self.assertIsNone(self.ledger.get_meta(survey._CURSOR))


class MigrateSafetyTests(SurveyLedgerTestCase):
    """
    The behaviours around a migration that does not go to plan. Each of these was a review
    finding: the failure mode is silent, and a campaign cannot afford silence.
    """

    def setUp(self):
        super().setUp()
        self.ledger.record("cc/thing", CANDIDATE, invalid_id_count=1)
        self.deposit = {"id": "https://dev.example/deposits/dep-1", "metsETag": "etag-1"}

    def test_a_timeout_during_preserve_keeps_the_deposit(self):
        # The import job may still be running - or may complete after we stop watching. Deleting
        # the deposit could break it mid-copy, and destroys the evidence either way.
        from app import migrate
        with mock.patch.object(api, "create_deposit", return_value=self.deposit), \
             mock.patch.object(api, "get_archival_group_mets",
                               return_value=b"<mets xmlns='http://www.loc.gov/METS/'/>"), \
             mock.patch.object(api, "normalise_mets_ids",
                               return_value={"changed": True, "idsRewritten": 1,
                                             "referencesRewritten": 0}), \
             mock.patch.object(api, "get_diff_import_job", return_value={
                 "binariesToPatch": [{"id": "https://x/mets.xml"}]}), \
             mock.patch.object(api, "execute_import_job", return_value={"id": "https://x/r/1"}), \
             mock.patch.object(api, "await_import_job", side_effect=TimeoutError("600s")), \
             mock.patch.object(api, "delete_deposit") as deleted:
            with self.assertRaises(TimeoutError):
                migrate.migrate_one(self.ledger, "cc/thing", dry_run=False)
        deleted.assert_not_called()

    def test_a_refusal_before_preserve_still_tidies_the_deposit(self):
        from app import migrate
        with mock.patch.object(api, "create_deposit", return_value=self.deposit), \
             mock.patch.object(api, "get_archival_group_mets",
                               return_value=b"<mets xmlns='http://www.loc.gov/METS/'/>"), \
             mock.patch.object(api, "normalise_mets_ids",
                               return_value={"changed": True, "idsRewritten": 1,
                                             "referencesRewritten": 0}), \
             mock.patch.object(api, "get_diff_import_job", return_value={"binariesToPatch": []}), \
             mock.patch.object(api, "delete_deposit") as deleted:
            with self.assertRaises(migrate.MigrationRefused):
                migrate.migrate_one(self.ledger, "cc/thing", dry_run=False)
        deleted.assert_called_once()

    def test_a_declined_rewrite_is_not_settled_as_no_change(self):
        # changed=false WITH warnings means the platform declined to fix a document the survey
        # says is invalid. Settling that as no-change is the failure that looks like success.
        from app import migrate
        with mock.patch.object(api, "create_deposit", return_value=self.deposit), \
             mock.patch.object(api, "get_archival_group_mets",
                               return_value=b"<mets xmlns='http://www.loc.gov/METS/'/>"), \
             mock.patch.object(api, "normalise_mets_ids",
                               return_value={"changed": False,
                                             "warnings": ["ID 'x' was not rewritten"]}), \
             mock.patch.object(api, "delete_deposit"):
            with self.assertRaises(migrate.MigrationRefused) as refused:
                migrate.migrate_one(self.ledger, "cc/thing", dry_run=False)
        self.assertIn("declined", str(refused.exception))

    def test_verify_leaves_a_done_row_alone_when_the_read_fails(self):
        # The same rule as the survey's: a failed read is not a verdict. Overwriting DONE would
        # permanently remove a correctly migrated group from the verify queue.
        from app import migrate
        from app.ledger import DONE
        self.ledger.record("cc/migrated", DONE, from_version="v1", to_version="v2")
        with mock.patch.object(api, "get_archival_group_mets",
                               side_effect=api.ApiError("Could not read METS", "ReadTimeout")):
            migrate.verify_migrated(self.ledger)
        row = self.ledger.get("cc/migrated")
        self.assertEqual(DONE, row["state"])
        self.assertEqual("v2", row["to_version"], "the migration evidence survives the blip")


@mock.patch.object(settings, "DISABLE_AUTH", True)
class IfMatchTests(unittest.TestCase):
    def test_normalise_sends_the_deposits_mets_etag_as_if_match(self):
        response = mock.Mock(ok=True, status_code=200)
        response.json.return_value = {"changed": False}
        with mock.patch.object(requests, "request", return_value=response) as sent:
            api.normalise_mets_ids("dep-1", "etag-abc")
        self.assertEqual("etag-abc", sent.call_args.kwargs["headers"]["If-Match"])

    def test_no_etag_sends_no_if_match(self):
        response = mock.Mock(ok=True, status_code=200)
        response.json.return_value = {"changed": False}
        with mock.patch.object(requests, "request", return_value=response) as sent:
            api.normalise_mets_ids("dep-1", None)
        self.assertNotIn("If-Match", sent.call_args.kwargs["headers"])


class VerifySkippedTests(SurveyLedgerTestCase):
    def test_sampled_rows_gain_their_true_verdict(self):
        for n in range(5):
            self.ledger.record(f"cc/skipped-{n}", SKIPPED, note="test")
        with mock.patch.object(survey, "_survey_one", side_effect=self._recording()) as surveyed:
            survey.verify_skipped(self.ledger, sample_size=3)
        self.assertEqual(3, surveyed.call_count)
        states = [row["state"] for row in self.ledger.in_state(FOREIGN)]
        self.assertEqual(3, len(states))
        self.assertEqual(2, len(self.ledger.in_state(SKIPPED)))


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
