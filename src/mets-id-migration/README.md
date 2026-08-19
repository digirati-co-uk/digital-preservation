# METS ID migration

Bulk migration of preserved METS documents to legal `xs:ID` values — issue #188 step 3.

Before release 1.3.0 the platform minted METS IDs by concatenating a prefix and the item's path, so
`objects/my file.pdf` became `PHYS_objects/my file.pdf`. That is not a legal `xs:ID`: an `xs:ID` must
be an XML NCName, which excludes both the `/` and the space. Since #214 the path part is escaped
(`PHYS_objects_x002F_my_x0020_file.pdf`), but existing documents are never rewritten, so both
generations are live. This tool rewrites the older ones.

## What decides that a document needs migrating

Not its age, and not who wrote it — **whether it contains an ID that is not a legal NCName**. That is
the actual defect, it is decidable from the document alone, and it stops being true once the
migration has run. Three consequences worth knowing:

- **The campaign converges**, and re-running is safe. A migrated document reports nothing to do.
- **An ID that is already legal is never touched**, whoever minted it. That includes client-supplied
  logical range IDs, which are public through the IIIF Range URIs built from them.
- **A pre-#214 document whose paths happened to contain no illegal characters is left alone**, rather
  than gaining a version for a rewrite that would produce the identical string.

The `mets:agent` creator name narrows the population — EPrints, Archivematica and Goobi documents
carry their own ID schemes, are legal NCNames, and are not ours to renumber — but it is the filter,
not the trigger.

## How one Archival Group is migrated

1. **Create a deposit against the Archival Group, without export.** This is why the migration is
   cheap: an export copies every binary into the deposit's S3 area, whereas a plain deposit against
   an existing group copies only the METS. The diff still comes out right, because the deposit's
   combined tree takes every other file from the METS, with the digests the group already holds.
2. **Normalise the IDs** — `POST /deposits/{id}/mets/normalise`. The rewriting happens in the
   platform's own `MetsManager`, never here, so there is one implementation of how an ID is spelt.
3. **If nothing changed, stop.** An Archival Group must not gain a version for a document that did
   not change.
4. **Generate the diff import job and refuse unless it is exactly one binary to patch — the METS.**
   This is the gate that matters. Because a METS-only deposit gets its file list from the METS, a
   file the Archival Group holds but the METS does not mention would be listed for *deletion* rather
   than failing the diff. The assertion closes that.
5. **Execute it**, with `suppressActivityStreamEvent` set.
6. **Verify**: re-read the preserved METS and check the set of paths and digests is byte-identical
   to what it was before. A rename of identifiers cannot change it; anything worse would.

The result is one new OCFL version whose only new content is `mets.xml`.

## Why the Activity Stream event is suppressed

The stream is a IIIF Change Discovery feed. Its readers — `iiif-builder` among them — treat an entry
as "this object changed, rebuild what you derived from it". Renaming identifiers inside a METS gives
them nothing to rebuild, so an entry would be both wasted work and a misleading account of the
object's history.

It suppresses the *entry*, not the *record*: Preservation API still writes the event row, and OCFL
still holds the new version with everything that made it. Nothing is lost — it just is not announced
as a change. (The row is written for a practical reason too: the stream reader takes its watermark
from the latest event date, so skipping rows entirely would leave it re-reading the same window for
ever, which is exactly what a bulk migration would cause.)

## What needs deploying, and where to point it

**`survey`, `list`, `report` and `verify` need nothing deployed.** They call only endpoints that have
been there all along:

| call | endpoint |
|---|---|
| discovery | `GET /deposits?ShowAll&Archived&OrderBy=Created` |
| the decision | `GET /repository/{path}?view=mets` |
| cross-check | `GET /activity/archivalgroups/pages/{n}` |

So you can size the job against development *or production* today, before any of this ships.

**`migrate` needs this branch deployed**, with `FeatureFlags:EnableMetsIdNormalisation` set. Two
things go wrong without it, and they fail differently:

- `POST /deposits/{id}/mets/normalise` does not exist, so the tool gets a 404, records the Archival
  Group as `failed`, and moves on. Noisy, but harmless — it deletes the deposit it made.
- `suppressActivityStreamEvent` on the import job is an unknown property to an older API, and
  `System.Text.Json` ignores unknown properties. So a migration would run and **publish an Activity
  Stream event anyway**, silently. That is the more dangerous half, and the reason not to point
  `migrate` at an environment you have not checked.

To get it onto development, add the `deploy` label to the PR — but note `build-push-images` needs
`test-dotnet`, which is skipped while the PR is a draft, so mark it ready for review first or the
label does nothing.

### If your local stack shares development's Fedora

A local Preservation API and Storage API have their **own** Postgres (5433, 5434) but may talk to the
**shared** Fedora and S3. That splits the tool's two halves in a way worth knowing:

- **Pointing `PRESERVATION_API` at `localhost` makes `survey` nearly empty.** The deposits query
  reads Preservation API's own database, which locally is yours and holds only your deposits — even
  though the Fedora behind it is full of Archival Groups. Point it at the deployed development API to
  survey development.
- **`--path` is the exception, and the useful one.** It skips the deposits query entirely and reads
  `/repository/{path}?view=mets`, which goes through your local Storage API to the shared Fedora. So
  `survey --path cc/something` against `localhost` sees real content, and is the natural way to try
  one document locally.
- **`migrate` against a local stack is not sandboxed.** Executing the import job writes a real new
  OCFL version into the shared repository, through your local Storage API. The same is true of the
  UI action followed by Preserve.
- **`migrate --dry-run` cannot create a version.** It stops before executing the import job. It does
  create and then delete a deposit, which touches your local database and the deposit area in S3, and
  it reads the Archival Group's METS — but nothing preserved changes.
- **`NormaliseMetsIdsOnWrite` is set in the tracked `appsettings.Development.json` examples.** If it
  is on locally, any edit you preserve against a pre-#214 Archival Group migrates its whole METS into
  the shared repository as a side effect. Intended behaviour, but turn it off locally until you want
  it.

## Running it

```bash
pip install -r requirements.txt
cp .env.example .env      # and fill it in
```

**Start by finding out how big the job is. That costs nothing and changes nothing:**

```bash
python mets_id_migration.py survey --check-completeness   # read-only
python mets_id_migration.py report                        # counts by state
python mets_id_migration.py list                          # the actual list
python mets_id_migration.py list --csv candidates.csv
```

`survey` reads the deposits query and each Archival Group's METS and writes only to the local
ledger. `list` prints what it found. If that list is short — and on production it is expected to be,
since almost everything there is EPrints material that was never affected — **stop here and do them
by hand**; see below. The bulk migration is for when it isn't.

### Surveying part of a deployment

A full survey of development means walking every deposit it has ever had, which is not what you want
when trying this out. Three ways to look at less, all of which still write only to the ledger:

```bash
# a handful of the most recently deposited Archival Groups - on dev, whatever the nightly
# Playwright run just made
python mets_id_migration.py survey --limit 5 --newest-first

# everything under one container
python mets_id_migration.py survey --path-prefix cc-test/ --limit 20

# exactly these, skipping the deposits query altogether
python mets_id_migration.py survey --path cc/pdc7mlqc --path cc-test/1000001
```

`--limit` stops the paging as well as the examining, because the walk is lazy: `--limit 5` fetches
one page of deposits and five METS documents, not the whole deposit list. `--newest-first` gives up
the stable-paging guarantee that ascending order provides, so it is for sampling, not for a campaign
that has to see everything.

The ledger accumulates, so a narrow survey can be widened later — rerunning without `--limit` picks
up everything not already recorded, and `--rescan` re-examines what is. `list`, `migrate` and their
`--path-prefix` work the same way, so a partial survey leads naturally to a partial migration.

```bash
python mets_id_migration.py migrate --dry-run             # rehearse; nothing is preserved
python mets_id_migration.py migrate --limit 1             # then one
python mets_id_migration.py migrate                       # then the rest
python mets_id_migration.py verify                        # read-only
```

`--dry-run` goes all the way to generating the diff and checking the gate, then throws the deposit
away without preserving. It is the rehearsal that proves, per Archival Group, that the change really
is a single METS patch.

There is deliberately no command that does the whole thing unattended. Read `report` between steps.

## Doing it by hand

Set `FeatureFlags:ShowNormaliseMetsIds` on the UI and `FeatureFlags:EnableMetsIdNormalisation` on
Preservation API. Then, for each Archival Group on the list:

1. Browse to the Archival Group and create a deposit for it — **not** an export. Only the METS comes
   down, which is what makes this quick even for a large object.
2. On the deposit, **Actions → Normalise METS IDs**. It says how many IDs and references it
   rewrote, or that the document already conforms. If it already conforms, delete the deposit and
   move on: there is nothing here to preserve.
3. Generate the import job. **Check it is a single binary to patch, and that it is the METS file.**
   The tool refuses automatically at this point; by hand, this is the check to make yourself.
4. Tick **"Maintenance only — keep this version out of the Activity Stream"**, then Preserve.
5. Delete the deposit.

That checkbox appears next to Preserve under the same feature flag, so a migration done by hand
leaves the same clean history as one done in bulk.

## Migrating by attrition

`FeatureFlags:NormaliseMetsIdsOnWrite` (on Preservation API, Pipeline API, the UI and the Deposit
Archiver) makes every METS write migrate the document it is writing. It exists because an edit to a
pre-#214 document otherwise makes it *worse* — the new entries get encoded IDs while the old ones
keep their raw form, so editing is the only thing that creates mixed-generation documents.

With it on, anything anyone edits fixes itself, and this tool only has to deal with what nobody
touches. **Turn it on after a campaign has been through the same documents**, not before: the first
real runs of the normaliser should happen in a controlled batch rather than while somebody waits for
a page to save.

It does not replace this tool. Most preserved Archival Groups are never edited again.

**Rehearse on development first.** It holds far more affected Archival Groups than production does —
the accumulated residue of testing and the nightly Playwright runs — which makes it both the larger
corpus and the expendable one.

## The ledger

One SQLite file (`ledger.sqlite` by default) with a row per Archival Group. It makes the campaign
resumable, stops anything being migrated twice, and is the record of what the identifiers used to
be — a question with no other home, since after migration the old IDs exist only in the previous
OCFL version.

States: `candidate` (ours, has invalid IDs), `conforms` (ours, already legal), `foreign` (someone
else's METS), `no-mets`, `done`, `no-change`, `failed` (see the `note` column).

```sql
SELECT path, ids_rewritten, from_version, to_version FROM archival_groups WHERE state = 'done';
SELECT path, note FROM archival_groups WHERE state = 'failed';
```

## What the survey can miss, and `--check-completeness`

Deposits index *deposits*, not Archival Groups: a group whose deposit rows were hard-deleted will not
appear in the survey at all, and the survey has no way to notice its own blind spot.

`--check-completeness` answers that from the other direction. After the survey it reads the
**Activity Stream** — the platform's other record of the same population, with an entry for every
Archival Group create and update — and compares the two sets:

```
Archival Groups known from deposits: 812
Archival Groups mentioned in the activity stream: 814
2 Archival Group(s) appear in the stream but not in the survey, e.g. ['cc/abc', 'cc/def']
```

Without the flag, `survey` does exactly the same work and simply doesn't run that comparison. It
changes nothing about what is surveyed or recorded — it is a read-only cross-check, and it can be run
on its own later (`survey --check-completeness` over an already-complete ledger does no new
surveying, just the check).

The two sets will not match exactly, and the differences are expected rather than alarming:

- the stream begins at a **seeded backstop event**, so it does not reach the earliest Archival Groups
  — the survey will know about groups the stream has never mentioned;
- **suppressed events** (this migration's own) are not published.

What matters is the other direction: anything in the stream that the survey never saw means the
deposits query is not showing you the whole job, and the list should not be trusted until that is
understood.

**It is only meaningful after a full survey.** Comparing a deliberately narrowed survey (`--limit`,
`--path`, `--path-prefix`) against the whole stream would report everything else as missing, so the
tool detects that and skips the check with a warning rather than printing a frightening number.

## If a migration goes wrong

OCFL keeps the previous version. The fix is to preserve the old METS again, not to delete a version.
