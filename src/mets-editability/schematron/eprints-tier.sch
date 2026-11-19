<?xml version="1.0" encoding="UTF-8"?>
<!--
The XML-visible rules of the "editable with declared assumptions" tier - the EPrints-migrated
shape defined on the docs site's "METS editability" page. See common.sch for how these files are
built and run, and chosen-structmap.ent for the one map the structural rules are scoped to.

The tier is the shape the save's mutations (CONTRACT.md) turn into the platform's own: a flat
structMap - one root div, untyped or a Directory, holding one div per file, each untyped or an
Item with exactly one fptr - over files that sit directly in objects/ and carry SHA256 fixity, in
one level of fileGrps that agree on USE. Anything outside that shape is a document the mutations
would not turn into a platform document, so it fails the tier.
-->
<!DOCTYPE sch:schema [
<!ENTITY % chosen-structmap SYSTEM "chosen-structmap.ent">
%chosen-structmap;
]>
<sch:schema xmlns:sch="http://purl.oclc.org/dsdl/schematron"
            xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
            queryBinding="xslt">
  <sch:title>METS editability: the EPrints tier's XML-visible rules</sch:title>

  <sch:ns prefix="mets" uri="http://www.loc.gov/METS/"/>
  <sch:ns prefix="xlink" uri="http://www.w3.org/1999/xlink"/>
  <sch:ns prefix="premis" uri="http://www.loc.gov/premis/v3"/>

  <xsl:key name="md-by-id" match="mets:techMD | mets:amdSec" use="@ID"/>

  <sch:pattern id="structmap">
    <sch:rule context="&chosenExplicit; | &chosenUntyped;">
      <sch:assert id="E_HAS_FILES" test="mets:div/mets:div">
        The physical structMap has no file divs; the EPrints tier is a root div holding one div
        per file, and there is nothing here to restructure.
      </sch:assert>
      <sch:assert id="E_NOT_FLAT" test="not(mets:div/mets:div/mets:div)">
        The physical structMap nests divs more than one level below the root; the EPrints tier
        only covers a flat root-plus-file-divs shape.
      </sch:assert>
      <sch:assert id="E_ROOT_NO_FPTR" test="not(mets:div/mets:fptr)">
        The root div points at a file itself; in the EPrints tier the root only holds the file
        divs, and the save turns it into a Directory.
      </sch:assert>
      <!-- TYPE is optional in METS and compared case-insensitively; any OTHER type (EPrints'
           own "eprint"/"page" vocabulary, say) means something the save would have to guess
           at, so it fails the tier rather than being silently retyped. -->
      <sch:assert id="E_ROOT_TYPE"
                  test="not(mets:div[@TYPE][translate(@TYPE, 'DIRECTORY', 'directory') != 'directory'])">
        The root div has a TYPE other than Directory; the EPrints tier's root is untyped or a
        Directory.
      </sch:assert>
      <sch:assert id="E_FILE_DIV_TYPE"
                  test="not(mets:div/mets:div[@TYPE][translate(@TYPE, 'ITEM', 'item') != 'item'])">
        A file div has a TYPE other than Item; the EPrints tier's file divs are untyped or
        Items.
      </sch:assert>
      <sch:assert id="E_ITEM_ONE_FPTR" test="not(mets:div/mets:div[count(mets:fptr) != 1])">
        A file div must carry exactly one fptr.
      </sch:assert>
    </sch:rule>
  </sch:pattern>

  <sch:pattern id="filesec">
    <sch:rule context="mets:fileSec//mets:file">
      <!-- Directly in objects/: the save materialises ONE directory div, objects, and puts
           every file div in it. A file in objects/sub/ would need directory divs the save does
           not create. -->
      <sch:assert id="E_HREF_UNDER_OBJECTS"
                  test="starts-with(mets:FLocat/@xlink:href, 'objects/') and not(contains(substring-after(mets:FLocat/@xlink:href, 'objects/'), '/'))">
        A file's href is not directly under objects/; the save materialises a single objects
        directory and every file must sit directly in it.
      </sch:assert>
      <!-- SHA256 fixity WITH a digest that is one (64 hex digits), resolved through the file's
           ADMID (a single token in this shape) - as P_SHA256. -->
      <sch:assert id="E_SHA256"
                  test="key('md-by-id', @ADMID)//premis:fixity[translate(normalize-space(premis:messageDigestAlgorithm), 'SHA-', 'sha') = 'sha256'][string-length(normalize-space(premis:messageDigest)) = 64][translate(normalize-space(premis:messageDigest), '0123456789abcdefABCDEF', '') = '']">
        The file's technical metadata carries no SHA256 digest (64 hex digits), which every
        import job - and therefore every edit-and-preserve - requires.
      </sch:assert>
    </sch:rule>

    <sch:rule context="mets:fileSec">
      <!-- The save consolidates EVERY fileGrp into one USE="OBJECTS" group. Nested groups have
           no single obvious flattening, so they fail the tier. -->
      <sch:assert id="E_FLAT_FILEGRPS" test="not(mets:fileGrp//mets:fileGrp)">
        The fileSec nests fileGrps; the EPrints tier's groups are one level deep, so they can
        be consolidated into one.
      </sch:assert>
      <!-- All groups must agree on USE: EPrints writes one group per file, all
           USE="reference", and consolidation needs them to be one kind of thing. Mixed USE
           values are the genuine ambiguity (which copy is the content?) - and so is a mix of
           groups WITH a USE and groups WITHOUT one, which the second clause catches (an XPath
           1.0 comparison against a missing attribute is silently false, so the first clause
           alone cannot). Every group counts, referenced or not, because every group is
           consolidated. -->
      <sch:assert id="E_MIXED_FILEGRP_USE"
                  test="not(mets:fileGrp[@USE != current()/mets:fileGrp[1]/@USE])
                        and not(mets:fileGrp[@USE] and mets:fileGrp[not(@USE)])">
        The fileSec mixes fileGrp USE values (or mixes groups with and without USE); which
        group holds the content is ambiguous.
      </sch:assert>
    </sch:rule>
  </sch:pattern>
</sch:schema>
