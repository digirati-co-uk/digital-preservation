<?xml version="1.0" encoding="UTF-8"?>
<!--
The XML-visible rules of the "editable with declared assumptions" tier - the EPrints-migrated
shape defined in the docs repo's 02e-METS-Editability.md. One rule source, executed by both the
Python judge (lxml isoschematron) and the .NET judge (the compiled XSLT in compiled/).

Each assert's @id is a finding code shared with the native checks; the judges merge Schematron
failures and native findings into one report. Rules that XPath 1.0 cannot express - path
normalisation and uniqueness, NCName legality, the robust deposit-relative guard - are native
code on both sides, specified by 02e. Keep everything here XPath 1.0: lxml's isoschematron
compiles via the XSLT 1.0 skeleton, and .NET's XslCompiledTransform runs XSLT 1.0.
-->
<sch:schema xmlns:sch="http://purl.oclc.org/dsdl/schematron" queryBinding="xslt">
  <sch:title>METS editability: the EPrints tier's XML-visible rules</sch:title>

  <sch:ns prefix="mets" uri="http://www.loc.gov/METS/"/>
  <sch:ns prefix="xlink" uri="http://www.w3.org/1999/xlink"/>
  <sch:ns prefix="premis" uri="http://www.loc.gov/premis/v3"/>

  <sch:pattern id="structmap">
    <sch:rule context="mets:mets">
      <!-- A physical structMap candidate: explicitly physical in any case, or untyped.
           (Which candidate wins, and the case-insensitivity assumption, are reported natively.) -->
      <sch:assert id="E_NO_PHYSICAL_CANDIDATE"
                  test="mets:structMap[not(@TYPE)] or mets:structMap[translate(@TYPE, 'physical', 'PHYSICAL') = 'PHYSICAL']">
        The document has no structMap that is physical or can be assumed physical (untyped).
      </sch:assert>
    </sch:rule>

    <!-- The tier is flat: a root div whose children are file divs. Any deeper nesting is a
         directory structure this tier does not cover. -->
    <sch:rule context="mets:structMap[not(@TYPE) or translate(@TYPE, 'physical', 'PHYSICAL') = 'PHYSICAL']/mets:div">
      <sch:assert id="E_NOT_FLAT" test="not(mets:div/mets:div)">
        The physical structMap nests divs more than one level below the root; the EPrints tier
        only covers a flat root-plus-file-divs shape.
      </sch:assert>
    </sch:rule>

    <!-- Each file div points at exactly one file. -->
    <sch:rule context="mets:structMap[not(@TYPE) or translate(@TYPE, 'physical', 'PHYSICAL') = 'PHYSICAL']/mets:div/mets:div">
      <sch:assert id="E_ITEM_ONE_FPTR" test="count(mets:fptr) = 1">
        A file div must carry exactly one fptr.
      </sch:assert>
    </sch:rule>

    <sch:rule context="mets:structMap[not(@TYPE) or translate(@TYPE, 'physical', 'PHYSICAL') = 'PHYSICAL']//mets:fptr[@FILEID]">
      <sch:assert id="E_FILEID_RESOLVES" test="//mets:file[@ID = current()/@FILEID]">
        An fptr references a FILEID that no mets:file declares.
      </sch:assert>
    </sch:rule>
  </sch:pattern>

  <sch:pattern id="filesec">
    <sch:rule context="mets:fileSec/mets:fileGrp/mets:file">
      <sch:assert id="E_FILE_HAS_HREF" test="mets:FLocat/@xlink:href">
        A file has no FLocat href, so it has no location in the deposit.
      </sch:assert>
      <sch:assert id="E_HREF_UNDER_OBJECTS" test="starts-with(mets:FLocat/@xlink:href, 'objects/')">
        A file's href is not under objects/; the EPrints tier's implied objects container
        requires every file path to sit beneath it.
      </sch:assert>
      <!-- SHA256 fixity, resolved through the file's ADMID (a single token in this shape).
           translate() lowercases and deletes '-' so SHA256, sha256 and SHA-256 all match. -->
      <sch:assert id="E_SHA256"
                  test="//mets:techMD[@ID = current()/@ADMID]//premis:fixity[translate(normalize-space(premis:messageDigestAlgorithm), 'SHA-', 'sha') = 'sha256']
                        or //mets:amdSec[@ID = current()/@ADMID]//premis:fixity[translate(normalize-space(premis:messageDigestAlgorithm), 'SHA-', 'sha') = 'sha256']">
        The file's technical metadata carries no SHA256 fixity, which every import job - and
        therefore every edit-and-preserve - requires.
      </sch:assert>
    </sch:rule>

    <!-- All groups holding referenced files must agree on USE: EPrints writes one group per
         file, all USE="reference", and consolidation on save needs them to be one kind of thing.
         Mixed USE values are the genuine ambiguity (which copy is the content?). -->
    <sch:rule context="mets:fileSec">
      <sch:assert id="E_MIXED_FILEGRP_USE"
                  test="not(mets:fileGrp[@USE != current()/mets:fileGrp[1]/@USE])">
        The fileSec mixes fileGrp USE values; which group holds the content is ambiguous.
      </sch:assert>
    </sch:rule>
  </sch:pattern>
</sch:schema>
