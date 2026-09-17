# mets-editability

Is this METS file editable by the platform — and what would saving do to it?

```
pip install -r requirements.txt
python mets_editability.py judge path/to/mets.xml
python mets_editability.py judge path/to/folder --json
```

This is the Python half of the **editability judge** (issue #223): a runnable implementation of
the rules in `02e-METS-Editability.md` (docs repo, `mets-profiles` branch), specified concretely
in [CONTRACT.md](./CONTRACT.md). The .NET half is `DigitalPreservation.Mets.Conformance` in the
main solution; both implement the contract, both enforce the same acceptance table, and when they
disagree the contract decides which is wrong.

Read-only. Nothing here writes, anywhere — the "mutations" in a judgement are a *dry run* of
what the platform's first save would do to an EPrints-tier document.

## The verdicts

| Verdict | Meaning |
|---|---|
| `editable` | The platform's own shape (02b). Pre-#214 legacy IDs are noted, not fatal. |
| `editable-with-normalisation` | The EPrints shape, under the declared assumptions; the judgement lists the save's mutations. |
| `navigable-read-only` | Every file resolves to a unique deposit-relative path, but no editable tier is met. |
| `not-editable` | The document does not resolve. |

The judge judges the document. Policy the document cannot show — Goobi is read-only because Goobi
still edits its own documents, however conformant they look — is the platform's overlay.

## How the rules are split

- **Schematron** (`schematron/*.sch`) holds the XML-visible tier rules — one source, two
  executors. Python runs the `.sch` directly (lxml ISO Schematron); .NET runs the compiled XSLT
  committed under `schematron/compiled/`. After editing a `.sch`, regenerate with
  `python tools/compile_schematron.py`; a test proves the compiled form behaves identically.
- **Native code** (`app/judge.py`) holds what XPath 1.0 cannot say: path resolution and
  uniqueness, the deposit-relative guard, NCName legality (from `ncname_ranges.json`, a pinned
  copy of the migration tool's XmlConvert-derived tables), and verdict assembly.

## Tests

```
python -m unittest tests -v
```

The acceptance table in CONTRACT.md runs against the real sample corpus in
`src/DigitalPreservation/XmlGen.Tests/Samples/` — the measured #223 conformance table
re-expressed as verdicts.

## A seed, on purpose

This package is deliberately standalone — no imports from the platform, fixtures reached only as
test data — because it is the intended seed of a future published METS library (PyPI, with the
.NET METS code as the NuGet twin). Keep it that way: anything platform-specific belongs on the
.NET side or in the platform itself.
