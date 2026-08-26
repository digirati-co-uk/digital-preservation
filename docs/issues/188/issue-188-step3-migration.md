# Issue #188 step 3 — migrating preserved METS to legal xs:ID values

Steps 1 (path cache) and 2 (encoded IDs at every minting site) are on `main`. Step 3 is the
migration of documents already preserved. This is what was built for it and why.

## The predicate

A document needs migrating if it **contains an ID that is not a legal NCName**. Not "was it written
before #214" — nothing records that — and not "who wrote it", which is the filter and not the
trigger.

Three consequences follow, and they are the reason for choosing this predicate over "rewrite
everything old":

- **The campaign converges and reruns are safe.** After migration the predicate is false.
- **An ID that is already legal is never touched**, whoever minted it. That covers client-supplied
  logical range IDs, which `ManifestBuilder.MakeRange` turns into public IIIF Range URIs.
- **A pre-#214 document whose paths held no illegal characters is left alone** rather than gaining a
  version for a rewrite that would produce the identical string.

The `mets:agent` creator name narrows the population. Measured over the whole held sample corpus,
the two criteria agree completely: every third-party document (EPrints, Archivematica, all six Goobi
variants) has zero invalid IDs anyway, and every document the platform wrote before #214 has between
8 and 78.

## The rewrite

`MetsIds.Normalise(id)` respells one ID: strip a known prefix (`PHYS_`, `FILE_`, `ADM_`, `TECH_`,
`DMD_`, or the ClamAV event prefixes, whose remainder is itself an ID), encode the stem with
`ToMetsId`, put the prefix back. The result is character-for-character what `MetsIds.Phys` and its
siblings would mint from the same path — pinned by a test, because the two must not drift.

It never reads a path and never asks what an ID means: it re-encodes the string it was given. That
is what keeps the migration inside the rule that IDs are opaque to code (02d, "Opaque to code,
legible to people") — the one place it would have been most tempting to break it.

`MetsIdNormaliser` applies that across a document and follows every reference. The graph walk is
reflective over the generated XmlGen classes, reading the `XmlAttributeAttribute` declarations the
schema produced rather than a hand-transcribed list of about thirty types. Two things it handles
that a name-based sweep would get wrong:

- IDREFS arrive from the XmlSerializer already split on whitespace, and a legacy ID contains spaces,
  so the joined form is tried first — the same tiering, for the same reason, as `IdRefs`.
- `smLink/@xlink:from` is an IDREF; `smArcLink/@xlink:from` has the same name but holds an xlink
  label. Only the first is rewritten.

Collisions (a rewritten ID landing on one that was already valid) are detected and the rewrite is
dropped with a warning, leaving the document unmigrated rather than ambiguous.

`IMetsManager.NormaliseIds` wraps it: rebuild the path cache afterwards, and refuse the whole
normalisation — writing nothing — if that turns up a diagnostic the document did not already have.

## Migrating one Archival Group

**The files do not need to move.** A deposit created against an existing Archival Group *without*
export copies only the METS (`CreateDepositBase.EnsureMets`, the `ExportArchivalGroupMetsOnly`
branch). The diff still comes out right because `WorkspaceManager` merges the METS physical
structure into the combined tree, so every other file is present with the digest the Archival Group
already holds, and `GetDiffImportJob`'s deposit-presence check applies only to adds and patches.

    create deposit (no export) → normalise → if unchanged, stop
                               → diff → gate → execute (event suppressed) → verify

**The gate.** The import job must be exactly one binary to patch, and it must be the METS. This is
not belt and braces: on a METS-only deposit, a file the Archival Group holds but the METS does not
mention would be listed for *deletion* rather than failing the diff. That one assertion closes it.

**The one allowance (found on the first dev trial, 2026-08-26).** Creating a deposit against an
Archival Group preserved before LPII-9 writes the platform's `metadata/` and `metadata/ad-hoc/`
scaffold folders into the deposit's METS (`CreateDepositBase`, and `GetDepositBase` after an
export). The diff for such a group is therefore the METS patch *plus* `ContainersToAdd` for those
two empty folders, and a strict gate would refuse every pre-LPII-9 group. They hold nothing, no
consumer derives anything from them, and they would be added on the group's next preservation
anyway — recording, not content. Both gates (`ImportJobsController.SuppressedButNotMetsOnly` and
the tool's `_refuse_unless_mets_only`) tolerate exactly those two paths, judged relative to the
job's Archival Group, and nothing else in `ContainersToAdd`.

The result is one new OCFL version whose only new content is `mets.xml` (plus, for a pre-LPII-9
group, the two empty scaffold containers).

## The Activity Stream

`ImportJob.SuppressActivityStreamEvent` keeps the resulting version out of the published stream. The
stream is a IIIF Change Discovery feed: its readers treat an entry as "rebuild what you derived from
this", and a rename of identifiers gives them nothing to rebuild.

It suppresses the entry, not the record. `ArchivalGroupEvent` rows are still written, with
`Suppressed` set, and both the collection and page handlers filter on it — identically, since
between them they decide the page boundaries. Writing the row matters for a practical reason as
well: `StorageImportJobsProcessor` takes its watermark from the latest event date, so skipping rows
entirely would leave it re-reading the same window of Storage activities for ever, which is exactly
what a bulk migration would cause.

## Migrating on write

`FeatureFlags:NormaliseMetsIdsOnWrite` makes `MetsManager.WriteMets` migrate whatever document it is
about to write. `WriteMets` is the single point every mutation passes through — `HandleSingleChange`,
`AddItemsToMets`, `DeleteItems`, `SetLogicalStructMap`, `RemoveLogicalStructMap` and
`SetModsInformation` all end there — which is why the behaviour hangs off it rather than off each of
them.

The problem it solves is not that edits are blocked, but that **an edit to a pre-#214 document
currently makes it worse**: the new entries get encoded IDs while the existing ones keep their raw
form, so the only thing creating mixed-generation documents is us editing them. With the flag on,
every edit migrates its own document, and the corpus converges by attrition from the top while the
bulk migration works up from the bottom.

It is a flag, not a permanent behaviour, for two reasons. The first runs of the normaliser should
happen in a controlled batch rather than while somebody waits for a page to save — so turn it on
after the campaign has been through the same documents. And a write is refused if normalisation
fails, which should be impossible (paths are not touched) but would otherwise persist a document we
have just decided we do not understand; a flag is something you can turn off.

One consequence to have decided in advance: a person who edits one rights statement gets a METS diff
touching every ID in the document, preserved under their name with a genuine Activity Stream event.
Invisible at the import-job level — `mets.xml` was the one binary any edit patches — but visible to
anyone diffing two OCFL versions of it.

**Not** an import-job gate. Refusing to preserve a deposit whose METS has invalid IDs was considered
and rejected: it fails at the most expensive moment, after the work is done, and it fails machine
clients — Goobi, the EPrints ingest, the Deposit Archiver — that have no way to act on "normalise the
IDs first". Once nothing can produce an invalid ID and nothing writes one back unchanged, the same
check becomes worth adding to `GetDiffImportJob` in a different role: a regression assertion that
should never fire.

## Doing it

`src/mets-id-migration/` — Python, driving the Preservation API. `survey` and `list` change nothing
and answer the question that decides everything else: how many are there? If the answer is small,
the UI's **Actions → Normalise METS IDs** (behind `FeatureFlags:ShowNormaliseMetsIds`, with
`FeatureFlags:EnableMetsIdNormalisation` on the API) does the same job by hand, and the
"Maintenance only" checkbox beside Preserve gives it the same clean history.

Rehearse on development: it holds far more affected Archival Groups than production, being the
residue of testing and the nightly Playwright runs, which makes it both the larger corpus and the
expendable one.

## What this makes possible later

Once no preserved document contains a space in an ID, the legacy compatibility tiers have nothing
left to serve: the `PHYS_`+raw-path navigation fallback, and the joined-form tier in `IdRefs`. They
should not be removed on the strength of the migration having run, but on the strength of a survey
that reports zero candidates — which is what the predicate above is for.
