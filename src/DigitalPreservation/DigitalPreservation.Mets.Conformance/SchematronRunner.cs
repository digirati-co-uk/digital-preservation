using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;

namespace DigitalPreservation.Mets.Conformance;

/// <summary>
/// Runs the compiled Schematron rules against a METS document.
///
/// The rules are authored as Schematron next to the Python judge
/// (src/mets-editability/schematron/*.sch) and compiled once, by tools/compile_schematron.py,
/// into plain XSLT 1.0 that emits SVRL (schematron/compiled/*.xsl, embedded here). Both judges
/// run that same compiled XSLT - this class through XslCompiledTransform, the Python judge
/// through lxml - so the two cannot drift apart through different Schematron processors. Each
/// svrl:failed-assert carries the assert's @id, a finding code shared with the native checks
/// and the Python side. The twin of app/schematron.py.
/// </summary>
internal static class SchematronRunner
{
    // A namespace name is an identifier, never fetched, and fixed by the ISO Schematron spec:
    // https would be a different (wrong) namespace.
#pragma warning disable S5332
    private static readonly XNamespace Svrl = "http://purl.oclc.org/dsdl/svrl";
#pragma warning restore S5332

    // XslCompiledTransform.Transform is thread-safe once loaded, and each call below produces
    // its own report document, so one transform per rule set serves every thread.
    private static readonly Dictionary<string, XslCompiledTransform> Transforms = new();
    private static readonly object TransformsLock = new();

    /// <summary>
    /// The failed asserts of one rule set ("common", "platform-tier" or "eprints-tier") against
    /// one mets:mets element: one Finding per code, counting every element that failed it.
    /// </summary>
    internal static List<Finding> Failures(string ruleSet, XElement mets)
    {
        var report = new XDocument();
        using (var reader = mets.CreateReader())
        using (var writer = report.CreateWriter())
        {
            TransformFor(ruleSet).Transform(reader, writer);
        }

        var found = new List<Finding>();
        foreach (var failed in report.Descendants(Svrl + "failed-assert"))
        {
            var text = failed.Element(Svrl + "text")?.Value ?? "";
            Findings.Add(found, failed.Attribute("id")?.Value ?? "SCHEMATRON",
                string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
        }
        return found;
    }

    private static XslCompiledTransform TransformFor(string ruleSet)
    {
        lock (TransformsLock)
        {
            if (Transforms.TryGetValue(ruleSet, out var existing))
            {
                return existing;
            }
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .Single(name => name.EndsWith($".{ruleSet}.xsl", StringComparison.Ordinal));
            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = XmlReader.Create(stream);
            var transform = new XslCompiledTransform();
            transform.Load(reader);
            Transforms[ruleSet] = transform;
            return transform;
        }
    }
}
