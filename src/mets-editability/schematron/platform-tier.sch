<?xml version="1.0" encoding="UTF-8"?>
<!--
The XML-visible rules of the "editable" tier - the platform's own METS shape, normatively
specified on the docs site's "The METS files we write" page. See common.sch for how these files
are built and run, and chosen-structmap.ent for the one map the structural rules are scoped to.
P_PHYSICAL_STRUCTMAP asserts that THAT map is exactly TYPE="PHYSICAL", so a document cannot pass
the tier on the strength of a conformant map the judge never walked.

Deliberately absent: ID legality. A platform document written before issue #214 carries raw path
IDs (spaces, slashes) and is still fully editable - the #188 migration retires those forms, and
the judge reports them as LEGACY_IDS without demoting the verdict. ADMID references are matched
as raw strings (key() on the whole attribute value), which is how the platform's own single-token
ADMIDs - legacy space-containing ones included - resolve.
-->
<!DOCTYPE sch:schema [
<!ENTITY % chosen-structmap SYSTEM "chosen-structmap.ent">
%chosen-structmap;
]>
<sch:schema xmlns:sch="http://purl.oclc.org/dsdl/schematron"
            xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
            queryBinding="xslt">
  <sch:title>METS editability: the platform tier's XML-visible rules</sch:title>

  <sch:ns prefix="mets" uri="http://www.loc.gov/METS/"/>
  <sch:ns prefix="xlink" uri="http://www.w3.org/1999/xlink"/>
  <sch:ns prefix="premis" uri="http://www.loc.gov/premis/v3"/>

  <xsl:key name="md-by-id" match="mets:techMD | mets:amdSec" use="@ID"/>
  <xsl:key name="amdsec-by-id" match="mets:amdSec" use="@ID"/>

  <sch:pattern id="document">
    <sch:rule context="mets:mets">
      <sch:assert id="P_PHYSICAL_STRUCTMAP"
                  test="(&chosenExplicit; | &chosenUntyped;)[@TYPE = 'PHYSICAL']">
        The platform profile requires the judged structMap to carry TYPE="PHYSICAL", exactly.
      </sch:assert>
      <sch:assert id="P_AGENT" test="mets:metsHdr/mets:agent/mets:name[normalize-space() != '']">
        The platform profile requires a metsHdr agent with a name.
      </sch:assert>
      <sch:assert id="P_SINGLE_OBJECTS_GROUP"
                  test="count(mets:fileSec//mets:fileGrp) = 1 and mets:fileSec/mets:fileGrp/@USE = 'OBJECTS'">
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
      <!-- Every directory div below the root anchors its path: its ADMID names an amdSec
           carrying exactly one premis:originalName. An ADMID that is empty or names nothing
           anchors nothing. The root div itself has no path, so it is exempt. -->
      <sch:assert id="P_DIRECTORY_ADMID"
                  test="not(mets:div//mets:div[@TYPE = 'Directory'][count(key('amdsec-by-id', @ADMID)//premis:originalName) != 1])">
        A directory div below the root does not anchor its path: its ADMID must name an amdSec
        carrying exactly one premis:originalName.
      </sch:assert>
    </sch:rule>
  </sch:pattern>

  <sch:pattern id="filesec">
    <!-- SHA256 fixity WITH a digest that is one: 64 hex digits. Every edit ends in an import
         job, and import jobs compare it with the file's actual checksum - an empty or malformed
         digest can never match. translate() lowercases the algorithm and deletes '-', so
         SHA256, sha256 and SHA-256 all count. -->
    <sch:rule context="mets:fileSec//mets:file">
      <sch:assert id="P_SHA256"
                  test="key('md-by-id', @ADMID)//premis:fixity[translate(normalize-space(premis:messageDigestAlgorithm), 'SHA-', 'sha') = 'sha256'][string-length(normalize-space(premis:messageDigest)) = 64][translate(normalize-space(premis:messageDigest), '0123456789abcdefABCDEF', '') = '']">
        The file's administrative metadata carries no SHA256 digest (64 hex digits), which
        every import job - and therefore every edit-and-preserve - requires.
      </sch:assert>
    </sch:rule>
  </sch:pattern>
</sch:schema>
