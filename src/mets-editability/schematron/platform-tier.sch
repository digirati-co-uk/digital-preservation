<?xml version="1.0" encoding="UTF-8"?>
<!--
The XML-visible rules of the "editable" tier - the platform's own METS shape, normatively
specified in the docs repo's 02b-METS-Written-by-the-Platform.md. See eprints-tier.sch for how
these files are executed and why they stay XPath 1.0, and common.sch for the chosen-structMap
entities and the aggregation/key rationale. All structural rules here are scoped to the ONE
structMap the contract judges, and P_PHYSICAL_STRUCTMAP asserts that THAT map is exactly
TYPE="PHYSICAL" - so a document cannot pass the tier on the strength of a conformant map the
judge never walked.

Note what is deliberately absent: ID legality. A platform document written before issue #214
carries raw path IDs (spaces, slashes) and is still fully editable - the #188 step 3 migration
retires those forms, and the judge reports them as LEGACY_IDS without demoting the verdict.
ADMID/DMDID referential integrity is also absent: those attributes are IDREFS whose tokens can
be legacy space-containing IDs, and XPath 1.0 cannot re-implement the platform's two-tier
resolution - that is native-check territory.
-->
<!DOCTYPE sch:schema [
<!ENTITY chosenExplicit "mets:structMap[translate(@TYPE, 'physical', 'PHYSICAL') = 'PHYSICAL'][not(preceding-sibling::mets:structMap[translate(@TYPE, 'physical', 'PHYSICAL') = 'PHYSICAL'])]">
<!ENTITY chosenUntyped "mets:structMap[not(@TYPE)][not(preceding-sibling::mets:structMap[not(@TYPE)])][not(../mets:structMap[translate(@TYPE, 'physical', 'PHYSICAL') = 'PHYSICAL'])]">
]>
<sch:schema xmlns:sch="http://purl.oclc.org/dsdl/schematron"
            xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
            queryBinding="xslt">
  <sch:title>METS editability: the platform tier's XML-visible rules</sch:title>

  <sch:ns prefix="mets" uri="http://www.loc.gov/METS/"/>
  <sch:ns prefix="xlink" uri="http://www.w3.org/1999/xlink"/>
  <sch:ns prefix="premis" uri="http://www.loc.gov/premis/v3"/>

  <xsl:key name="md-by-id" match="mets:techMD | mets:amdSec" use="@ID"/>

  <sch:pattern id="document">
    <sch:rule context="mets:mets">
      <sch:assert id="P_PHYSICAL_STRUCTMAP"
                  test="(&chosenExplicit; | &chosenUntyped;)[@TYPE = 'PHYSICAL']">
        The platform profile requires the judged structMap to carry TYPE="PHYSICAL", exactly.
      </sch:assert>
      <sch:assert id="P_AGENT" test="mets:metsHdr/mets:agent/mets:name">
        The platform profile requires a metsHdr agent.
      </sch:assert>
      <sch:assert id="P_SINGLE_OBJECTS_GROUP"
                  test="count(mets:fileSec/mets:fileGrp) = 1 and mets:fileSec/mets:fileGrp/@USE = 'OBJECTS'">
        The platform profile requires exactly one fileGrp, USE="OBJECTS".
      </sch:assert>
    </sch:rule>
  </sch:pattern>

  <sch:pattern id="structmap">
    <sch:rule context="&chosenExplicit; | &chosenUntyped;">
      <sch:assert id="P_DIV_TYPED" test="not(.//mets:div[not(@TYPE = 'Directory' or @TYPE = 'Item')])">
        Every div in the platform profile's physical structMap is TYPE="Directory" or
        TYPE="Item".
      </sch:assert>
      <sch:assert id="P_ITEM_ONE_FPTR" test="not(.//mets:div[@TYPE = 'Item'][count(mets:fptr) != 1])">
        An Item div carries exactly one fptr.
      </sch:assert>
      <!-- Every directory div below the root anchors its path via ADMID (premis:originalName).
           The root div itself has no path, so it is exempt. -->
      <sch:assert id="P_DIRECTORY_ADMID" test="not(mets:div//mets:div[@TYPE = 'Directory'][not(@ADMID)])">
        A directory div below the root has no ADMID, so its path cannot be anchored in
        premis:originalName.
      </sch:assert>
    </sch:rule>
  </sch:pattern>

  <sch:pattern id="filesec">
    <!-- SHA256 fixity WITH an actual digest value: every edit ends in an import job, and
         import jobs require it. The platform's ADMID points at the amdSec (ADM_...);
         resolution is a raw string match, which is exactly how a legacy space-containing ID
         resolves too. An algorithm label with an empty digest is a record of having lost the
         checksum, not of having one. -->
    <sch:rule context="mets:fileSec//mets:file">
      <sch:assert id="P_SHA256"
                  test="key('md-by-id', @ADMID)//premis:fixity[translate(normalize-space(premis:messageDigestAlgorithm), 'SHA-', 'sha') = 'sha256'][normalize-space(premis:messageDigest) != '']">
        The file's administrative metadata carries no SHA256 digest value, which every import
        job - and therefore every edit-and-preserve - requires.
      </sch:assert>
    </sch:rule>
  </sch:pattern>
</sch:schema>
