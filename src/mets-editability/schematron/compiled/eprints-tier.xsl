<?xml version='1.0' encoding='utf-8'?>
<axsl:stylesheet xmlns:axsl="http://www.w3.org/1999/XSL/Transform" xmlns:sch="http://www.ascc.net/xml/schematron" xmlns:iso="http://purl.oclc.org/dsdl/schematron" xmlns:mets="http://www.loc.gov/METS/" xmlns:xlink="http://www.w3.org/1999/xlink" xmlns:premis="http://www.loc.gov/premis/v3" version="1.0"><!--Implementers: please note that overriding process-prolog or process-root is 
    the preferred method for meta-stylesheets to use where possible. -->
<axsl:param name="archiveDirParameter"/><axsl:param name="archiveNameParameter"/><axsl:param name="fileNameParameter"/><axsl:param name="fileDirParameter"/>

<!--PHASES-->


<!--PROLOG-->
<axsl:output xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:schold="http://www.ascc.net/xml/schematron" xmlns:svrl="http://purl.oclc.org/dsdl/svrl" method="xml" omit-xml-declaration="no" standalone="yes" indent="yes"/>

<!--KEYS-->
<axsl:key name="md-by-id" match="mets:techMD | mets:amdSec" use="@ID"/>

<!--DEFAULT RULES-->


<!--MODE: SCHEMATRON-SELECT-FULL-PATH-->
<!--This mode can be used to generate an ugly though full XPath for locators-->
<axsl:template match="*" mode="schematron-select-full-path"><axsl:apply-templates select="." mode="schematron-get-full-path"/></axsl:template>

<!--MODE: SCHEMATRON-FULL-PATH-->
<!--This mode can be used to generate an ugly though full XPath for locators-->
<axsl:template match="*" mode="schematron-get-full-path"><axsl:apply-templates select="parent::*" mode="schematron-get-full-path"/><axsl:text>/</axsl:text><axsl:choose><axsl:when test="namespace-uri()=''"><axsl:value-of select="name()"/><axsl:variable name="p_1" select="1+    count(preceding-sibling::*[name()=name(current())])"/><axsl:if test="$p_1&gt;1 or following-sibling::*[name()=name(current())]">[<axsl:value-of select="$p_1"/>]</axsl:if></axsl:when><axsl:otherwise><axsl:text>*[local-name()='</axsl:text><axsl:value-of select="local-name()"/><axsl:text>' and namespace-uri()='</axsl:text><axsl:value-of select="namespace-uri()"/><axsl:text>']</axsl:text><axsl:variable name="p_2" select="1+   count(preceding-sibling::*[local-name()=local-name(current())])"/><axsl:if test="$p_2&gt;1 or following-sibling::*[local-name()=local-name(current())]">[<axsl:value-of select="$p_2"/>]</axsl:if></axsl:otherwise></axsl:choose></axsl:template><axsl:template match="@*" mode="schematron-get-full-path"><axsl:text>/</axsl:text><axsl:choose><axsl:when test="namespace-uri()=''">@<axsl:value-of select="name()"/></axsl:when><axsl:otherwise><axsl:text>@*[local-name()='</axsl:text><axsl:value-of select="local-name()"/><axsl:text>' and namespace-uri()='</axsl:text><axsl:value-of select="namespace-uri()"/><axsl:text>']</axsl:text></axsl:otherwise></axsl:choose></axsl:template>

<!--MODE: SCHEMATRON-FULL-PATH-2-->
<!--This mode can be used to generate prefixed XPath for humans-->
<axsl:template match="node() | @*" mode="schematron-get-full-path-2"><axsl:for-each select="ancestor-or-self::*"><axsl:text>/</axsl:text><axsl:value-of select="name(.)"/><axsl:if test="preceding-sibling::*[name(.)=name(current())]"><axsl:text>[</axsl:text><axsl:value-of select="count(preceding-sibling::*[name(.)=name(current())])+1"/><axsl:text>]</axsl:text></axsl:if></axsl:for-each><axsl:if test="not(self::*)"><axsl:text/>/@<axsl:value-of select="name(.)"/></axsl:if></axsl:template>

<!--MODE: GENERATE-ID-FROM-PATH -->
<axsl:template match="/" mode="generate-id-from-path"/><axsl:template match="text()" mode="generate-id-from-path"><axsl:apply-templates select="parent::*" mode="generate-id-from-path"/><axsl:value-of select="concat('.text-', 1+count(preceding-sibling::text()), '-')"/></axsl:template><axsl:template match="comment()" mode="generate-id-from-path"><axsl:apply-templates select="parent::*" mode="generate-id-from-path"/><axsl:value-of select="concat('.comment-', 1+count(preceding-sibling::comment()), '-')"/></axsl:template><axsl:template match="processing-instruction()" mode="generate-id-from-path"><axsl:apply-templates select="parent::*" mode="generate-id-from-path"/><axsl:value-of select="concat('.processing-instruction-', 1+count(preceding-sibling::processing-instruction()), '-')"/></axsl:template><axsl:template match="@*" mode="generate-id-from-path"><axsl:apply-templates select="parent::*" mode="generate-id-from-path"/><axsl:value-of select="concat('.@', name())"/></axsl:template><axsl:template match="*" mode="generate-id-from-path" priority="-0.5"><axsl:apply-templates select="parent::*" mode="generate-id-from-path"/><axsl:text>.</axsl:text><axsl:value-of select="concat('.',name(),'-',1+count(preceding-sibling::*[name()=name(current())]),'-')"/></axsl:template><!--MODE: SCHEMATRON-FULL-PATH-3-->
<!--This mode can be used to generate prefixed XPath for humans 
	(Top-level element has index)-->
<axsl:template match="node() | @*" mode="schematron-get-full-path-3"><axsl:for-each select="ancestor-or-self::*"><axsl:text>/</axsl:text><axsl:value-of select="name(.)"/><axsl:if test="parent::*"><axsl:text>[</axsl:text><axsl:value-of select="count(preceding-sibling::*[name(.)=name(current())])+1"/><axsl:text>]</axsl:text></axsl:if></axsl:for-each><axsl:if test="not(self::*)"><axsl:text/>/@<axsl:value-of select="name(.)"/></axsl:if></axsl:template>

<!--MODE: GENERATE-ID-2 -->
<axsl:template match="/" mode="generate-id-2">U</axsl:template><axsl:template match="*" mode="generate-id-2" priority="2"><axsl:text>U</axsl:text><axsl:number level="multiple" count="*"/></axsl:template><axsl:template match="node()" mode="generate-id-2"><axsl:text>U.</axsl:text><axsl:number level="multiple" count="*"/><axsl:text>n</axsl:text><axsl:number count="node()"/></axsl:template><axsl:template match="@*" mode="generate-id-2"><axsl:text>U.</axsl:text><axsl:number level="multiple" count="*"/><axsl:text>_</axsl:text><axsl:value-of select="string-length(local-name(.))"/><axsl:text>_</axsl:text><axsl:value-of select="translate(name(),':','.')"/></axsl:template><!--Strip characters--><axsl:template match="text()" priority="-1"/>

<!--SCHEMA METADATA-->
<axsl:template match="/"><svrl:schematron-output xmlns:svrl="http://purl.oclc.org/dsdl/svrl" xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:schold="http://www.ascc.net/xml/schematron" title="METS editability: the EPrints tier's XML-visible rules" schemaVersion=""><axsl:comment><axsl:value-of select="$archiveDirParameter"/>   
		 <axsl:value-of select="$archiveNameParameter"/>  
		 <axsl:value-of select="$fileNameParameter"/>  
		 <axsl:value-of select="$fileDirParameter"/></axsl:comment><svrl:ns-prefix-in-attribute-values uri="http://www.loc.gov/METS/" prefix="mets"/><svrl:ns-prefix-in-attribute-values uri="http://www.w3.org/1999/xlink" prefix="xlink"/><svrl:ns-prefix-in-attribute-values uri="http://www.loc.gov/premis/v3" prefix="premis"/><svrl:active-pattern><axsl:attribute name="id">structmap</axsl:attribute><axsl:attribute name="name">structmap</axsl:attribute><axsl:apply-templates/></svrl:active-pattern><axsl:apply-templates select="/" mode="M5"/><svrl:active-pattern><axsl:attribute name="id">filesec</axsl:attribute><axsl:attribute name="name">filesec</axsl:attribute><axsl:apply-templates/></svrl:active-pattern><axsl:apply-templates select="/" mode="M6"/></svrl:schematron-output></axsl:template>

<!--SCHEMATRON PATTERNS-->
<svrl:text xmlns:svrl="http://purl.oclc.org/dsdl/svrl" xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:schold="http://www.ascc.net/xml/schematron">METS editability: the EPrints tier's XML-visible rules</svrl:text>

<!--PATTERN structmap-->


	<!--RULE -->
<axsl:template match="mets:mets" priority="1001" mode="M5"><svrl:fired-rule xmlns:svrl="http://purl.oclc.org/dsdl/svrl" context="mets:mets"/>

		<!--ASSERT -->
<axsl:choose><axsl:when test="mets:structMap[not(@TYPE)] or mets:structMap[translate(@TYPE, 'physical', 'PHYSICAL') = 'PHYSICAL']"/><axsl:otherwise><svrl:failed-assert xmlns:svrl="http://purl.oclc.org/dsdl/svrl" xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:schold="http://www.ascc.net/xml/schematron" test="mets:structMap[not(@TYPE)] or mets:structMap[translate(@TYPE, 'physical', 'PHYSICAL') = 'PHYSICAL']"><axsl:attribute name="id">E_NO_PHYSICAL_CANDIDATE</axsl:attribute><axsl:attribute name="location"><axsl:apply-templates select="." mode="schematron-get-full-path"/></axsl:attribute><svrl:text>
        The document has no structMap that is physical or can be assumed physical (untyped).
      </svrl:text></svrl:failed-assert></axsl:otherwise></axsl:choose><axsl:apply-templates select="@*|*" mode="M5"/></axsl:template>

	<!--RULE -->
<axsl:template match="mets:structMap[translate(@TYPE, 'physical', 'PHYSICAL') = 'PHYSICAL'][not(preceding-sibling::mets:structMap[translate(@TYPE, 'physical', 'PHYSICAL') = 'PHYSICAL'])] | mets:structMap[not(@TYPE)][not(preceding-sibling::mets:structMap[not(@TYPE)])][not(../mets:structMap[translate(@TYPE, 'physical', 'PHYSICAL') = 'PHYSICAL'])]" priority="1000" mode="M5"><svrl:fired-rule xmlns:svrl="http://purl.oclc.org/dsdl/svrl" context="mets:structMap[translate(@TYPE, 'physical', 'PHYSICAL') = 'PHYSICAL'][not(preceding-sibling::mets:structMap[translate(@TYPE, 'physical', 'PHYSICAL') = 'PHYSICAL'])] | mets:structMap[not(@TYPE)][not(preceding-sibling::mets:structMap[not(@TYPE)])][not(../mets:structMap[translate(@TYPE, 'physical', 'PHYSICAL') = 'PHYSICAL'])]"/>

		<!--ASSERT -->
<axsl:choose><axsl:when test="not(mets:div/mets:div/mets:div)"/><axsl:otherwise><svrl:failed-assert xmlns:svrl="http://purl.oclc.org/dsdl/svrl" xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:schold="http://www.ascc.net/xml/schematron" test="not(mets:div/mets:div/mets:div)"><axsl:attribute name="id">E_NOT_FLAT</axsl:attribute><axsl:attribute name="location"><axsl:apply-templates select="." mode="schematron-get-full-path"/></axsl:attribute><svrl:text>
        The physical structMap nests divs more than one level below the root; the EPrints tier
        only covers a flat root-plus-file-divs shape.
      </svrl:text></svrl:failed-assert></axsl:otherwise></axsl:choose>

		<!--ASSERT -->
<axsl:choose><axsl:when test="not(mets:div/mets:div[count(mets:fptr) != 1])"/><axsl:otherwise><svrl:failed-assert xmlns:svrl="http://purl.oclc.org/dsdl/svrl" xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:schold="http://www.ascc.net/xml/schematron" test="not(mets:div/mets:div[count(mets:fptr) != 1])"><axsl:attribute name="id">E_ITEM_ONE_FPTR</axsl:attribute><axsl:attribute name="location"><axsl:apply-templates select="." mode="schematron-get-full-path"/></axsl:attribute><svrl:text>
        A file div must carry exactly one fptr.
      </svrl:text></svrl:failed-assert></axsl:otherwise></axsl:choose><axsl:apply-templates select="@*|*" mode="M5"/></axsl:template><axsl:template match="text()" priority="-1" mode="M5"/><axsl:template match="@*|node()" priority="-2" mode="M5"><axsl:apply-templates select="@*|*" mode="M5"/></axsl:template>

<!--PATTERN filesec-->


	<!--RULE -->
<axsl:template match="mets:fileSec/mets:fileGrp/mets:file" priority="1001" mode="M6"><svrl:fired-rule xmlns:svrl="http://purl.oclc.org/dsdl/svrl" context="mets:fileSec/mets:fileGrp/mets:file"/>

		<!--ASSERT -->
<axsl:choose><axsl:when test="starts-with(mets:FLocat/@xlink:href, 'objects/')"/><axsl:otherwise><svrl:failed-assert xmlns:svrl="http://purl.oclc.org/dsdl/svrl" xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:schold="http://www.ascc.net/xml/schematron" test="starts-with(mets:FLocat/@xlink:href, 'objects/')"><axsl:attribute name="id">E_HREF_UNDER_OBJECTS</axsl:attribute><axsl:attribute name="location"><axsl:apply-templates select="." mode="schematron-get-full-path"/></axsl:attribute><svrl:text>
        A file's href is not under objects/; the EPrints tier's implied objects container
        requires every file path to sit beneath it.
      </svrl:text></svrl:failed-assert></axsl:otherwise></axsl:choose>

		<!--ASSERT -->
<axsl:choose><axsl:when test="key('md-by-id', @ADMID)//premis:fixity[translate(normalize-space(premis:messageDigestAlgorithm), 'SHA-', 'sha') = 'sha256'][normalize-space(premis:messageDigest) != '']"/><axsl:otherwise><svrl:failed-assert xmlns:svrl="http://purl.oclc.org/dsdl/svrl" xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:schold="http://www.ascc.net/xml/schematron" test="key('md-by-id', @ADMID)//premis:fixity[translate(normalize-space(premis:messageDigestAlgorithm), 'SHA-', 'sha') = 'sha256'][normalize-space(premis:messageDigest) != '']"><axsl:attribute name="id">E_SHA256</axsl:attribute><axsl:attribute name="location"><axsl:apply-templates select="." mode="schematron-get-full-path"/></axsl:attribute><svrl:text>
        The file's technical metadata carries no SHA256 digest value, which every import job -
        and therefore every edit-and-preserve - requires.
      </svrl:text></svrl:failed-assert></axsl:otherwise></axsl:choose><axsl:apply-templates select="@*|*" mode="M6"/></axsl:template>

	<!--RULE -->
<axsl:template match="mets:fileSec" priority="1000" mode="M6"><svrl:fired-rule xmlns:svrl="http://purl.oclc.org/dsdl/svrl" context="mets:fileSec"/>

		<!--ASSERT -->
<axsl:choose><axsl:when test="not(mets:fileGrp[@USE != current()/mets:fileGrp[1]/@USE])                         and not(mets:fileGrp[@USE] and mets:fileGrp[not(@USE)])"/><axsl:otherwise><svrl:failed-assert xmlns:svrl="http://purl.oclc.org/dsdl/svrl" xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:schold="http://www.ascc.net/xml/schematron" test="not(mets:fileGrp[@USE != current()/mets:fileGrp[1]/@USE]) and not(mets:fileGrp[@USE] and mets:fileGrp[not(@USE)])"><axsl:attribute name="id">E_MIXED_FILEGRP_USE</axsl:attribute><axsl:attribute name="location"><axsl:apply-templates select="." mode="schematron-get-full-path"/></axsl:attribute><svrl:text>
        The fileSec mixes fileGrp USE values (or mixes groups with and without USE); which
        group holds the content is ambiguous.
      </svrl:text></svrl:failed-assert></axsl:otherwise></axsl:choose><axsl:apply-templates select="@*|*" mode="M6"/></axsl:template><axsl:template match="text()" priority="-1" mode="M6"/><axsl:template match="@*|node()" priority="-2" mode="M6"><axsl:apply-templates select="@*|*" mode="M6"/></axsl:template></axsl:stylesheet>