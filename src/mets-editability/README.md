# mets-editability

Is this METS file editable by the platform — and what would saving do to it?

```
pip install -r requirements.txt
python mets_editability.py judge path/to/mets.xml
python mets_editability.py judge path/to/folder --json
```

This is the Python half of the **editability judge** (issue #223): a runnable implementation of
the rules on the docs site's "METS editability" page, specified concretely in
[CONTRACT.md](./CONTRACT.md). The .NET half is `DigitalPreservation.Mets.Conformance` in the main
solution; both implement the contract, both enforce the same acceptance table and the same golden
judgements, and when they disagree the contract decides which is wrong.

Read-only. Nothing here writes, anywhere — the "mutations" in a judgement are a *dry run* of
what the platform's first save would do to an EPrints-tier document.

## The verdicts

| Verdict | Meaning |
|---|---|
| `editable` | The platform's own shape. Pre-#214 legacy IDs are noted, not fatal. |
| `editable-with-normalisation` | The EPrints shape, under the declared assumptions; the judgement lists the save's mutations. |
| `navigable-read-only` | The platform can open it and every file resolves to a unique deposit-relative path, but no editable tier is met. |
| `not-editable` | The platform cannot open it: its parser would fail, or the files do not resolve. |

The judge judges the document. Policy the document cannot show — Goobi is read-only because Goobi
still edits its own documents, however conformant they look — is the platform's overlay.

## How the rules are split

- **Schematron** (`schematron/*.sch`) holds the XML-visible tier rules. It is the source people
  edit; `python tools/compile_schematron.py` compiles it into plain XSLT 1.0 under
  `schematron/compiled/`, which is committed, and **both judges run that compiled XSLT** (lxml's
  `etree.XSLT` here, `XslCompiledTransform` in .NET) — one artefact, one behaviour. After editing
  a `.sch` (or `chosen-structmap.ent`), recompile; a test and CI both fail if `compiled/` is not
  exactly what the sources compile to.
- **Native code** (`app/judge.py`, and its twin `EditabilityJudge.cs`) holds what XPath 1.0
  cannot say: reading the document the way the platform's parser does (anything the parser would
  throw on is refused), path checks, NCName legality (from `ncname_ranges.json`, a pinned copy of
  the migration tool's XmlConvert-derived tables), and verdict assembly. The two files are
  written function for function, so they can be compared side by side.

## Tests

```
python -m unittest tests -v
```

- The acceptance table in CONTRACT.md, over the real sample corpus in
  `src/DigitalPreservation/XmlGen.Tests/Samples/`.
- The golden judgements: `fixtures/` holds one small document per rule and per parity edge
  (each says in its first comment what it pins), and `fixtures/judgements.json` their expected
  judgements. The .NET suite asserts the same file. To add a rule, add a fixture that fails it
  and its entry, in the same change as the rule.
- Unit cases, the compiled-rules freshness check, thread safety, and robustness (deep documents,
  paths that are not readable files).

## A seed, on purpose

This package is deliberately standalone — no imports from the platform, fixtures reached only as
test data — because it is the intended seed of a future published METS library (PyPI, with the
.NET METS code as the NuGet twin). Keep it that way: anything platform-specific belongs on the
.NET side or in the platform itself.

The .NET project embeds `schematron/compiled/*.xsl` from this folder, outside
`src/DigitalPreservation/`. A Docker build of any service that uses the .NET judge must copy
`src/mets-editability/schematron/compiled` into its build context too; the project refuses to
build without it.
