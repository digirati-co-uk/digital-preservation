using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DigitalPreservation.Mets.Conformance;

/// <summary>
/// The editability judge: given a METS document, which 02e tier does it reach, and what would
/// saving do to it?
///
/// This is the .NET half of a twin implementation - the Python half is src/mets-editability/ -
/// and both implement src/mets-editability/CONTRACT.md, written from that spec rather than from
/// each other. The XML-visible tier rules run from the shared Schematron; this class contributes
/// what XPath 1.0 cannot express (path resolution and uniqueness, the deposit-relative guard,
/// NCName legality via <see cref="MetsIds.IsValidId"/> - the same XmlConvert authority the
/// platform enforces) and assembles the verdict.
///
/// The judge judges the document. Policy the document cannot show - the living-external-editor
/// principle that keeps Goobi read-only whatever its shape - is the platform's overlay.
/// </summary>
public static class EditabilityJudge
{
    private static readonly XNamespace MetsNs = "http://www.loc.gov/METS/";
    private static readonly XNamespace XlinkNs = "http://www.w3.org/1999/xlink";
    private static readonly XNamespace PremisNs = "http://www.loc.gov/premis/v3";
    private static readonly XNamespace ModsNs = "http://www.loc.gov/mods/v3";

    private static readonly Regex UriScheme = new("^[A-Za-z][A-Za-z0-9+.-]*:", RegexOptions.Compiled);

    private static readonly HashSet<string> Blockers =
    [
        "PARSE_FAILED", "NO_PHYSICAL_STRUCTMAP", "NO_ROOT_DIV", "NO_FILES", "FILEID_UNRESOLVED",
        "FILE_NO_HREF", "HREF_NOT_DEPOSIT_RELATIVE", "DUPLICATE_PATH", "DUPLICATE_ID"
    ];

    public static Judgement JudgeFile(string path)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(path);
        }
        catch (Exception bad) when (bad is System.Xml.XmlException or IOException)
        {
            return new Judgement
            {
                Verdict = Verdicts.NotEditable,
                Reasons = { new Finding("PARSE_FAILED", bad.Message) }
            };
        }
        return Judge(document);
    }

    public static Judgement Judge(XDocument document)
    {
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

        var reasons = new List<Finding>();
        var assumptions = new List<Finding>();
        var notes = new List<Finding>();

        var structMap = ChoosePhysicalStructMap(mets, assumptions, notes, reasons);
        if (structMap == null)
        {
            return new Judgement { Verdict = Verdicts.NotEditable, Reasons = reasons };
        }

        CheckDuplicateIds(mets, reasons);
        var legacyIds = DeclaredIds(mets).Where(id => !MetsIds.IsValidId(id)).ToList();
        if (legacyIds.Count > 0)
        {
            notes.Add(new Finding("LEGACY_IDS",
                $"{legacyIds.Count} declared ID(s) are not legal NCNames, e.g. '{legacyIds[0]}'"));
        }

        var filesById = FileIndex(mets);
        var resolved = Walk(structMap, filesById, reasons, notes);
        var unwrappedMdWraps = QuirkNotes(mets, notes);

        if (resolved.FileCount == 0 && !reasons.Any(f => Blockers.Contains(f.Code)))
        {
            // A verdict must carry its reasons. The platform's own freshly-created deposit
            // skeleton is this class: structure but no files yet.
            reasons.Add(new Finding("NO_FILES", "the physical structMap references no files"));
        }

        var navigable = resolved.FileCount > 0 && !reasons.Any(f => Blockers.Contains(f.Code));

        if (!navigable)
        {
            return new Judgement
            {
                Verdict = Verdicts.NotEditable, FileCount = resolved.FileCount,
                Reasons = reasons, Assumptions = assumptions, Notes = notes
            };
        }

        // The common rules guard the linkage every edit operation relies on - fptrs and areas
        // in EVERY structMap (logical included), smLink ends - so they gate both tiers: the
        // platform cannot safely change what it cannot resolve.
        var commonFailures = SchematronRunner.Failures("common", mets);
        var platformFailures = SchematronRunner.Failures("platform-tier", mets);
        if (commonFailures.Count == 0 && platformFailures.Count == 0)
        {
            return new Judgement
            {
                Verdict = Verdicts.Editable, FileCount = resolved.FileCount,
                Assumptions = assumptions, Notes = notes
            };
        }

        var eprintsFailures = SchematronRunner.Failures("eprints-tier", mets);
        if (commonFailures.Count == 0 && eprintsFailures.Count == 0 && legacyIds.Count == 0)
        {
            AddEprintsAssumptions(resolved, assumptions);
            return new Judgement
            {
                Verdict = Verdicts.EditableWithNormalisation, FileCount = resolved.FileCount,
                Assumptions = assumptions, Notes = notes,
                Mutations = Mutations(structMap, mets, resolved, unwrappedMdWraps)
            };
        }

        if (commonFailures.Count == 0 && eprintsFailures.Count == 0 && legacyIds.Count > 0)
        {
            reasons.Add(new Finding("INVALID_IDS",
                "the document matches the EPrints tier but declares IDs that are not legal " +
                "NCNames; it needs the #188 normalisation, which this tier does not perform"));
        }

        foreach (var (code, message) in Distinct(commonFailures)
                     .Concat(Distinct(platformFailures)).Concat(Distinct(eprintsFailures)))
        {
            reasons.Add(new Finding(code, message));
        }
        return new Judgement
        {
            Verdict = Verdicts.NavigableReadOnly, FileCount = resolved.FileCount,
            Reasons = reasons, Assumptions = assumptions, Notes = notes
        };
    }

    private sealed class Resolved
    {
        public int FileCount;
        public int UntypedFileDivs;
        public int FileDivs;
        public readonly HashSet<XElement> ReferencedGroups = [];

        /// <summary>Distinct mets:file IDs already resolved - a file referenced twice (two
        /// divs, or a whole-file fptr plus an area on the same file) is one file, counted and
        /// path-checked once. DUPLICATE_PATH means two FILES claiming one path, never one file
        /// referenced twice.</summary>
        public readonly HashSet<string> SeenFileIds = new(StringComparer.Ordinal);
    }

    private static XElement? ChoosePhysicalStructMap(
        XElement mets, List<Finding> assumptions, List<Finding> notes, List<Finding> reasons)
    {
        var structMaps = mets.Elements(MetsNs + "structMap").ToList();
        var explicitlyPhysical = structMaps
            .Where(sm => string.Equals((string?)sm.Attribute("TYPE"), "physical",
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        var untyped = structMaps.Where(sm => sm.Attribute("TYPE") == null).ToList();

        List<XElement> chosen;
        string category;
        if (explicitlyPhysical.Count > 0)
        {
            (chosen, category) = (explicitlyPhysical, "explicitly physical");
            var spelling = (string?)explicitlyPhysical[0].Attribute("TYPE");
            if (spelling != "PHYSICAL")
            {
                assumptions.Add(new Finding("CASE_INSENSITIVE_STRUCTMAP_TYPE",
                    $"structMap TYPE='{spelling}' read as physical case-insensitively"));
            }
        }
        else if (untyped.Count > 0)
        {
            (chosen, category) = (untyped, "untyped");
            assumptions.Add(new Finding("UNTYPED_STRUCTMAP_ASSUMED_PHYSICAL",
                "the structMap has no TYPE and is assumed physical"));
        }
        else
        {
            reasons.Add(new Finding("NO_PHYSICAL_STRUCTMAP",
                "no structMap is physical or can be assumed physical (untyped)"));
            return null;
        }

        if (chosen.Count > 1)
        {
            notes.Add(new Finding("MULTIPLE_PHYSICAL_CANDIDATES",
                $"{chosen.Count} {category} structMaps; the first is judged"));
        }
        return chosen[0];
    }

    private static List<string> DeclaredIds(XElement mets) =>
        mets.DescendantsAndSelf()
            .Select(element => (string?)element.Attribute("ID"))
            .Where(id => id != null)
            .Select(id => id!)
            .ToList();

    private static void CheckDuplicateIds(XElement mets, List<Finding> reasons)
    {
        var duplicates = DeclaredIds(mets)
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        if (duplicates.Count > 0)
        {
            reasons.Add(new Finding("DUPLICATE_ID",
                $"{duplicates.Count} ID(s) declared more than once, e.g. '{duplicates[0]}'"));
        }
    }

    private static Dictionary<string, XElement> FileIndex(XElement mets)
    {
        var index = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var file in mets.Elements(MetsNs + "fileSec")
                     .Descendants(MetsNs + "file"))
        {
            var id = (string?)file.Attribute("ID");
            if (id != null)
            {
                index.TryAdd(id, file);
            }
        }
        return index;
    }

    private static Resolved Walk(XElement structMap, Dictionary<string, XElement> filesById,
        List<Finding> reasons, List<Finding> notes)
    {
        var resolved = new Resolved();
        var rootDiv = structMap.Element(MetsNs + "div");
        if (rootDiv == null)
        {
            reasons.Add(new Finding("NO_ROOT_DIV", "the physical structMap has no div"));
            return resolved;
        }

        var directoriesWithoutAdmid = 0;
        var pathsSeen = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var div in rootDiv.DescendantsAndSelf(MetsNs + "div"))
        {
            var divType = (string?)div.Attribute("TYPE");
            var fptrs = div.Elements(MetsNs + "fptr").ToList();
            if (div != rootDiv
                && string.Equals(divType, "directory", StringComparison.OrdinalIgnoreCase)
                && div.Attribute("ADMID") == null)
            {
                directoriesWithoutAdmid++;
            }
            if (fptrs.Count > 0)
            {
                resolved.FileDivs++;
                if (divType == null)
                {
                    resolved.UntypedFileDivs++;
                }
            }
            foreach (var fileId in fptrs.SelectMany(FptrFileIds))
            {
                ResolveOne(fileId, filesById, pathsSeen, resolved, reasons);
            }
        }

        if (directoriesWithoutAdmid > 0)
        {
            notes.Add(new Finding("DIRECTORY_DIV_NO_ADMID",
                $"{directoriesWithoutAdmid} directory div(s) have no ADMID, so their own paths " +
                "cannot be anchored in premis:originalName"));
        }

        var duplicatePaths = pathsSeen.Where(pair => pair.Value > 1)
            .Select(pair => pair.Key)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        if (duplicatePaths.Count > 0)
        {
            reasons.Add(new Finding("DUPLICATE_PATH",
                $"{duplicatePaths.Count} path(s) resolved from more than one file, " +
                $"e.g. '{duplicatePaths[0]}'"));
        }
        return resolved;
    }

    private static IEnumerable<string> FptrFileIds(XElement fptr)
    {
        var own = (string?)fptr.Attribute("FILEID");
        if (own != null)
        {
            yield return own;
        }
        foreach (var area in fptr.Descendants(MetsNs + "area"))
        {
            var areaFileId = (string?)area.Attribute("FILEID");
            if (areaFileId != null)
            {
                yield return areaFileId;
            }
        }
    }

    private static void ResolveOne(string fileId, Dictionary<string, XElement> filesById,
        Dictionary<string, int> pathsSeen, Resolved resolved, List<Finding> reasons)
    {
        if (!filesById.TryGetValue(fileId, out var file))
        {
            AppendOnce(reasons, "FILEID_UNRESOLVED",
                $"an fptr references FILEID '{fileId}' that no mets:file declares");
            return;
        }
        if (!resolved.SeenFileIds.Add(fileId))
        {
            return;
        }
        resolved.FileCount++;
        var group = file.Ancestors(MetsNs + "fileGrp").FirstOrDefault();
        if (group != null)
        {
            resolved.ReferencedGroups.Add(group);
        }

        var href = (string?)file.Element(MetsNs + "FLocat")?.Attribute(XlinkNs + "href");
        if (string.IsNullOrEmpty(href))
        {
            AppendOnce(reasons, "FILE_NO_HREF", $"file '{fileId}' has no FLocat href");
            return;
        }
        var forward = href.Replace('\\', '/');
        if (UriScheme.IsMatch(href) || href.StartsWith('/') || href.StartsWith('\\')
            || forward.Split('/').Contains(".."))
        {
            AppendOnce(reasons, "HREF_NOT_DEPOSIT_RELATIVE",
                $"file '{fileId}' href '{href}' is not a relative path within the deposit");
            return;
        }
        var normalised = Normalise(forward);
        pathsSeen[normalised] = pathsSeen.GetValueOrDefault(normalised) + 1;
    }

    /// <summary>Collapse '.' segments and empty segments, the same normalisation the Python
    /// judge gets from posixpath.normpath ('..' segments were rejected above).</summary>
    private static string Normalise(string path) =>
        string.Join('/', path.Split('/').Where(part => part.Length > 0 && part != "."));

    private static void AppendOnce(List<Finding> findings, string code, string message)
    {
        var existing = findings.FirstOrDefault(f => f.Code == code);
        if (existing == null)
        {
            findings.Add(new Finding(code, message));
            return;
        }
        var match = Regex.Match(existing.Message, @"^(.*) \[x(\d+)]$");
        var newMessage = match.Success
            ? $"{match.Groups[1].Value} [x{int.Parse(match.Groups[2].Value) + 1}]"
            : $"{existing.Message} [x2]";
        findings[findings.IndexOf(existing)] = existing with { Message = newMessage };
    }

    private static IEnumerable<(string Code, string Message)> Distinct(
        List<(string Code, string Message)> failures) =>
        failures.GroupBy(failure => failure.Code)
            .Select(group => (group.Key, group.Count() == 1
                ? group.First().Message
                : $"{group.First().Message} [x{group.Count()}]"));

    private static void AddEprintsAssumptions(Resolved resolved, List<Finding> assumptions)
    {
        if (resolved.UntypedFileDivs > 0)
        {
            assumptions.Add(new Finding("UNTYPED_DIV_ASSUMED_ITEM",
                $"{resolved.UntypedFileDivs} untyped div(s) carrying an fptr read as Items"));
        }
        assumptions.Add(new Finding("IMPLIED_OBJECTS_DIV",
            "no div declares the objects directory; it is implied by every file path sitting " +
            "under objects/"));
    }

    /// <summary>
    /// Corpus quirks reported for every document, whatever the verdict. Returns the count of
    /// mdWrap elements needing the xmlData repair, which the EPrints tier turns into a mutation.
    /// </summary>
    private static int QuirkNotes(XElement mets, List<Finding> notes)
    {
        var foreignStorage = mets.Descendants(PremisNs + "storage")
            .Count(storage => storage.Descendants(PremisNs + "storageMedium")
                .All(medium => medium.Value != Constants.MetsCreatorAgent));
        if (foreignStorage > 0)
        {
            notes.Add(new Finding("FOREIGN_STORAGE_LOCATION",
                $"{foreignStorage} premis:storage assertion(s) belong to another system (no " +
                "platform storageMedium); the editing stack ignores them, and #236 tracks " +
                "making the parser do the same"));
        }

        if (mets.Descendants(MetsNs + "dmdSec")
            .SelectMany(dmd => dmd.Descendants(MetsNs + "recordInfo")).Any())
        {
            notes.Add(new Finding("METS_NAMESPACE_RECORD_INFO",
                "record identifiers are declared in the METS namespace (an EPrints quirk); " +
                "invisible to the platform's parser until #237"));
        }

        var foreignDmdSecs = ForeignDmdSecs(mets);
        if (foreignDmdSecs > 0)
        {
            notes.Add(new Finding("FOREIGN_DMDSEC",
                $"{foreignDmdSecs} referenced dmdSec(s) claim MODS but hold no mods:mods record; " +
                "the platform never edits these - an edit on such a div creates a platform dmdSec " +
                "and appends its ID to the div's DMDID, leaving the original untouched"));
        }

        var unwrapped = UnwrappedMdWraps(mets);
        if (unwrapped > 0)
        {
            notes.Add(new Finding("NO_XMLDATA_WRAPPER",
                $"{unwrapped} mdWrap(s) hold their payload directly, without the mets:xmlData " +
                "element the schema requires (an EPrints quirk); a save wraps them - the payload " +
                "itself is preserved verbatim"));
        }
        return unwrapped;
    }

    /// <summary>
    /// Referenced dmdSecs claiming MODS (mdWrap MDTYPE="MODS") without a mods:mods record - the
    /// EPrints root dmdSec shape. DMDID is IDREFS, so the whole value is tried first (a legacy
    /// platform ID can contain spaces) and then each token; a DMDID resolving to nothing is
    /// IGNORED, because dangling DMDIDs are by design in the platform's own skeleton.
    /// </summary>
    private static int ForeignDmdSecs(XElement mets)
    {
        var dmdSecs = mets.Elements(MetsNs + "dmdSec")
            .Where(dmd => dmd.Attribute("ID") != null)
            .ToDictionary(dmd => (string)dmd.Attribute("ID")!, dmd => dmd, StringComparer.Ordinal);
        var foreign = new HashSet<string>(StringComparer.Ordinal);
        foreach (var div in mets.Descendants(MetsNs + "div"))
        {
            var dmdId = (string?)div.Attribute("DMDID");
            if (string.IsNullOrEmpty(dmdId))
            {
                continue;
            }
            List<string> candidates = dmdSecs.ContainsKey(dmdId)
                ? [dmdId]
                : dmdId.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Where(dmdSecs.ContainsKey).ToList();
            foreach (var candidate in candidates)
            {
                var mdWrap = dmdSecs[candidate].Element(MetsNs + "mdWrap");
                if (mdWrap != null && (string?)mdWrap.Attribute("MDTYPE") == "MODS"
                    && !mdWrap.Descendants(ModsNs + "mods").Any())
                {
                    foreign.Add(candidate);
                }
            }
        }
        return foreign.Count;
    }

    /// <summary>mdWraps whose payload sits directly inside them rather than in binData/xmlData.</summary>
    private static int UnwrappedMdWraps(XElement mets) =>
        mets.Descendants(MetsNs + "mdWrap")
            .Count(mdWrap => mdWrap.Elements()
                .Any(child => child.Name != MetsNs + "binData" && child.Name != MetsNs + "xmlData"));

    private static List<string> Mutations(
        XElement structMap, XElement mets, Resolved resolved, int unwrappedMdWraps)
    {
        var mutations = new List<string>();
        if ((string?)structMap.Attribute("TYPE") != "PHYSICAL")
        {
            mutations.Add("set TYPE=\"PHYSICAL\" on the structMap");
        }
        var rootDiv = structMap.Element(MetsNs + "div");
        if (rootDiv?.Attribute("TYPE") == null)
        {
            mutations.Add("set TYPE=\"Directory\" on the root div");
        }
        if (resolved.UntypedFileDivs > 0)
        {
            mutations.Add($"set TYPE=\"Item\" on {resolved.UntypedFileDivs} file div(s)");
        }
        mutations.Add(
            "materialise the objects Directory div (amdSec/techMD with premis:originalName) " +
            $"and re-parent {resolved.FileDivs} file div(s) under it");
        if (resolved.ReferencedGroups.Count > 1
            || resolved.ReferencedGroups.Any(g => (string?)g.Attribute("USE") != "OBJECTS"))
        {
            mutations.Add(
                $"consolidate {resolved.ReferencedGroups.Count} fileGrp(s) into one " +
                "USE=\"OBJECTS\" group");
        }
        if (unwrappedMdWraps > 0)
        {
            mutations.Add(
                $"wrap the payload of {unwrappedMdWraps} mdWrap(s) in the mets:xmlData element " +
                "the schema requires");
        }
        var agents = mets.Elements(MetsNs + "metsHdr")
            .SelectMany(header => header.Elements(MetsNs + "agent"))
            .Select(agent => agent.Element(MetsNs + "name")?.Value);
        if (!agents.Contains(Constants.MetsCreatorAgent))
        {
            mutations.Add("append the platform agent to metsHdr");
        }
        return mutations;
    }
}
