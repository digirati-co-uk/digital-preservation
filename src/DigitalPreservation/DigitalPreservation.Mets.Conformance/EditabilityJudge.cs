using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace DigitalPreservation.Mets.Conformance;

/// <summary>
/// The editability judge: given a METS document, which tier does it reach, and what would saving
/// do to it?
///
/// This is the .NET half of a twin implementation - the Python half is src/mets-editability/ -
/// and both implement src/mets-editability/CONTRACT.md, function for function. The XML-visible
/// tier rules run as the compiled Schematron; this class contributes what XPath 1.0 cannot
/// express - reading the document the way the platform's parser does, path checks, NCName
/// legality via <see cref="MetsIds.IsValidId"/> (the XmlConvert authority the platform enforces)
/// - and assembles the verdict.
///
/// The judge judges the document. Policy the document cannot show - the living-external-editor
/// principle that keeps Goobi read-only whatever its shape - is the platform's overlay.
/// </summary>
public static class EditabilityJudge
{
    // The parser's namespaces (XNames), so the judge and the parser cannot disagree on them.
    private static readonly XNamespace MetsNs = XNames.mets;
    private static readonly XNamespace XlinkNs = XNames.xlink;
    private static readonly XNamespace PremisNs = XNames.premis;
    private static readonly XNamespace ModsNs = XNames.mods;

    /// <summary>The LABEL the platform writes on its root div
    /// (WorkingDirectory.DefaultRootName).</summary>
    private const string RootLabel = "__ROOT";

    /// <summary>Deeper documents are refused before anything walks them: XslCompiledTransform
    /// recurses with the tree, and a deep enough document overflows its stack - an exception no
    /// caller can catch.</summary>
    public const int MaxDepth = 256;

    /// <summary>Any of these means the platform cannot open the document: not-editable.</summary>
    private static readonly HashSet<string> Blockers =
    [
        "PARSE_FAILED", "DOCTYPE_DECLARED", "TOO_DEEP", "SEVERAL_METS_ELEMENTS",
        "NO_PHYSICAL_STRUCTMAP", "NO_ROOT_DIV", "DUPLICATE_ID",
        "FPTR_NO_FILEID", "FILEID_UNRESOLVED", "FILE_REFERENCED_TWICE", "FILE_SEVERAL_FLOCATS",
        "FILE_NO_HREF", "HREF_NOT_DEPOSIT_RELATIVE", "HREF_NOT_NORMALISED", "DUPLICATE_PATH",
        "DIRECTORY_NO_LABEL", "DIRECTORY_PATH_INVALID", "DIRECTORY_LOCATION_NOT_URI",
        "PREMIS_REPEATED", "RECORD_IDENTIFIER_NO_SOURCE", "TIME_VALUE_BLANK"
    ];

    /// <summary>What the parser reads, with SingleOrDefault, from a file's technical metadata -
    /// PREMIS elements, and the platform's own un-namespaced ExifMetadata.</summary>
    private static readonly string[] ReadFromAFileSection =
    [
        "premis:fixity", "premis:size", "premis:originalName", "premis:contentLocation",
        "premis:format", "premis:formatName", "premis:formatRegistryKey", "ExifMetadata"
    ];

    /// <summary>What the parser reads, the same way, from a directory's.</summary>
    private static readonly string[] ReadFromADirectorySection = ["premis:originalName", "premis:contentLocation"];

    private static readonly char[] XmlWhitespace = [' ', '\t', '\r', '\n'];

    // The patterns are anchored and cannot backtrack pathologically, so the timeouts can never
    // be reached; they are here because a regex run against documents we did not write should
    // have a bound regardless (the same stance as MetsIds' pattern).
    private static readonly Regex UriScheme =
        new("^[A-Za-z][A-Za-z0-9+.-]*:", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    /// <summary>A deliberately narrow absolute URI - scheme://host[:port][/path] - that
    /// <see cref="Uri"/> is known to accept. Narrower than what it accepts, never wider.</summary>
    private static readonly Regex AbsoluteUri =
        new(@"^[A-Za-z][A-Za-z0-9+.-]*://[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?" +
            @"(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)*(?::[0-9]{1,4})?(?:/\S*)?\z",
            RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Judge one METS file. Only a local file is read - a URL is not fetched - and anything that
    /// stops it being read or parsed is a judgement (PARSE_FAILED), never a crash.
    /// </summary>
    public static Judgement JudgeFile(string path)
    {
        XDocument document;
        try
        {
            using var stream = File.OpenRead(path);
            var refusal = RefuseWhileReading(stream);
            if (refusal != null)
            {
                return new Judgement { Verdict = Verdicts.NotEditable, Reasons = { refusal } };
            }
            stream.Position = 0;
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null
            });
            document = XDocument.Load(reader);
        }
        catch (Exception bad) when (bad is XmlException or IOException or UnauthorizedAccessException
                                        or ArgumentException or NotSupportedException)
        {
            return new Judgement
            {
                Verdict = Verdicts.NotEditable, Reasons = { new Finding("PARSE_FAILED", bad.Message) }
            };
        }
        return Judge(document);
    }

    public static Judgement Judge(XDocument document)
    {
        var refusal = RefuseParsed(document);
        if (refusal != null)
        {
            return new Judgement { Verdict = Verdicts.NotEditable, Reasons = { refusal } };
        }
        var mets = document.Root?.Name == MetsNs + "mets"
            ? document.Root
            : document.Descendants(MetsNs + "mets").FirstOrDefault();
        if (mets == null)
        {
            return new Judgement
            {
                Verdict = Verdicts.NotEditable,
                Reasons = { new Finding("PARSE_FAILED", "no mets:mets element") }
            };
        }
        var metsElements = document.Descendants(MetsNs + "mets").Count();
        if (metsElements > 1)
        {
            return new Judgement
            {
                Verdict = Verdicts.NotEditable,
                Reasons =
                {
                    new Finding("SEVERAL_METS_ELEMENTS",
                        $"the document holds {metsElements} mets:mets elements; the platform's parser " +
                        "searches the whole document by element name, so it would read the others' " +
                        "structMaps, files and sections as this one's")
                }
            };
        }

        var reasons = new List<Finding>();
        var assumptions = new List<Finding>();
        var notes = new List<Finding>();

        var structMap = ChoosePhysicalStructMap(mets, assumptions, notes, reasons);
        if (structMap == null)
        {
            return new Judgement
            {
                Verdict = Verdicts.NotEditable,
                Reasons = reasons, Assumptions = assumptions, Notes = notes
            };
        }

        CheckDuplicateIds(mets, reasons);
        var legacyIds = DeclaredIds(mets).Where(id => !MetsIds.IsValidId(id)).ToList();
        if (legacyIds.Count > 0)
        {
            notes.Add(new Finding("LEGACY_IDS",
                $"{legacyIds.Count} declared ID(s) are not legal NCNames, e.g. '{legacyIds[0]}'",
                legacyIds.Count));
        }

        var index = new Index(mets);
        var fileCount = Walk(structMap, index, reasons, notes);
        CheckValuesTheParserConverts(mets, index, reasons);
        var unwrappedMdWraps = QuirkNotes(mets, index, notes);

        if (reasons.Any(finding => Blockers.Contains(finding.Code)))
        {
            return new Judgement
            {
                Verdict = Verdicts.NotEditable, FileCount = fileCount,
                Reasons = reasons, Assumptions = assumptions, Notes = notes
            };
        }
        if (fileCount == 0)
        {
            // The platform's own freshly-created deposit skeleton is this: structure, no files yet.
            notes.Add(new Finding("NO_FILES", "the physical structMap references no files yet"));
        }

        // The common rules guard the linkage every edit operation relies on, so they gate both
        // tiers: the platform cannot safely change what it cannot resolve. Shared editable
        // dmdSecs gate the same way, natively (DMDID token-splitting is not safe in XPath 1.0).
        var sharedDmdSecs = SharedEditableDmdSecs(mets, index);
        if (sharedDmdSecs > 0)
        {
            reasons.Add(new Finding("SHARED_DMDSEC",
                $"{sharedDmdSecs} MODS dmdSec(s) are referenced from more than one div; editing " +
                "metadata on one div would silently rewrite the other's", sharedDmdSecs));
        }
        var commonFailures = SchematronRunner.Failures("common", mets);
        var platformFailures = SchematronRunner.Failures("platform-tier", mets);
        if (sharedDmdSecs == 0 && commonFailures.Count == 0 && platformFailures.Count == 0)
        {
            return new Judgement
            {
                Verdict = Verdicts.Editable, FileCount = fileCount,
                Assumptions = assumptions, Notes = notes
            };
        }

        var eprintsFailures = SchematronRunner.Failures("eprints-tier", mets);
        if (sharedDmdSecs == 0 && commonFailures.Count == 0 && eprintsFailures.Count == 0)
        {
            if (legacyIds.Count == 0)
            {
                AddEprintsAssumptions(structMap, assumptions);
                return new Judgement
                {
                    Verdict = Verdicts.EditableWithNormalisation, FileCount = fileCount,
                    Assumptions = assumptions, Notes = notes,
                    Mutations = Mutations(structMap, mets, unwrappedMdWraps)
                };
            }
            reasons.Add(new Finding("INVALID_IDS",
                "the document matches the EPrints tier but declares IDs that are not legal " +
                "NCNames; it needs the #188 normalisation, which this tier does not perform"));
        }

        reasons.AddRange(commonFailures.Concat(platformFailures).Concat(eprintsFailures));
        return new Judgement
        {
            Verdict = Verdicts.NavigableReadOnly, FileCount = fileCount,
            Reasons = reasons, Assumptions = assumptions, Notes = notes
        };
    }

    // --- Refusals before any rule runs --------------------------------------------------------

    /// <summary>A streaming pass that refuses a DTD or excessive depth before the document is
    /// built. The DTD is read (DtdProcessing.Parse) only so that it can be seen and refused; no
    /// entity is expanded, and nothing external is fetched.</summary>
    private static Finding? RefuseWhileReading(Stream stream)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse, XmlResolver = null, CloseInput = false
        });
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.DocumentType)
            {
                return DoctypeDeclared();
            }
            if (reader.NodeType == XmlNodeType.Element && reader.Depth + 1 > MaxDepth)
            {
                return TooDeep();
            }
        }
        return null;
    }

    /// <summary>The same refusals, for a document the caller has already parsed.</summary>
    private static Finding? RefuseParsed(XDocument document)
    {
        if (document.DocumentType != null)
        {
            return DoctypeDeclared();
        }
        var stack = new Stack<(XElement Element, int Depth)>();
        if (document.Root != null)
        {
            stack.Push((document.Root, 1));
        }
        while (stack.Count > 0)
        {
            var (element, depth) = stack.Pop();
            if (depth > MaxDepth)
            {
                return TooDeep();
            }
            foreach (var child in element.Elements())
            {
                stack.Push((child, depth + 1));
            }
        }
        return null;
    }

    private static Finding DoctypeDeclared() =>
        new("DOCTYPE_DECLARED",
            "the document declares a DTD; the judge does not accept one, because XML parsers " +
            "disagree on what its entity declarations expand to");

    private static Finding TooDeep() =>
        new("TOO_DEEP", $"the document nests elements more than {MaxDepth} deep");

    // --- Reading the document the way the platform's parser does ------------------------------

    /// <summary>Whether an attribute value is <paramref name="word"/> ignoring ASCII case. The
    /// parser lowercases TYPE values with ToLowerInvariant, which, for these all-ASCII words,
    /// matches exactly the values that are the word in some ASCII case.</summary>
    private static bool ReadsAs(string? value, string word) =>
        value != null && Ascii.IsValid(value) && value.Equals(word, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The map the platform's parser walks: the first structMap typed physical, otherwise the
    /// first one not typed logical. The judge accepts that choice only if the map is physical or
    /// untyped - the parser would also walk a map typed something else entirely (OTHER, or an
    /// empty TYPE), but nothing says such a map is the physical one.
    /// </summary>
    private static XElement? ChoosePhysicalStructMap(
        XElement mets, List<Finding> assumptions, List<Finding> notes, List<Finding> reasons)
    {
        var structMaps = mets.Elements(MetsNs + "structMap").ToList();
        var physical = structMaps.Where(sm => ReadsAs((string?)sm.Attribute("TYPE"), "physical")).ToList();
        List<XElement> candidates;
        string category;
        if (physical.Count > 0)
        {
            (candidates, category) = (physical, "explicitly physical");
            var spelling = (string?)physical[0].Attribute("TYPE");
            if (spelling != "PHYSICAL")
            {
                assumptions.Add(new Finding("CASE_INSENSITIVE_STRUCTMAP_TYPE",
                    $"structMap TYPE='{spelling}' read as physical case-insensitively"));
            }
        }
        else
        {
            var notLogical = structMaps.Where(sm => !ReadsAs((string?)sm.Attribute("TYPE"), "logical")).ToList();
            if (notLogical.Count == 0)
            {
                reasons.Add(new Finding("NO_PHYSICAL_STRUCTMAP",
                    "no structMap is physical or can be assumed physical (untyped)"));
                return null;
            }
            if (notLogical[0].Attribute("TYPE") != null)
            {
                reasons.Add(new Finding("NO_PHYSICAL_STRUCTMAP",
                    "the platform's parser would walk the structMap with TYPE=" +
                    $"'{(string?)notLogical[0].Attribute("TYPE")}' as the physical one; only a physical or " +
                    "untyped structMap can be judged as physical"));
                return null;
            }
            (candidates, category) = (structMaps.Where(sm => sm.Attribute("TYPE") == null).ToList(), "untyped");
            assumptions.Add(new Finding("UNTYPED_STRUCTMAP_ASSUMED_PHYSICAL",
                "the structMap has no TYPE and is assumed physical"));
        }

        if (candidates.Count > 1)
        {
            notes.Add(new Finding("MULTIPLE_PHYSICAL_CANDIDATES",
                $"{candidates.Count} {category} structMaps; the first is judged", candidates.Count));
        }
        return candidates[0];
    }

    private static List<string> DeclaredIds(XElement mets) =>
        mets.DescendantsAndSelf()
            .Select(element => (string?)element.Attribute("ID"))
            .OfType<string>()
            .ToList();

    private static void CheckDuplicateIds(XElement mets, List<Finding> reasons)
    {
        var duplicates = DeclaredIds(mets)
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicates.Count > 0)
        {
            reasons.Add(new Finding("DUPLICATE_ID",
                $"{duplicates.Count} ID(s) declared more than once, e.g. '{duplicates[0]}'",
                duplicates.Count));
        }
    }

    /// <summary>The lookups by ID the walk needs, built once. The first element wins where an
    /// ID is declared twice - DUPLICATE_ID has already refused such a document.</summary>
    private sealed class Index(XElement mets)
    {
        public readonly Dictionary<string, XElement> Files = ById(
            mets.Elements(MetsNs + "fileSec").Elements(MetsNs + "fileGrp").Descendants(MetsNs + "file"));

        public readonly Dictionary<string, XElement> Sections = ById(
            mets.Descendants().Where(e => e.Name == MetsNs + "techMD" || e.Name == MetsNs + "amdSec"));

        public readonly Dictionary<string, XElement> DmdSecs = ById(mets.Elements(MetsNs + "dmdSec"));
    }

    private static Dictionary<string, XElement> ById(IEnumerable<XElement> elements)
    {
        var index = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var element in elements)
        {
            var id = (string?)element.Attribute("ID");
            if (id != null)
            {
                index.TryAdd(id, element);
            }
        }
        return index;
    }

    /// <summary>
    /// The elements an IDREFS attribute (ADMID, DMDID) names, as the platform's IdRefs.ResolveAll
    /// reads it: the whole value first (a legacy platform ID can contain spaces), otherwise each
    /// XML-whitespace-separated token that names something, each element once.
    /// </summary>
    private static List<XElement> ResolveIdRefs(string value, Dictionary<string, XElement> byId)
    {
        if (byId.TryGetValue(value, out var whole))
        {
            return [whole];
        }
        var resolved = new List<XElement>();
        foreach (var token in value.Split(XmlWhitespace, StringSplitOptions.RemoveEmptyEntries))
        {
            if (byId.TryGetValue(token, out var element) && !resolved.Contains(element))
            {
                resolved.Add(element);
            }
        }
        return resolved;
    }

    private static string Name(XElement element)
    {
        var id = (string?)element.Attribute("ID");
        return id == null ? "(no ID)" : $"'{id}'";
    }

    /// <summary>
    /// Walk the chosen structMap as the platform's parser does - every div, in document order -
    /// refusing whatever would stop the parser loading the document, and resolving every file
    /// the divs point at. Returns the number of distinct files referenced.
    /// </summary>
    private static int Walk(XElement structMap, Index index, List<Finding> reasons, List<Finding> notes)
    {
        if (structMap.Element(MetsNs + "div") == null)
        {
            reasons.Add(new Finding("NO_ROOT_DIV", "the physical structMap has no div"));
            return 0;
        }

        var seenFileIds = new HashSet<string>(StringComparer.Ordinal);
        var pathsSeen = new Dictionary<string, int>(StringComparer.Ordinal);
        var fileSections = new List<XElement>();
        var directoriesWithoutAdmid = 0;

        foreach (var div in structMap.Descendants(MetsNs + "div"))
        {
            if (ReadsAs((string?)div.Attribute("TYPE"), "directory"))
            {
                CheckDirectoryDiv(div, index, reasons);
                if (div.Parent != structMap && div.Attribute("ADMID") == null)
                {
                    directoriesWithoutAdmid++;
                }
            }
            foreach (var fptr in div.Elements(MetsNs + "fptr"))
            {
                ResolveFptr(fptr, div, index, seenFileIds, pathsSeen, fileSections, reasons);
            }
        }

        foreach (var section in fileSections)
        {
            CheckReadOnce(section, ReadFromAFileSection, reasons);
        }

        if (directoriesWithoutAdmid > 0)
        {
            notes.Add(new Finding("DIRECTORY_DIV_NO_ADMID",
                $"{directoriesWithoutAdmid} directory div(s) have no ADMID, so their own paths " +
                "cannot be anchored in premis:originalName", directoriesWithoutAdmid));
        }

        var duplicates = pathsSeen.Where(pair => pair.Value > 1)
            .Select(pair => pair.Key)
            .ToList();
        if (duplicates.Count > 0)
        {
            reasons.Add(new Finding("DUPLICATE_PATH",
                $"{duplicates.Count} path(s) resolved from more than one file, e.g. '{duplicates[0]}'",
                duplicates.Count));
        }
        return seenFileIds.Count;
    }

    /// <summary>A Directory div must have a LABEL; the section its ADMID names supplies its
    /// path.</summary>
    private static void CheckDirectoryDiv(XElement div, Index index, List<Finding> reasons)
    {
        if (string.IsNullOrEmpty((string?)div.Attribute("LABEL")))
        {
            Findings.Add(reasons, "DIRECTORY_NO_LABEL",
                $"directory div {Name(div)} has no LABEL; the platform's parser requires one on " +
                "every directory");
        }
        var admid = (string?)div.Attribute("ADMID");
        if (string.IsNullOrEmpty(admid))
        {
            return;
        }
        foreach (var section in ResolveIdRefs(admid, index.Sections))
        {
            CheckReadOnce(section, ReadFromADirectorySection, reasons);
            foreach (var originalName in section.Descendants(PremisNs + "originalName"))
            {
                if (PathProblem(originalName.Value) != null)
                {
                    Findings.Add(reasons, "DIRECTORY_PATH_INVALID",
                        $"directory div {Name(div)} has premis:originalName '{originalName.Value}', " +
                        "which is not a normalised path within the deposit");
                }
            }
            foreach (var location in section.Descendants(PremisNs + "contentLocation"))
            {
                if (!AbsoluteUri.IsMatch(location.Value))
                {
                    Findings.Add(reasons, "DIRECTORY_LOCATION_NOT_URI",
                        $"directory div {Name(div)} has premis:contentLocation '{location.Value}', " +
                        "which the platform's parser cannot read as an absolute URI");
                }
            }
        }
    }

    private static void ResolveFptr(XElement fptr, XElement div, Index index, HashSet<string> seenFileIds,
        Dictionary<string, int> pathsSeen, List<XElement> fileSections, List<Finding> reasons)
    {
        var fileId = (string?)fptr.Attribute("FILEID");
        if (fileId == null)
        {
            Findings.Add(reasons, "FPTR_NO_FILEID",
                $"an fptr in div {Name(div)} has no FILEID; the platform's parser requires one on " +
                "every fptr in the physical structMap (area-only pointers belong in logical ones)");
            return;
        }
        if (!index.Files.TryGetValue(fileId, out var file))
        {
            Findings.Add(reasons, "FILEID_UNRESOLVED",
                $"an fptr references FILEID '{fileId}' that no mets:file declares");
            return;
        }
        if (seenFileIds.Contains(fileId))
        {
            Findings.Add(reasons, "FILE_REFERENCED_TWICE",
                $"file '{fileId}' is referenced from more than one fptr in the physical " +
                "structMap; the platform's parser would list it twice and fail");
            return;
        }
        seenFileIds.Add(fileId);

        foreach (var admid in new[] { (string?)div.Attribute("ADMID"), (string?)file.Attribute("ADMID") })
        {
            foreach (var section in string.IsNullOrEmpty(admid) ? [] : ResolveIdRefs(admid, index.Sections))
            {
                if (!fileSections.Contains(section))
                {
                    fileSections.Add(section);
                }
            }
        }

        var locations = file.Elements(MetsNs + "FLocat").ToList();
        if (locations.Count > 1)
        {
            Findings.Add(reasons, "FILE_SEVERAL_FLOCATS",
                $"file '{fileId}' has {locations.Count} FLocat elements; the platform's parser " +
                "reads exactly one");
            return;
        }
        var href = locations.Count == 1 ? (string?)locations[0].Attribute(XlinkNs + "href") : null;
        if (string.IsNullOrEmpty(href))
        {
            Findings.Add(reasons, "FILE_NO_HREF", $"file '{fileId}' has no FLocat href");
            return;
        }
        switch (PathProblem(href))
        {
            case "HREF_NOT_DEPOSIT_RELATIVE":
                Findings.Add(reasons, "HREF_NOT_DEPOSIT_RELATIVE",
                    $"file '{fileId}' href '{href}' is not a relative path within the deposit");
                break;
            case "HREF_NOT_NORMALISED":
                Findings.Add(reasons, "HREF_NOT_NORMALISED",
                    $"file '{fileId}' href '{href}' contains an empty or '.' segment, which the " +
                    "platform's path cache does not normalise - the entry would be unreachable");
                break;
            default:
                pathsSeen[href] = pathsSeen.GetValueOrDefault(href) + 1;
                break;
        }
    }

    /// <summary>
    /// What is wrong with a path taken from the document, or null.
    ///
    /// HREF_NOT_DEPOSIT_RELATIVE: a URI scheme, a leading '/', a backslash anywhere, or a segment
    /// that is '..' or holds a separator once percent-decoded - the standing guard, and
    /// everything the parser refuses outright.
    /// HREF_NOT_NORMALISED: an empty or '.' segment, as given or percent-decoded. The platform's
    /// path cache does not normalise paths, so such an entry is real but unreachable - and the
    /// judge must not be more tolerant than the machinery it vouches for.
    /// </summary>
    private static string? PathProblem(string path)
    {
        var segments = path.Split('/');
        var decoded = segments.Select(Uri.UnescapeDataString).ToList();
        if (UriScheme.IsMatch(path) || path.StartsWith('/') || path.Contains('\\')
            || decoded.Any(part => part == ".." || part.Contains('/') || part.Contains('\\')))
        {
            return "HREF_NOT_DEPOSIT_RELATIVE";
        }
        if (segments.Where((segment, i) => segment == "" || decoded[i] == ".").Any())
        {
            return "HREF_NOT_NORMALISED";
        }
        return null;
    }

    /// <summary>The parser reads each of these once from the section, and throws if there are
    /// two.</summary>
    private static void CheckReadOnce(XElement section, string[] names, List<Finding> reasons)
    {
        foreach (var name in names)
        {
            var localName = name.StartsWith("premis:", StringComparison.Ordinal) ? name["premis:".Length..] : null;
            var count = section.Descendants(localName == null ? XName.Get(name) : PremisNs + localName).Count();
            if (count > 1)
            {
                Findings.Add(reasons, "PREMIS_REPEATED",
                    $"section {Name(section)} holds {count} {name} elements; the platform's " +
                    "parser reads exactly one");
            }
        }
    }

    /// <summary>
    /// Values the platform's parser converts without a guard, so that a bad one stops it loading
    /// the document at all. Each check covers at least every value the parser reads, and
    /// sometimes more: a stricter judge is safe, a more tolerant one is not.
    /// </summary>
    private static void CheckValuesTheParserConverts(XElement mets, Index index, List<Finding> reasons)
    {
        foreach (var dmdId in DmdReferents(mets, index).Keys)
        {
            foreach (var identifier in index.DmdSecs[dmdId].Descendants(ModsNs + "recordIdentifier"))
            {
                if (identifier.Attribute("source") == null)
                {
                    Findings.Add(reasons, "RECORD_IDENTIFIER_NO_SOURCE",
                        $"dmdSec '{dmdId}' holds a mods:recordIdentifier without a source " +
                        "attribute, which the platform's parser requires");
                }
            }
        }

        foreach (var area in mets.Descendants(MetsNs + "area"))
        {
            foreach (var attribute in new[] { "BEGIN", "END" })
            {
                var value = (string?)area.Attribute(attribute);
                if (value != null && string.IsNullOrWhiteSpace(value))
                {
                    Findings.Add(reasons, "TIME_VALUE_BLANK",
                        $"an area has a blank {attribute}; the platform's parser fails on a blank " +
                        "time code");
                }
            }
        }
        foreach (var properties in mets.Descendants(PremisNs + "significantProperties"))
        {
            var kind = properties.Element(PremisNs + "significantPropertiesType");
            var value = properties.Element(PremisNs + "significantPropertiesValue");
            if (kind != null && kind.Value == "Duration" && value != null
                && string.IsNullOrWhiteSpace(value.Value))
            {
                Findings.Add(reasons, "TIME_VALUE_BLANK",
                    "a premis:significantProperties Duration is blank; the platform's parser " +
                    "fails on a blank time code");
            }
        }
    }

    // --- Notes and the EPrints tier -----------------------------------------------------------

    private static void AddEprintsAssumptions(XElement structMap, List<Finding> assumptions)
    {
        var rootDiv = structMap.Element(MetsNs + "div")!;
        var fileDivs = rootDiv.Elements(MetsNs + "div").ToList();
        var untyped = fileDivs.Count(div => div.Attribute("TYPE") == null);
        if (untyped > 0)
        {
            assumptions.Add(new Finding("UNTYPED_DIV_ASSUMED_ITEM",
                $"{untyped} untyped div(s) carrying an fptr read as Items", untyped));
        }
        var rootType = (string?)rootDiv.Attribute("TYPE");
        var caseVariants = (rootType != null && rootType != "Directory" ? 1 : 0)
            + fileDivs.Count(div => (string?)div.Attribute("TYPE") is { } type && type != "Item");
        if (caseVariants > 0)
        {
            assumptions.Add(new Finding("CASE_INSENSITIVE_DIV_TYPE",
                $"{caseVariants} div TYPE(s) read as Directory or Item case-insensitively", caseVariants));
        }
        assumptions.Add(new Finding("IMPLIED_OBJECTS_DIV",
            "no div declares the objects directory; it is implied by every file path sitting " +
            "directly under objects/"));
    }

    /// <summary>
    /// Corpus quirks reported for every document, whatever the verdict. Returns the count of
    /// mdWrap elements needing the xmlData repair, which the EPrints tier turns into a mutation.
    /// </summary>
    private static int QuirkNotes(XElement mets, Index index, List<Finding> notes)
    {
        var foreignStorage = mets.Descendants(PremisNs + "storage")
            .Count(storage => storage.Descendants(PremisNs + "storageMedium")
                .All(medium => medium.Value != Constants.MetsCreatorAgent));
        if (foreignStorage > 0)
        {
            notes.Add(new Finding("FOREIGN_STORAGE_LOCATION",
                $"{foreignStorage} premis:storage assertion(s) belong to another system (no " +
                "platform storageMedium); the editing stack ignores them, and #236 tracks making " +
                "the parser do the same", foreignStorage));
        }

        if (mets.Descendants(MetsNs + "dmdSec").Descendants(MetsNs + "recordInfo").Any())
        {
            notes.Add(new Finding("METS_NAMESPACE_RECORD_INFO",
                "record identifiers are declared in the METS namespace (an EPrints quirk); " +
                "invisible to the platform's parser until #237"));
        }

        var foreignDmdSecs = DmdReferents(mets, index).Keys.Count(id => IsForeignDmdSec(index.DmdSecs[id]));
        if (foreignDmdSecs > 0)
        {
            notes.Add(new Finding("FOREIGN_DMDSEC",
                $"{foreignDmdSecs} referenced dmdSec(s) claim MODS but hold no mods:mods record; " +
                "the platform never edits these - an edit on such a div creates a platform dmdSec " +
                "and appends its ID to the div's DMDID, leaving the original untouched",
                foreignDmdSecs));
        }

        var unwrapped = UnwrappedMdWraps(mets);
        if (unwrapped > 0)
        {
            notes.Add(new Finding("NO_XMLDATA_WRAPPER",
                $"{unwrapped} mdWrap(s) hold their payload directly, without the mets:xmlData " +
                "element the schema requires (an EPrints quirk); a save wraps them - the payload " +
                "itself is preserved verbatim", unwrapped));
        }

        var unusualAdmids = new[] { "fileGrp", "area", "stream" }
            .Sum(name => mets.Descendants(MetsNs + name)
                .Count(element => !string.IsNullOrEmpty((string?)element.Attribute("ADMID"))));
        if (unusualAdmids > 0)
        {
            notes.Add(new Finding("ADMID_OUTSIDE_SURVIVAL_INDEX",
                $"{unusualAdmids} ADMID(s) sit on fileGrp/area/stream elements, which the " +
                "platform's deletion reasoning does not consult; sections referenced only from " +
                "there could be swept by an edit", unusualAdmids));
        }
        return unwrapped;
    }

    /// <summary>
    /// How many divs reference each dmdSec. A DMDID resolving to nothing is IGNORED, because
    /// dangling DMDIDs are by design in the platform's own skeleton; a div naming the same
    /// dmdSec twice still counts once.
    /// </summary>
    private static Dictionary<string, int> DmdReferents(XElement mets, Index index)
    {
        var referents = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var div in mets.Descendants(MetsNs + "div"))
        {
            var dmdId = (string?)div.Attribute("DMDID");
            foreach (var dmdSec in string.IsNullOrEmpty(dmdId) ? [] : ResolveIdRefs(dmdId, index.DmdSecs))
            {
                var id = (string)dmdSec.Attribute("ID")!;
                referents[id] = referents.GetValueOrDefault(id) + 1;
            }
        }
        return referents;
    }

    /// <summary>Claims MODS (mdWrap MDTYPE="MODS") but holds no mods:mods - the EPrints root
    /// shape.</summary>
    private static bool IsForeignDmdSec(XElement dmdSec)
    {
        var mdWrap = dmdSec.Element(MetsNs + "mdWrap");
        return mdWrap != null && (string?)mdWrap.Attribute("MDTYPE") == "MODS"
            && !mdWrap.Descendants(ModsNs + "mods").Any();
    }

    /// <summary>
    /// MODS-editable dmdSecs referenced from more than one div. Editing metadata on one such
    /// div rewrites the section in place and silently changes the other div's metadata too -
    /// so a document with one is not safely editable. A shared FOREIGN dmdSec is fine: the
    /// platform never edits those (it appends its own alongside).
    /// </summary>
    private static int SharedEditableDmdSecs(XElement mets, Index index) =>
        DmdReferents(mets, index).Count(pair => pair.Value > 1 && !IsForeignDmdSec(index.DmdSecs[pair.Key]));

    /// <summary>mdWraps whose payload sits directly inside them rather than in
    /// binData/xmlData.</summary>
    private static int UnwrappedMdWraps(XElement mets) =>
        mets.Descendants(MetsNs + "mdWrap")
            .Count(mdWrap => mdWrap.Elements()
                .Any(child => child.Name != MetsNs + "binData" && child.Name != MetsNs + "xmlData"));

    /// <summary>What the first save does to an EPrints-tier document, in order
    /// (CONTRACT.md).</summary>
    private static List<string> Mutations(XElement structMap, XElement mets, int unwrappedMdWraps)
    {
        var mutations = new List<string>();
        var rootDiv = structMap.Element(MetsNs + "div")!;
        var fileDivs = rootDiv.Elements(MetsNs + "div").ToList();
        if ((string?)structMap.Attribute("TYPE") != "PHYSICAL")
        {
            mutations.Add("set TYPE=\"PHYSICAL\" on the structMap");
        }
        if ((string?)rootDiv.Attribute("TYPE") != "Directory")
        {
            mutations.Add("set TYPE=\"Directory\" on the root div");
        }
        if (string.IsNullOrEmpty((string?)rootDiv.Attribute("LABEL")))
        {
            mutations.Add($"set LABEL=\"{RootLabel}\" on the root div");
        }
        var retyped = fileDivs.Count(div => (string?)div.Attribute("TYPE") != "Item");
        if (retyped > 0)
        {
            mutations.Add($"set TYPE=\"Item\" on {retyped} file div(s)");
        }
        mutations.Add(
            "materialise the objects Directory div (amdSec/techMD with premis:originalName) and " +
            $"re-parent {fileDivs.Count} file div(s) under it");
        var groups = mets.Elements(MetsNs + "fileSec").Elements(MetsNs + "fileGrp").ToList();
        if (groups.Count > 1 || groups.Any(group => (string?)group.Attribute("USE") != "OBJECTS"))
        {
            mutations.Add($"consolidate {groups.Count} fileGrp(s) into one USE=\"OBJECTS\" group");
        }
        if (unwrappedMdWraps > 0)
        {
            mutations.Add(
                $"wrap the payload of {unwrappedMdWraps} mdWrap(s) in the mets:xmlData element " +
                "the schema requires");
        }
        var agents = mets.Elements(MetsNs + "metsHdr").Elements(MetsNs + "agent")
            .Elements(MetsNs + "name").Select(name => name.Value);
        if (!agents.Contains(Constants.MetsCreatorAgent))
        {
            mutations.Add("append the platform agent to metsHdr");
        }
        return mutations;
    }
}
