<?xml version="1.0" encoding="UTF-8"?>
<!--
Rules every editable tier requires, whatever the document's shape: the linkage and file-location
invariants the platform's own machinery relies on. Editability means the platform understands the
document and can change it - and it cannot safely change linkage it cannot resolve, or locations
its parser cannot even load. These run for both tiers; a failure blocks both, leaving the
document navigable-read-only at best.

The CHOSEN structMap: the contract judges one physical candidate - the first explicitly-physical
structMap (case-insensitive), else the first untyped one. The two entities below spell that out
so rules scoped to the chosen map do not accidentally judge unchosen sibling maps, which are
preserved as parsed and never edited. Chosen-scope rules are written as single asserts on the
structMap (aggregating over its divs) rather than per-div rules: a per-div context makes the
XSLT template matcher evaluate the chosen-map predicates for every node in the document, which
turns a 355-file judgement from under a second into tens of seconds.

The xsl:key declarations exist for the same reason: without them, every fptr's resolution check
is a full-document scan, quadratic in file count.

Deliberately absent: DMDID resolution. A dangling DMDID is BY DESIGN in the platform's own
skeleton (dmdSecs are created lazily, references first), so it can never be a conformance rule.
Whether a *resolved* dmdSec is editable as MODS is a native check (FOREIGN_DMDSEC), because
DMDID is IDREFS and legacy platform IDs contain spaces - token splitting is not safe in XPath 1.0.

See eprints-tier.sch for how these files are executed and why they stay XPath 1.0.
-->
<!DOCTYPE sch:schema [
<!ENTITY chosenExplicit "mets:structMap[translate(@TYPE, 'physical', 'PHYSICAL') = 'PHYSICAL'][not(preceding-sibling::mets:structMap[translate(@TYPE, 'physical', 'PHYSICAL') = 'PHYSICAL'])]">
<!ENTITY chosenUntyped "mets:structMap[not(@TYPE)][not(preceding-sibling::mets:structMap[not(@TYPE)])][not(../mets:structMap[translate(@TYPE, 'physical', 'PHYSICAL') = 'PHYSICAL'])]">
]>
<sch:schema xmlns:sch="http://purl.oclc.org/dsdl/schematron"
            xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
            queryBinding="xslt">
  <sch:title>METS editability: rules common to every editable tier</sch:title>

  <sch:ns prefix="mets" uri="http://www.loc.gov/METS/"/>
  <sch:ns prefix="xlink" uri="http://www.w3.org/1999/xlink"/>

  <xsl:key name="file-by-id" match="mets:file" use="@ID"/>
  <xsl:key name="any-by-id" match="*[@ID]" use="@ID"/>

  <sch:pattern id="file-pointers">
    <!-- Every fptr in EVERY structMap - physical, logical, whatever - resolves, whether it
         points directly or through an area (a time segment or image region). A logical range
         pointing at nothing is structure the platform cannot understand, let alone edit. -->
    <sch:rule context="mets:structMap//mets:fptr[@FILEID]">
      <sch:assert id="C_FILEID_RESOLVES" test="key('file-by-id', @FILEID)">
        An fptr references a FILEID that no mets:file declares.
      </sch:assert>
    </sch:rule>
    <sch:rule context="mets:structMap//mets:area[@FILEID]">
      <sch:assert id="C_AREA_FILEID_RESOLVES" test="key('file-by-id', @FILEID)">
        An area (time segment or image region) references a FILEID that no mets:file declares.
      </sch:assert>
    </sch:rule>

    <!-- In the CHOSEN physical structMap, every fptr carries FILEID itself: the platform's
         physical walk requires it (an area-only fptr is legal in a logical map, where the
         platform itself writes them, but crashes the physical walk). -->
    <sch:rule context="&chosenExplicit; | &chosenUntyped;">
      <sch:assert id="C_PHYSICAL_FPTR_HAS_FILEID" test="not(.//mets:fptr[not(@FILEID)])">
        An fptr in the physical structMap has no FILEID of its own; the platform's physical
        walk requires one (area-only pointers belong in logical structMaps).
      </sch:assert>
    </sch:rule>
  </sch:pattern>

  <sch:pattern id="file-locations">
    <!-- Exactly one FLocat, and it locates something: the platform's parser takes the single
         FLocat of each file and reads its href, and fails on zero, two, or a missing href.
         METS allows 0..* FLocats; a document using that freedom is not one the platform can
         load for editing. -->
    <sch:rule context="mets:fileSec//mets:file">
      <sch:assert id="C_ONE_FLOCAT" test="count(mets:FLocat) = 1 and mets:FLocat/@xlink:href">
        A file must have exactly one FLocat, carrying an xlink:href - the platform's parser
        cannot load a file with zero or several locations, or a location without an href.
      </sch:assert>
    </sch:rule>
  </sch:pattern>

  <sch:pattern id="logical-structmaps">
    <!-- Logical structMaps are edited by ADDRESS: replaced, reordered and removed by their root
         div's ID. A logical map whose root div has no ID cannot be targeted by any of those
         operations - present but unchangeable, which is exactly what editable must not mean. -->
    <sch:rule context="mets:structMap[translate(@TYPE, 'logical', 'LOGICAL') = 'LOGICAL']">
      <sch:assert id="C_LOGICAL_ROOT_HAS_ID" test="mets:div/@ID">
        A logical structMap's root div has no ID, so the structMap cannot be replaced,
        reordered or removed.
      </sch:assert>
    </sch:rule>
  </sch:pattern>

  <sch:pattern id="struct-links">
    <!-- Both ends of every smLink resolve - to a file (the platform's own arcrole style) or to
         a div (Goobi's logical-to-physical style). Raw string match, which is exactly how a
         legacy space-containing platform ID resolves too. -->
    <sch:rule context="mets:structLink/mets:smLink">
      <sch:assert id="C_SMLINK_FROM_RESOLVES" test="key('any-by-id', @xlink:from)">
        An smLink's xlink:from references an ID nothing declares.
      </sch:assert>
      <sch:assert id="C_SMLINK_TO_RESOLVES" test="key('any-by-id', @xlink:to)">
        An smLink's xlink:to references an ID nothing declares.
      </sch:assert>
    </sch:rule>
  </sch:pattern>
</sch:schema>
