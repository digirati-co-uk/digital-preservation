<?xml version="1.0" encoding="UTF-8"?>
<!--
Rules every editable tier requires, whatever the document's shape: the linkage and file-location
invariants the platform's editing relies on. Editability means the platform understands the
document and can change it - and it cannot safely change linkage it cannot resolve. These run
for both tiers; a failure blocks both, leaving the document navigable-read-only at best.

They run only on a document that is already navigable: everything the platform's parser would
throw on (an fptr without a FILEID, a referenced file without exactly one location, a Directory
div without a LABEL, ...) is refused earlier by native code, as a blocker. See CONTRACT.md.

How these files are built and run: each .sch here is compiled once, by
tools/compile_schematron.py, into plain XSLT 1.0 under compiled/, and BOTH judges run that
compiled XSLT (lxml's etree.XSLT in Python, XslCompiledTransform in .NET) - one artefact, one
behaviour. Keep everything XPath 1.0. Each assert's @id is a finding code, shared vocabulary
with the native checks.

Rules scoped to the judged map aggregate on the structMap (one assert over its divs) rather than
firing per div: a per-div context with the chosen-map predicates makes the XSLT template matcher
evaluate those predicates for every node in the document, which turns a 355-file judgement from
under a second into tens of seconds. The xsl:keys exist for the same reason - without them every
fptr's resolution check is a full-document scan, quadratic in file count.

Deliberately absent: DMDID resolution. A dangling DMDID is BY DESIGN in the platform's own
skeleton (dmdSecs are created lazily, references first), so it can never be a conformance rule.
-->
<!DOCTYPE sch:schema [
<!ENTITY % chosen-structmap SYSTEM "chosen-structmap.ent">
%chosen-structmap;
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
  </sch:pattern>

  <sch:pattern id="judged-structmap">
    <!-- METS allows the structMap exactly one div. The platform's editing model holds one root
         div, so a second would not survive a save. -->
    <sch:rule context="&chosenExplicit; | &chosenUntyped;">
      <sch:assert id="C_ONE_ROOT_DIV" test="count(mets:div) = 1">
        The physical structMap has more than one root div; METS allows one, and the platform's
        editing model keeps only one.
      </sch:assert>
    </sch:rule>
  </sch:pattern>

  <sch:pattern id="file-locations">
    <!-- Exactly one FLocat, and it locates something. A file the physical structMap references
         is already held to this natively (the parser cannot load it otherwise); this rule
         extends it to every file in the fileSec, because the editing code addresses a file by
         its one location wherever it meets it - logical pointers and links included. -->
    <sch:rule context="mets:fileSec//mets:file">
      <sch:assert id="C_ONE_FLOCAT" test="count(mets:FLocat) = 1 and mets:FLocat/@xlink:href">
        A file must have exactly one FLocat, carrying an xlink:href.
      </sch:assert>
    </sch:rule>
  </sch:pattern>

  <sch:pattern id="logical-structmaps">
    <!-- Logical structMaps are edited by ADDRESS: replaced, reordered and removed by their root
         div's ID. A root div with no ID - or an empty one - cannot be targeted by any of those
         operations: present but unchangeable, which is exactly what editable must not mean. -->
    <sch:rule context="mets:structMap[translate(@TYPE, 'LOGICAL', 'logical') = 'logical']">
      <sch:assert id="C_LOGICAL_ROOT_HAS_ID" test="not(mets:div[normalize-space(@ID) = ''])">
        A logical structMap's root div has no ID, so the structMap cannot be replaced,
        reordered or removed.
      </sch:assert>
    </sch:rule>
  </sch:pattern>

  <sch:pattern id="struct-links">
    <!-- Both ends of every smLink resolve - to a file (the platform's own arcrole style) or to
         a div (Goobi's logical-to-physical style). Raw string match, which is exactly how a
         legacy space-containing platform ID resolves too. A missing end resolves to nothing. -->
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
