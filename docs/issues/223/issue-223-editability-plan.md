# Issue 223: METS editability by conformance — the plan

**Status:** agreed direction, 2026-08-21. Classification and mutate-on-save confirmed by Tom;
awaiting Leeds' agreement on the parts marked for them below.
**Issue:** [#223 — Decide METS editability by conformance to a profile, not by the mets:agent name](https://github.com/digirati-co-uk/digital-preservation/issues/223)
**Builds on:** the measured conformance data in #223's comments (harness: #227, `SurveyMetsCorpus`),
the #188 ID migration (step 3 merged to `main` 2026-08-21 via PR #232), and the shape documentation
in [digital-preservation-docs `mets-profiles`](https://github.com/digirati-co-uk/digital-preservation-docs/tree/mets-profiles/documentation).

## Why now

Leeds has a real, current use case: applying rights statements and access conditions to items whose
METS the platform did not write — above all the EPrints-migrated corpus, which is most of what
production holds. Today a single equality check (`mets.Agent == Constants.MetsCreatorAgent`) makes
all of it read-only. #223 established that the obstacle is convention, not information: a real
410-file EPrints METS resolves completely, to unambiguous deposit-relative paths, under two small
tolerances. What is missing is a *defined, agreed, checkable* boundary for what the platform may
edit — and that boundary is this deliverable.

No platform code changes happen yet. The #188 step 3 migration must bed in on dev and prod,
everything must be migrated, and new by-hand items must be demonstrably healthy first. Everything in
Phases 1–3 below is documentation, specification, and *standalone* code.

## Vocabulary — three words that must not blur

The reviews of this area kept tripping over one word, "conformance", doing three jobs. The
documentation and the code use three, and they are strictly ordered — each implies the one before:

1. **Parseable** — `MetsParser` can read the document into the domain model. The parser is
   deliberately forgiving and coupled to nothing (`XDocument`/LINQ, no XmlGen); it exists to read
   METS from anywhere — Archivematica, EPrints, Goobi, and shapes we have not met yet. We hope
   *everything* is parseable. Failure to parse is a diagnostic about the document, never a policy
   judgment about it.
2. **Navigable** — the physical structure resolves to a complete, unambiguous tree of
   deposit-relative paths: in implementation terms, `MetsCache.Build` populates fully with no
   diagnostics. Navigability is what the UI tree, diffing, and read-modify need. A document can be
   perfectly parseable and not navigable (Goobi: parseable, but its "paths" are IIIF URLs).
3. **Editable** — the platform may mutate the document and save it. Requires navigability, plus the
   structural invariants `MetsManager` relies on, **plus policy**: some documents that could be
   edited must not be (see the living-editor principle below).

"Conformance" hereafter means conformance to a *named profile tier*, checked by the judge — never a
loose synonym for any of the three words above.

## Two principles

**Conformance is necessary but not sufficient.** A document with a living external editor is not
editable here even if it conforms, because two writers with different models silently corrupt each
other. This is the real reason Goobi METS is never editable — Goobi actively re-edits its own
documents — and it is stated as a principle so the next "but this source's METS looks fine" has an
answer that is not incidental.

**A resolved path must be deposit-relative.** The measured trap: loosen div typing and Goobi 2026
becomes a false positive, its cache populated with `https://…` keys. Every tier of the profile, and
the judge, requires that every resolved path is a relative path within the deposit. This guard
travels with any typing tolerance, always.

## The classification

| Class | Parseable | Navigable | Editable | Basis |
|---|---|---|---|---|
| Platform-written (02b profile) | yes | yes | **yes** | Post-#188: all IDs legal NCNames, cache builds clean, structure is ours by construction |
| EPrints-migrated | yes | yes, under declared assumptions | **yes, with declared assumptions**; first save restructures to 02b | Measured: 410/410 paths resolved, 0 diagnostics, under the assumptions below. Scripted migration outputs; no living external editor |
| Archivematica | yes | partially (case-insensitive `TYPE="physical"`; some directory divs lack ADMID) | **no** — read-only | Not our document to restructure; navigability tolerance is for *reading* only |
| Goobi | yes | no (absolute IIIF hrefs fail the deposit-relative guard) | **never** | Fails the guard — and fails the living-editor principle regardless of shape |
| Anything else | hopefully | judged per document | only if it reaches a defined editable tier | The judge decides from the document, not from the agent name |

The `mets:agent` name drops out of the decision entirely — which was #223's founding point. It
remains in the document as provenance, nothing more.

## The EPrints assumptions, precisely

These are the "certain default assumptions" under which an EPrints-migrated METS is navigable and
editable. Each is a reading rule; none changes a byte on disk until a save:

1. **An untyped `structMap` is read as physical** when it is the only structMap (or the only
   untyped one and no `TYPE="PHYSICAL"` exists). METS makes `TYPE` optional; absence is not
   contradiction.
2. **`TYPE` comparison is case-insensitive** on both `structMap` and `div` (this tolerance also
   serves Archivematica's `TYPE="physical"` for read-only navigation).
3. **An untyped `div` carrying an `fptr` is read as an Item.**
4. **The `objects` container is implied.** These documents are flat — a root div and one div per
   file, no directory divs — and every `FLocat/@xlink:href` is already deposit-relative under
   `objects/`. The root container div for `objects` is synthesised on read from the common path
   prefix. Reading never writes it back.
5. **The file groups are mapped.** These documents carry fileGrps with USE values like
   `reference`/`original`/`DEFAULT` rather than our single `OBJECTS` — and EPrints writes **one
   fileGrp per file**, all `USE="reference"` (verified against the real `eprint_10315` sample), so
   referencing several groups is the *normal* shape, not an ambiguity. All the groups the physical
   structMap references are treated together as the OBJECTS-equivalent, consolidated into one
   `USE="OBJECTS"` group on save. What fails the tier is genuine ambiguity: referenced files in
   groups with *different* USE values, or two referenced entries resolving to the same
   deposit-relative path.
6. **Every resolved path is deposit-relative** (the standing guard).

Because these documents have no directory divs, nothing in them needs `premis:originalName` on
read — the first directory div they ever acquire is the `objects` div materialised on save.

## Mutate-on-save — the contract (decided)

**On the first platform save, the document is restructured to the 02b profile.** Decided 2026-08-21;
the alternative — a document that stays foreign until individual edits force our conventions in
piecemeal — creates a third shape that is neither EPrints' nor ours, and that chimera is the worst
outcome for every future parser, theirs or ours. One save, one transition, 02b thereafter.

What makes this palatable, and what the Leeds document must say plainly:

- **The restructure rides along with a real edit.** Applying a rights statement was always going to
  be a new OCFL version, a (published) Activity Stream event, and a IIIF rebuild. Nothing is
  mass-transformed: the tolerances handle reading forever, and restructuring happens lazily, per
  document, on the first edit somebody actually wanted. (This is the "tolerate, don't transform"
  position already recorded on #223, carried to its conclusion.)
- **Existing IDs survive.** `eprint_10315_370441` is a legal NCName, and the platform's rule —
  established through #188 and #232 — is that a legal ID is left alone, whoever minted it and
  whatever scheme it follows (IDs are opaque to code; see 02d). Only *new* elements get
  platform-minted IDs via `MetsIds`.
- **Provenance is kept honest.** The original CREATOR agent stays; the platform is added as a
  modifying agent. The document's history remains legible in its header.

The save performs, concretely:

1. `TYPE="PHYSICAL"` written onto the structMap; divs given their types (`Directory`/`Item` per our
   conventions).
2. The implied `objects` div **materialised**: a real Directory div, with an `amdSec`/`techMD`
   carrying `premis:originalName` (`objects`), ADMID wired, IDs minted via `MetsIds.Adm`.
3. The referenced fileGrp becomes (or is consolidated into) the single `USE="OBJECTS"` group.
4. The platform agent appended to `metsHdr`.
5. Whatever the edit itself was (the rights statement, the access condition) applied through the
   normal `MetsManager`/`MetadataManager` machinery.
6. Nothing else: no ID renumbering, no reordering for its own sake, no removal of sections we do
   not understand — they are preserved as parsed.

**Implication for third-party code**, stated for the Leeds document: any external tool that re-reads
one of these documents after a platform save will find an explicitly physical, typed structMap, an
`objects` Directory div with PREMIS metadata, and a single OBJECTS fileGrp — a *more* explicit
document than it wrote, with all of its own IDs intact. Since these are scripted migration outputs,
external re-editing is not expected; if it ever happens, the document it finds is legal METS that
says outright what the original left implied.

## The judge — shared output contract

Two runnable implementations, one behaviour. Given a METS document, the judge answers:

- **Verdict**: `editable` (02b tier) | `editable-with-normalisation` (EPrints tier — a save will
  restructure as above) | `navigable-read-only` (Archivematica tier) | `not-editable` (everything
  else, Goobi included).
- **Reasons**: every rule that failed or was satisfied-by-assumption, in terms a person can act on
  (the same spirit as `FullMets.PathDiagnostics`).
- **For `editable-with-normalisation`**: the list of mutations a save would perform on *this*
  document — effectively a dry run of the contract above.

Rule sources, layered:

- **Schematron** (one shared rule set, XPath 2-ish, executable from both sides via XSLT) for
  everything the XML can answer: structMap/div typing per tier, fileSec shape, header requirements,
  ADMID/DMDID/FILEID referential integrity, SHA256 fixity presence.
- **Native code each side** for what Schematron cannot see: path normalisation and uniqueness, the
  deposit-relative guard, implied-`objects` inference, fileGrp-reference ambiguity. Specified
  prose-first in 02e so both implementations are written *from the document*, not from each other.

Acceptance is measured, not asserted: both judges must reproduce the table from #223's conformance
survey over the held corpus — our fixtures, EPrints (4-file and 410-file), Archivematica, Goobi —
via the #227 harness on the .NET side and equivalent fixtures on the Python side. Goobi asserting
`not-editable` *for the right reasons* (deposit-relative guard) is itself a required test.

## Phases

### Phase 1 — finish the shape documentation (docs repo, `mets-profiles` branch)

- Reconcile `02a-Shape-of-METS.md` with the newer 02b/02c (02a predates them; decide supersede vs
  merge).
- Fold in what #188/#232 settled: all platform IDs are legal NCNames; `MetsIds` is the single
  minting/normalising authority; the 02d opacity rule ("opaque to code, legible to people").
- Make 02b explicitly the normative profile the editable tier is defined against.
- Write **02e — Editability**: the vocabulary, the two principles, the classification, the EPrints
  assumptions, and the mutate-on-save contract, essentially the content of this plan made normative.

### Phase 2 — the Leeds decision document

A short paper extracted from 02e, framed as decisions needing their agreement rather than a spec to
wade through:

1. the editable / read-only classification (especially: Goobi and Archivematica are permanently
   read-only, and why);
2. mutate-on-first-save, with the worked example: *here is an EPrints METS before; here it is after
   you apply a rights statement*; what any of their downstream code would encounter;
3. what `editable-with-normalisation` means operationally for their rights/access-conditions work.

We do not wait for their sign-off to build Phase 3 — a working example is part of how the
conversation is had.

### Phase 3 — Schematron and the two judges (start immediately, fully standalone)

- The Schematron rule set, tiered (02b tier; EPrints tier), living with the judge code.
- **Python judge**: standalone (lxml, Schematron via XSLT, native path checks). Deliberately the
  seed of the eventual PyPI METS library — its public surface designed as if already extracted,
  even while it lives in `src/mets-editability/` (or its own repo).
- **.NET judge**: a new `DigitalPreservation.Mets.Conformance` project in a PR against `main`
  (unblocked now #232 is merged). It reuses `MetsCache.Build`'s diagnosable pass and
  `FullMets.PathDiagnostics` — written for exactly this purpose — rather than reimplementing; kept
  free of reverse dependencies so it remains extractable.
- Shared acceptance corpus and the verdict contract above.

### Phase 4 — platform changes (later, gated)

*(Rewritten 2026-08-21, after Phases 1–3 delivered and the prerequisites they surfaced.)*

**Gates — all must hold before any Phase 4 behaviour ships:**

1. #232 bedded in on dev **and** prod; the dev (2,430) and prod (139) migration campaigns run;
   new by-hand items demonstrably healthy.
2. Leeds agreement on RFC 008 (the Phase 2 decision paper in the docs repo).

**Prerequisite fixes** — parser/platform work the wiring depends on. These can merge earlier and
independently; each was found by measuring the judge against the real machinery:

- **#236** — `MetsParser` reads any `premis:storage` regardless of `storageMedium`, and throws on
  two. An edited-then-exported EPrints item contains exactly two, so this crashes the parser the
  moment editing is enabled.
- **#237** — EPrints record identifiers (including the `id.library.leeds.ac.uk` PID) sit in the
  METS namespace and are invisible to the parser.
- **Audit M10** — the parser reads only the *first-resolving* dmdSec of a div's DMDID. The
  agreed foreign-dmdSec rule (edit = append a platform dmdSec ID to the IDREFS) depends on
  effective-metadata resolution reading every resolved section.
- **Audit P5** — `MetsManager` overwrites a shared dmdSec in place. The judge refuses such
  documents meanwhile (`SHARED_DMDSEC`), but the platform-side guard should exist regardless.
- Recommended in the same tranche: the audit's degrade-don't-crash items (P6, P7, P8) — the
  judge guards P7 and aligns with P6, but the parser hardening stands on its own.

**The wiring, in order:**

1. **Reading tolerances** into the editing stack's path cache (`MetsCache`): case-insensitive
   `TYPE`, untyped-as-physical, untyped-fptr-div-as-Item — **together with** the deposit-relative
   guard, never one without the other. Measure before/after with the #227 survey harness; the
   measured table is the acceptance test.
2. **The judge into the platform**: `DigitalPreservation.Mets.Conformance` consumed by the
   Preservation API; the agent-equality gate replaced by the verdict at its three sites
   (`MetsParser.Editable`, `FileSystemMetsStorage`, `S3MetsStorage`); and the read-only
   "judge this document" endpoint/UI panel #223 originally asked for — nearly free once the
   library is wired in.
3. **The restructure-on-first-save** in `MetsManager`, implemented against the judge's own
   mutation dry-run list (the seven steps in CONTRACT.md: type the structMap, root and item
   divs; materialise the `objects` div and re-parent; consolidate fileGrps into OBJECTS; wrap
   bare mdWrap payloads in `mets:xmlData` — the payload-preserved-verbatim promise needs its own
   tests, since a naive typed round trip silently drops exactly that content; append the
   platform agent), plus the foreign-dmdSec append rule.
4. **The normalise endpoint** (`POST /deposits/{id}/mets/normalise`) consults the judge —
   closing the gap that it is currently the one mutation that asks nothing at all.
5. **Feature-flagged rollout**: dev first; replay the twelve production sample documents;
   validate RFC 008's worked example end-to-end (apply a rights statement to an EPrints item on
   dev and verify every survival guarantee against the before/after documents).

**Phase 4's acceptance principle**, learned from the #238 review: *the judge must never be more
tolerant than the machinery it vouches for.* Any document the judge certifies must round-trip
parse → edit → save → parse without loss or error; agreement tests between the judge and the
platform are part of the wiring, not an afterthought.

Also to resolve or explicitly rule on: the parked conformance-backlog findings from #223
(dangling DMDID by design, mdSec-level ADMID absent from the survival index,
cache-resolves-across-fileGrps vs `SetFileAndFileGroup`).

### Deliberately not coupled: library extraction

Moving `MetsParser` and the METS domain model into a separate solution/NuGet package with a
maintained PyPI twin is a real ambition, and the Python judge is genuinely its first seed — but the
extraction is **not** a dependency of any phase here. The .NET judge only has to be *extractable*
(own project, no reverse dependencies). Whether `MetsManager` eventually follows `MetsParser` out
of the solution is a later decision the judges do not need answered.

## Open questions (tracked, not blocking)

- **02a's fate** — supersede or merge (Phase 1's first task).
- **Consolidation detail for fileGrps** on save: rename the referenced group to `USE="OBJECTS"` vs
  build a new group and re-point FILEIDs. To be settled when the save contract is implemented;
  the spec constrains the outcome (single OBJECTS group, IDs preserved), not the mechanism.
- **Where the judges run** operationally: CLI only at first; the read-only "does this conform and
  why" API/UI surface suggested in #223 is Phase 4 territory.
- **Archivematica's missing ADMIDs** — whether the read-only navigability tier tolerates directory
  divs without `premis:originalName` (using the div LABEL as the path segment) or reports them.
  Affects reading only; Archivematica is not editable either way.
