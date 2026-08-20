using System.Text.Json;
using System.Xml;
using FluentAssertions;

namespace XmlGen.Tests;

/// <summary>
/// The METS ID migration tool decides, in Python and without deploying anything, whether a preserved
/// document still has an illegal xs:ID. That decision has to be the same one the platform makes, and
/// there is no way to check it from the Python side: the authority is .NET's <see cref="XmlConvert"/>,
/// so the check lives here.
/// </summary>
/// <remarks>
/// It is not a theoretical divergence. <see cref="XmlConvert"/> implements XML 1.0 <b>fourth</b>
/// edition name rules, whose letter tables are enumerated and full of gaps - U+0132, U+0133 and
/// U+017F are letters the fifth edition's NameStartChar production accepts and this does not. A
/// Python-side definition written from the modern production would call such an ID legal, and the
/// survey would report an Archival Group as conforming while its METS was still invalid. That is the
/// one way a migration campaign can fail without anyone noticing, so it is pinned rather than
/// documented.
/// </remarks>
public class NCNameRangesTests
{
    private const string RangesFile = "../../../../../mets-id-migration/app/ncname_ranges.json";

    [Theory]
    [InlineData("nameStart")]
    [InlineData("nameChar")]
    public void The_Migration_Tools_Ranges_Are_Exactly_What_XmlConvert_Accepts(string production)
    {
        Func<char, bool> accepts = production == "nameStart"
            ? XmlConvert.IsStartNCNameChar
            : XmlConvert.IsNCNameChar;

        var allowed = Allowed(production);
        var disagreements = new List<string>();
        for (var c = 0; c <= 0xFFFF; c++)
        {
            // A surrogate is half a character, never a whole one, so neither side rules on it.
            if (c is >= 0xD800 and <= 0xDFFF)
            {
                continue;
            }
            if (allowed.Contains(c) != accepts((char)c))
            {
                disagreements.Add($"U+{c:X4} (json={allowed.Contains(c)}, XmlConvert={accepts((char)c)})");
            }
        }

        disagreements.Should().BeEmpty(
            "ncname_ranges.json is generated from XmlConvert and must still match it; "
            + "regenerate it if this fails. First few: "
            + string.Join(", ", disagreements.Take(10)));
    }

    [Fact]
    public void The_Gaps_That_Made_This_Worth_Checking_Are_Still_There()
    {
        // Named so that a future reader can see what the general test above is guarding against,
        // and so that a regeneration against some other definition of "legal" fails loudly.
        var allowed = Allowed("nameStart");
        foreach (var excluded in new[] { 0x0132, 0x0133, 0x017F })
        {
            allowed.Should().NotContain(excluded,
                $"U+{excluded:X4} is a letter the fifth edition allows and XmlConvert does not");
        }
        allowed.Should().Contain(0x00E9, "an accented letter is legal and must not be flagged");
    }

    private static HashSet<int> Allowed(string production)
    {
        File.Exists(RangesFile).Should().BeTrue(
            $"the migration tool's generated ranges should be at {Path.GetFullPath(RangesFile)}");
        using var document = JsonDocument.Parse(File.ReadAllText(RangesFile));
        var allowed = new HashSet<int>();
        foreach (var range in document.RootElement.GetProperty(production).EnumerateArray())
        {
            var low = range[0].GetInt32();
            var high = range[1].GetInt32();
            for (var c = low; c <= high; c++)
            {
                allowed.Add(c);
            }
        }
        return allowed;
    }
}
