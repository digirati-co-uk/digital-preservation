<?xml version="1.0" encoding="UTF-8"?>
<!--
The XML-visible rules of the "editable" tier - the platform's own METS shape, normatively
specified in the docs repo's 02b-METS-Written-by-the-Platform.md. See eprints-tier.sch for how
these files are executed and why they stay XPath 1.0.

Note what is deliberately absent: ID legality. A platform document written before issue #214
carries raw path IDs (spaces, slashes) and is still fully editable - the #188 step 3 migration
retires those forms, and the judge reports them as LEGACY_IDS without demoting the verdict.
ADMID/DMDID referential integrity is also absent: those attributes are IDREFS whose tokens can
be legacy space-containing IDs, and XPath 1.0 cannot re-implement the platform's two-tier
resolution - that is native-check territory.
-->
<sch:schema xmlns:sch="http://purl.oclc.org/dsdl/schematron" queryBinding="xslt">
  <sch:title>METS editability: the platform tier's XML-visible rules</sch:title>

  <sch:ns prefix="mets" uri="http://www.loc.gov/METS/"/>
  <sch:ns prefix="xlink" uri="http://www.w3.org/1999/xlink"/>
  <sch:ns prefix="premis" uri="http://www.loc.gov/premis/v3"/>

  <sch:pattern id="document">
    <sch:rule context="mets:mets">
      <sch:assert id="P_PHYSICAL_STRUCTMAP" test="mets:structMap[@TYPE = 'PHYSICAL']">
        The platform profile requires a structMap with TYPE="PHYSICAL", exactly.
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
    <sch:rule context="mets:structMap[@TYPE = 'PHYSICAL']//mets:div">
      <sch:assert id="P_DIV_TYPED" test="@TYPE = 'Directory' or @TYPE = 'Item'">
        Every div in the platform profile's physical structMap is TYPE="Directory" or
        TYPE="Item".
      </sch:assert>
    </sch:rule>

    <sch:rule context="mets:structMap[@TYPE = 'PHYSICAL']//mets:div[@TYPE = 'Item']">
      <sch:assert id="P_ITEM_ONE_FPTR" test="count(mets:fptr) = 1">
        An Item div carries exactly one fptr.
      </sch:assert>
    </sch:rule>

    <!-- Every directory div below the root anchors its path via ADMID (premis:originalName).
         The root div itself has no path, so it is exempt. -->
    <sch:rule context="mets:structMap[@TYPE = 'PHYSICAL']/mets:div//mets:div[@TYPE = 'Directory']">
      <sch:assert id="P_DIRECTORY_ADMID" test="@ADMID">
        A directory div below the root has no ADMID, so its path cannot be anchored in
        premis:originalName.
      </sch:assert>
    </sch:rule>

    <sch:rule context="mets:structMap[@TYPE = 'PHYSICAL']//mets:fptr[@FILEID]">
      <sch:assert id="P_FILEID_RESOLVES" test="//mets:file[@ID = current()/@FILEID]">
        An fptr references a FILEID that no mets:file declares.
      </sch:assert>
    </sch:rule>
  </sch:pattern>
</sch:schema>
