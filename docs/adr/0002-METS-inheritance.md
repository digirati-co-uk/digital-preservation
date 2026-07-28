# METS Inheritance Rules — Design Discussion

This document records the design discussion that established the rules for
`EffectiveAccessRestrictions`, `EffectiveRightsStatement`, and `EffectiveRecordInfo`
as implemented in `MetsParser` and documented in `EffectiveMetadataInheritanceTests`.

---

## The reasoning behind the inheritance rules

**Q: Can you remember the thinking behind these rules? Particularly, "Logical structure
does not affect access or rights for physical files."**

The reasoning is clearly worked out in the WoW experimental test — the key passage is
the comment at line 138–140 of `ParseWomenOfWestminster` and the contrast between how
RecordInfo and access/rights are inherited.

The core distinction is that **access and rights are properties of the physical byte
stream, not of how you conceptually group files**:

- `amber-rudd.m4a` gets its RecordInfo (`MS 2249/1`, the Amber Rudd interview identifier)
  from the logical range, because the logical range IS the catalogue record — it's saying
  "this file represents this specific interview."
- But its access (`Level1`, inherited from `objects/`) comes from the physical tree, because
  access is set by the archivist based on what the file actually contains, independently of
  how it's organised logically.

The practical force behind the rule comes from the **Liddle tapes pattern**: a single WAV
file spans multiple logical ranges (one tape side covers several interviews). If logical
ranges could impose access restrictions on physical files, you'd have an immediate conflict
— which range wins? There's no coherent answer. The physical tree has no such ambiguity.

There's also a concrete example in WoW: `angela-eagle.m4a` is absent from the logical
structMap entirely (only the redacted version appears in the range), but it has a direct
`"Closed"` access on its physical div. If logical ranges could propagate access down to
physical files, that `"Closed"` designation could be bypassed by the fact that it has no
logical range to inherit from. Keeping access strictly in the physical tree makes the
access model predictable and tamper-resistant.

The DMD_PHYS_ROOT rule for logical ranges is the same principle from the other direction:
a logical range does need *some* default access (e.g. for the IIIF manifest), and the most
defensible source is the top-level collection descriptor — not `objects/`, which is a
physical layout convention that has no logical meaning.

---

## Goobi METS: logical structure IS the source of access

**Q: For `b33061592.mets.xml`, our parser IS resolving effective access conditions on
physical files, when the access conditions are set on logical structure. In this case,
it's what I want to happen. I understand the potential problems you have identified. But
in scenarios where there is no ambiguity, it should be allowed — and in fact IS allowed.
What am I missing?**

Nothing — you're right. The key difference is structural.

**Goobi** (`b33061592.mets.xml`): logical divs have no `fptr` elements. Physical-to-logical
linkage is done entirely via `<mets:structLink>` / `<mets:smLink>` elements connecting
logical div IDs to physical div IDs. The parser has specific code to handle this: it
resolves the deepest-only logical assignment for each physical div and uses that for
access fallback.

**Leeds-native METS**: access lives in the physical tree (`objects/` div with a DMDID
pointing to a MODS section containing `accessCondition`). Logical ranges reference files
via `fptr`. The concern that drove the rule was: don't let logical `fptr` references
"inject" access that conflicts with or overrides the physical tree, especially when the
same file appears in multiple ranges.

So what the rule should really say is:

> **Physical tree takes precedence.** If access/rights are found anywhere by walking up
> the physical tree, that's the answer — logical structure is irrelevant. Only if the
> physical tree yields nothing does the file's logical assignment (via structLink
> deepest-only, or via fptr) become the source of effective access.

The "logical structure does not affect access" formulation was really saying "logical
fptrs don't override physical access" — which remains true. What it missed is the Goobi
case where the physical tree is simply silent on access, making logical the only possible
source.

This led to updating the rule in `EffectiveMetadataInheritanceTests` to:

> **The physical tree always takes precedence: logical structure cannot override physical
> access.**  
> If the physical tree yields nothing (no access anywhere from file up to root), fall
> back to the file's logical assignment — deepest structLink range (Goobi), or single
> whole-file fptr range (Leeds-native). This handles third-party METS where access lives
> only in logical DMDs.

---

## Leeds-native METS trying to replicate logical access inheritance

**Q: I've tried to re-create the same logical access inheritance as the Goobi example
in `leeds-mets-version.xml` — but it doesn't do the same thing. What's the difference?**

The difference is the linkage mechanism.

**Goobi** uses `structLink` to connect logical ranges to physical pages. The parser has
specific code to handle this (`ApplySmLinkLogicalFileRefs`): it builds a deepest-only
logical assignment for each physical div and uses that for access fallback.

**Leeds version** has no `structLink`. Logical divs contain `fptr` elements pointing
directly at files. The parser uses the fptr-based assignment for RecordInfo inheritance
(the "single whole-file fptr in exactly one range" rule), but that same mechanism is not
currently wired up as an access fallback for physical files.

There is also a secondary difference: the Leeds version has `PHYS_objects` with
`DMDID="DMD_objects"` pointing to `<mods:mods/>` — an empty but present DMD section.
Analysis of the parser code shows this is **not** a current bug: `GetDmdForDiv()` returns
`null` for access restrictions when the MODS has no `accessCondition` elements, which is
treated identically to having no DMDID at all.

The asymmetry in `SetFileEffective()` is the real issue:

```
Access chain:    own → parentAccess → fileToAssociatedRange (structLink only) → []
RecordInfo chain: own → fileToWholeFileRanges (fptr, exactly-one-range) → fileToAssociatedRange → parentRecordInfo
```

RecordInfo uses `fileToWholeFileRanges` (fptr-based) as a fallback; access does not.

---

## How "physical takes precedence" squares with allowing logical access

**Q: How does this square with the earlier conclusion "The core distinction is that access
and rights are properties of the physical byte stream, not of how you conceptually group
files"?**

It's a genuine tension, but they can be reconciled — the earlier principle was about
something slightly different from what's now being considered.

**"Access is a physical property" was an argument about priority and source-of-truth, not
about where in the METS the declaration can live.**

The Liddle tapes argument was: a tape side spans multiple interviews, so the logical
structure cannot tell you which interview's access applies to the tape — you need to look
at the physical declaration. The principle was about preventing logical structure from
*overriding* a physical declaration, and about preventing ambiguity when a file appears
in multiple ranges. It was never an argument that access information *must* be declared
in the physical structMap.

**The distinction that actually matters is unambiguity, not physical-vs-logical placement.**

When the physical tree is silent and a file maps unambiguously to one logical range, using
that range's access isn't saying "the logical organisation determines access." It's saying
"the logical range is the archival entity this file belongs to, and that entity's access
restriction *is* the file's access restriction." The access is still a property of the
file — we just found it in a different part of the METS.

**Where the earlier principle still holds absolutely:**

- Physical access always beats logical access. If the physical tree says Level1, that's
  the answer — no logical range can override it.
- A file appearing in multiple logical ranges with *different* access remains undefined
  regardless of whether the physical tree is silent.

---

## The scenario: 100 files, override 10 via logical grouping

**Q: The physical is a flat list of 100 files. The user sets "Open" access on objects/.
But then realises 10 of the files should be "Closed". They could set access=Closed on
each of the 10 files individually. But they might think they can create a logical range,
put those 10 files in it, and set the access on the logical range.**

**If physical always wins, the 10 files will still inherit from the physical root. But,
the relaxation we could make is: remove the access condition from the physical root, and
do ALL access via logical — put the other 90 files in their own group with access=Open.
I think this is what I am doing in `leeds-mets-version.xml`, but it won't work yet
because there's no `fileToWholeFileRanges` step for access computation. The question is,
shall we allow this approach? Or does it introduce potential problems?**

With the proposed fptr fallback in place, Option B works — but only because the author
has accepted a discipline:

- Remove all access from the physical tree (objects/ has no access condition)
- Every file must appear as a whole-file fptr in exactly one logical range
- The 90 files go in an "Open" range, the 10 files go in a "Closed" range

This works, and it's structurally analogous to what Goobi does via structLink. The
"exactly one range" rule provides the same unambiguity guarantee.

**The question is whether the discipline is enforceable and the failure mode is acceptable.**

If a file is accidentally left out of all logical ranges — say the user adds an 11th file
to the deposit but forgets to assign it to a range — it gets empty access. Not "Open"
(which is probably the intended default), just nothing. That's a silent data problem: the
METS is structurally valid, no parse error, but the file is effectively undescribed from
an access perspective.

With Option A (per-file physical, or access on objects/ with per-file overrides), the
omission is safe: the stray file inherits from objects/ and gets "Open." The physical
root acts as a safety net.

With Option B, there is no safety net. You gave it up deliberately by removing the access
condition from objects/.

**This is actually the deeper tension with the whole approach:** the reason "physical takes
precedence" makes sense architecturally is that the physical tree provides a complete
covering of all files — every file lives somewhere in the physical hierarchy, so it can
always inherit upward. The logical tree is not guaranteed to cover all files. When you
move access responsibility to the logical structure, you lose that completeness guarantee.

**So the question to answer before allowing it:** is partial logical coverage a user error
that should be detectable, or is it a silent dangerous state?

If you surface it — e.g. validation that warns "these files have no effective access when
physical access is absent" — then Option B is safe to allow. The failure mode is visible
and correctable. Without that validation, I'd be cautious, because the archivist's plan
looks right but has a hidden gap.

On balance: the approach is coherent and the `fileToWholeFileRanges` extension would make
it work. But a validation check alongside it — something that can report "N files have no
effective access" when the physical tree is silent — is needed rather than implementing
the fallback in isolation.
