using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using DigitalPreservation.Mets;
using FluentAssertions;
using Xunit.Abstractions;

namespace XmlGen.Tests.Experimental.Parsing;

/// <summary>
/// A read-only survey of real METS files: what does the platform's path cache make of each one,
/// and where it makes nothing, why?
/// </summary>
/// <remarks>
/// Two questions this exists to answer, both of which need real documents rather than fixtures.
/// How much of an existing corpus would pass the navigability half of a conformance check
/// (issue #223) - and of the documents that fail, how many fail only because they do not follow
/// conventions that METS itself makes optional?
/// <para>
/// It reads and reports; it writes nothing and mutates nothing on disk. Point it at a directory
/// with <c>METS_SURVEY_PATH</c>. Manual because it needs a corpus that is not in the repository.
/// </para>
/// </remarks>
public class SurveyMetsCorpus(ITestOutputHelper output)
{
    private const string PathVariable = "METS_SURVEY_PATH";

    [Fact, Trait("Category", "Manual")]
    public void Survey()
    {
        var root = Environment.GetEnvironmentVariable(PathVariable);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            output.WriteLine($"Set {PathVariable} to a directory of METS files. Nothing surveyed.");
            return;
        }

        var verdicts = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories).Order())
        {
            foreach (var (mets, label) in ReadMetsDocuments(file))
            {
                verdicts.Add(Report(mets, label));
            }
        }

        // The one thing a survey can assert: if a corpus was pointed at, it was read. An empty
        // result otherwise looks exactly like a corpus in which nothing was worth reporting,
        // when in fact it usually means the path is wrong or holds no METS.
        verdicts.Should().NotBeEmpty($"{PathVariable} ('{root}') should hold METS files to survey");

        output.WriteLine("");
        output.WriteLine($"=== {verdicts.Count} document(s) ===");
        foreach (var group in verdicts.GroupBy(v => v).OrderByDescending(g => g.Count()))
        {
            output.WriteLine($"  {group.Count(),6}  {group.Key}");
        }
    }

    /// <summary>
    /// The METS documents in a file. Usually one, but an EPrints export wraps several in a
    /// <c>mets-objects</c> element, and a document we cannot deserialise is reported rather than
    /// thrown - a survey that stops at the first unreadable file surveys nothing.
    /// </summary>
    private static IEnumerable<(DigitalPreservation.XmlGen.Mets.Mets? Mets, string Label)> ReadMetsDocuments(string file)
    {
        var name = Path.GetFileName(file);
        XDocument? document = null;
        string? loadError = null;
        try
        {
            document = XDocument.Load(file);
        }
        catch (Exception e)
        {
            loadError = e.Message;
        }

        if (document == null)
        {
            yield return (null, $"{name}: not well-formed XML ({loadError})");
            yield break;
        }

        var metsElements = document.Descendants(XName.Get("mets", "http://www.loc.gov/METS/")).ToList();
        if (metsElements.Count == 0)
        {
            yield return (null, $"{name}: contains no mets:mets element");
            yield break;
        }

        var serializer = new XmlSerializer(typeof(DigitalPreservation.XmlGen.Mets.Mets));
        for (var i = 0; i < metsElements.Count; i++)
        {
            var label = metsElements.Count == 1 ? name : $"{name}[{i}]";
            DigitalPreservation.XmlGen.Mets.Mets? mets = null;
            string? deserialiseError = null;
            try
            {
                using var reader = metsElements[i].CreateReader();
                mets = (DigitalPreservation.XmlGen.Mets.Mets?)serializer.Deserialize(reader);
            }
            catch (Exception e)
            {
                deserialiseError = e.Message;
            }

            yield return mets == null
                ? (null, $"{label}: does not deserialise ({deserialiseError})")
                : (mets, label);
        }
    }

    private string Report(DigitalPreservation.XmlGen.Mets.Mets? mets, string label)
    {
        if (mets == null)
        {
            output.WriteLine(label);
            return "unreadable";
        }

        var asIs = Populate(mets);

        output.WriteLine($"{label}");
        output.WriteLine($"    agent      : {mets.MetsHdr?.Agent?.FirstOrDefault()?.Name ?? "(none)"}");
        output.WriteLine($"    structMaps : {Describe(mets.StructMap.Select(sm => sm.Type))}");
        output.WriteLine($"    fileGrps   : {Describe(mets.FileSec?.FileGrp.Select(g => g.Use) ?? [])}");
        output.WriteLine($"    cached     : {asIs.Entries}, diagnostics: {asIs.Diagnostics.Count}");
        foreach (var bucket in Bucket(asIs.Diagnostics)) output.WriteLine($"        {bucket}");

        if (asIs.Diagnostics.Count == 0)
        {
            return "conforms: navigable as it stands";
        }

        // The what-if: a structMap the cache does not accept as physical stops it before it looks
        // at a single div, so a document can fail for that alone and hide whatever else is wrong.
        // Two shapes reach here - no TYPE at all (EPrints), and a TYPE that differs only in case
        // (Archivematica writes "physical"). Typing it in memory shows what is really underneath;
        // nothing is written back.
        var soleStructMap = mets.StructMap.Count == 1 ? mets.StructMap[0] : null;
        var acceptableIfTyped = soleStructMap != null
                                && (string.IsNullOrEmpty(soleStructMap.Type)
                                    || soleStructMap.Type.Equals(Constants.Physical, StringComparison.OrdinalIgnoreCase));
        if (acceptableIfTyped)
        {
            mets.StructMap[0].Type = Constants.Physical;
            var typed = Populate(mets);
            output.WriteLine($"    if typed   : {typed.Entries}, diagnostics: {typed.Diagnostics.Count}");
            foreach (var bucket in Bucket(typed.Diagnostics)) output.WriteLine($"        {bucket}");

            if (typed.Diagnostics.Count == 0) return "blocked only by the structMap TYPE";
            if (typed.Diagnostics.All(IsUntypedDiv)) return "blocked only by structMap and div typing";
            return "blocked by more than typing";
        }

        return asIs.Diagnostics.All(IsUntypedDiv)
            ? "blocked only by div typing"
            : "blocked by more than typing";
    }

    private static (int Entries, List<string> Diagnostics) Populate(DigitalPreservation.XmlGen.Mets.Mets mets)
    {
        var fullMets = new FullMets { Mets = mets, Uri = new Uri("file:///survey/mets.xml") };
        var diagnostics = MetsCache.Populate(fullMets);
        return (fullMets.PhysicalDivsByPath.Count, diagnostics);
    }

    /// <summary>A div carrying no TYPE - legal METS, but not a shape the cache recognises.</summary>
    private static bool IsUntypedDiv(string diagnostic) => diagnostic.Contains("unrecognised TYPE");

    /// <summary>
    /// Diagnostics are one per div, so a large document produces hundreds that say the same
    /// thing. Report the shapes and their counts instead of the list.
    /// </summary>
    private static IEnumerable<string> Bucket(IEnumerable<string> diagnostics) =>
        diagnostics
            .GroupBy(Shape)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Count(),6}  {g.Key}");

    private static string Shape(string diagnostic)
    {
        if (diagnostic.Contains("no PHYSICAL structMap")) return "no PHYSICAL structMap";
        if (IsUntypedDiv(diagnostic)) return "div has no recognised TYPE";
        if (diagnostic.Contains("no premis:originalName")) return "directory div has no premis:originalName";
        if (diagnostic.Contains("has no ADMID")) return "directory div has no ADMID";
        if (diagnostic.Contains("has no fptr")) return "item div has no fptr";
        if (diagnostic.Contains("no FLocat href")) return "item div's FILEID resolves to no FLocat href";
        // Matched via the constant, not a copy of the wording: this bucket was dead for exactly that
        // reason, looking for an "already" that the diagnostic has never said.
        if (diagnostic.Contains(MetsCache.DuplicatePathFragment)) return "two divs claim one path";
        return diagnostic;
    }

    private static string Describe(IEnumerable<string?> values) =>
        string.Join(", ", values
            .Select(value => string.IsNullOrEmpty(value) ? "(untyped)" : value)
            .GroupBy(value => value)
            .Select(group => group.Count() == 1 ? group.Key : $"{group.Key}×{group.Count()}"));
}
