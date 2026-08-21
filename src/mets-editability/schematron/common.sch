<?xml version="1.0" encoding="UTF-8"?>
<!--
Rules every editable tier requires, whatever the document's shape: referential integrity of the
linkage the platform edits. Editability means the platform understands the document and can
change it - and it cannot safely change linkage it cannot resolve. These run for both tiers; a
failure blocks both, leaving the document navigable-read-only at best.

Deliberately absent: DMDID resolution. A dangling DMDID is BY DESIGN in the platform's own
skeleton (dmdSecs are created lazily, references first), so it can never be a conformance rule.
Whether a *resolved* dmdSec is editable as MODS is a native check (FOREIGN_DMDSEC), because
DMDID is IDREFS and legacy platform IDs contain spaces - token splitting is not safe in XPath 1.0.

See eprints-tier.sch for how these files are executed and why they stay XPath 1.0.
-->
<sch:schema xmlns:sch="http://purl.oclc.org/dsdl/schematron" queryBinding="xslt">
  <sch:title>METS editability: rules common to every editable tier</sch:title>

  <sch:ns prefix="mets" uri="http://www.loc.gov/METS/"/>
  <sch:ns prefix="xlink" uri="http://www.w3.org/1999/xlink"/>

  <sch:pattern id="file-pointers">
    <!-- Every fptr in EVERY structMap - physical, logical, whatever - resolves, whether it
         points directly or through an area (a time segment or image region). A logical range
         pointing at nothing is structure the platform cannot understand, let alone edit. -->
    <sch:rule context="mets:structMap//mets:fptr[@FILEID]">
      <sch:assert id="C_FILEID_RESOLVES" test="//mets:file[@ID = current()/@FILEID]">
        An fptr references a FILEID that no mets:file declares.
      </sch:assert>
    </sch:rule>
    <sch:rule context="mets:structMap//mets:area[@FILEID]">
      <sch:assert id="C_AREA_FILEID_RESOLVES" test="//mets:file[@ID = current()/@FILEID]">
        An area (time segment or image region) references a FILEID that no mets:file declares.
      </sch:assert>
    </sch:rule>
  </sch:pattern>

  <sch:pattern id="struct-links">
    <!-- Both ends of every smLink resolve - to a file (the platform's own arcrole style) or to
         a div (Goobi's logical-to-physical style). Raw string match, which is exactly how a
         legacy space-containing platform ID resolves too. -->
    <sch:rule context="mets:structLink/mets:smLink">
      <sch:assert id="C_SMLINK_FROM_RESOLVES" test="//*[@ID = current()/@xlink:from]">
        An smLink's xlink:from references an ID nothing declares.
      </sch:assert>
      <sch:assert id="C_SMLINK_TO_RESOLVES" test="//*[@ID = current()/@xlink:to]">
        An smLink's xlink:to references an ID nothing declares.
      </sch:assert>
    </sch:rule>
  </sch:pattern>
</sch:schema>
