using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Utils;
using DigitalPreservation.XmlGen.Mets;
using Microsoft.Extensions.Logging;

namespace DigitalPreservation.Mets.AccessConditions;

/// <summary>
/// The METS writer's half of derived access conditions (issue #433): on every write, the values an
/// installation's profile derives (for Leeds, the Access Tier) are recomputed for the whole
/// document from each div's EFFECTIVE chosen conditions. Kept apart from <see cref="MetsManager"/>,
/// whose own access-condition code is the standard model - set a restriction, set a rights
/// statement - and which only calls in here; with the standard access conditions nothing is
/// derived and this class is never constructed.
/// </summary>
internal sealed class DerivedAccessConditionWriter(IDerivedAccessConditionResolver resolver, ILogger? logger)
{
    /// <summary>
    /// Rewrite the derived access conditions (issue #433) across the whole document from each div's
    /// EFFECTIVE chosen conditions - restriction, statement and rights statement - so that what is
    /// written agrees with what the parser reads back for every node. The writer OWNS the managed
    /// types (for Leeds, <c>tier</c>) on:
    /// <list type="bullet">
    /// <item>every div that carries a chosen element of its own: the value for its effective inputs
    /// is written;</item>
    /// <item>every FILE div whose effective inputs come partly from its logical range - the parser
    /// falls back to the range for whichever axis the physical tree is silent on, so such a file
    /// cannot inherit the right value from its folder and is given its own;</item>
    /// <item>every div whose dmdSec the platform itself minted: a managed type left there by an
    /// earlier write, now without inputs, is removed so the div inherits like everything else.</item>
    /// </list>
    /// It also owns <c>use and reproduction</c> on every div with a statement of its own: the
    /// statement's rights statement (<see cref="ApplyStatementRights"/>).
    /// A div with a THIRD PARTY's dmdSec and no chosen inputs is left exactly as found - a Goobi
    /// export carrying only its resulting tier keeps it - as is every non-managed typed condition
    /// everywhere. Called from <see cref="MetsManager.WriteMets"/>, through
    /// <see cref="MetsManager.ApplyDerivedAccessConditions"/>; a no-op with no statements and nothing derived.
    /// </summary>
    /// <returns>The IDs of the divs whose MODS changed.</returns>
    public async Task<List<string>> ApplyAsync(FullMets fullMets)
    {
        var changed = new List<string>();
        var managed = await resolver.GetManagedTypesAsync();
        var statementType = await resolver.GetStatementTypeAsync();
        if (managed.Count == 0 && statementType == null) return changed;

        var pass = new DerivedPass(fullMets, managed, statementType, changed, PathsByDiv(fullMets));

        // Logical ranges inherit from the physical root (MetsParser.ComputeEffectiveMetadata) and
        // files fall back to their range, so: directories first, then ranges, then files.
        var rootInputs = EffectiveAccessInputs.None;
        var fileDivs = new List<(DivType Div, EffectiveAccessInputs Parent)>();
        var physRoot = fullMets.Mets.StructMap.FirstOrDefault(sm => sm.Type == Constants.Physical)?.Div;
        if (physRoot != null)
        {
            rootInputs = await ApplyDerivedToDirectoryTree(pass, physRoot, rootInputs, fileDivs);
        }
        var logicalInputs = new Dictionary<DivType, EffectiveAccessInputs>(ReferenceEqualityComparer.Instance);
        foreach (var logical in fullMets.Mets.StructMap.Where(sm => sm.Type == Constants.Logical && sm.Div != null))
        {
            await ApplyDerivedToLogicalTree(pass, logical.Div, rootInputs, logicalInputs);
        }
        var wholeFileRanges = WholeFileRangesByFileId(logicalInputs.Keys);
        foreach (var (div, parent) in fileDivs)
        {
            await ApplyDerivedToFileDiv(pass, div, parent, wholeFileRanges, logicalInputs);
        }

        if (changed.Count > 0 && logger?.IsEnabled(LogLevel.Information) == true)
        {
            logger.LogInformation("Derived access conditions rewritten on {Count} div(s) in {MetsUri}",
                changed.Count, fullMets.Uri);
        }
        return changed;
    }

    /// <summary>Everything one derived pass needs, so that the helpers stay small.</summary>
    private sealed record DerivedPass(
        FullMets Mets,
        IReadOnlyCollection<string> Managed,
        string? StatementType,
        List<string> Changed,
        Dictionary<DivType, string> PathByDiv);

    /// <summary>The chosen elements a div carries itself.</summary>
    private sealed record ChosenInputs(List<string> Restrictions, string? Statement, bool RightsPresent, Uri? Rights)
    {
        public static readonly ChosenInputs Nothing = new([], null, false, null);

        public bool Any => Restrictions.Count > 0 || Statement != null || RightsPresent;

        /// <summary>The div's effective inputs: its own where set, else its parent's.</summary>
        public EffectiveAccessInputs ApplyTo(EffectiveAccessInputs parent) => new(
            Restrictions.Count > 0 ? Restrictions : parent.Restrictions,
            Statement ?? parent.Statement,
            RightsPresent ? Rights : parent.RightsStatement);
    }

    private static ChosenInputs ReadChosenInputs(DerivedPass pass, XmlGen.Mods.V3.ModsDefinition mods)
    {
        // Only the platform's own chosen elements are inputs. Goobi's "status" element, which the
        // parser reads as a restriction, never reaches here: Goobi METS is never editable (#223).
        // Nor do ranges associated with files through div-to-div smLinks, another Goobi idiom the
        // parser follows (docs/effective-metadata-inheritance.md, "Other typed access conditions").
        var restrictions = mods.GetAccessConditions(Constants.RestrictionOnAccess);
        var statement = pass.StatementType == null ? null : mods.GetAccessConditions(pass.StatementType).FirstOrDefault();
        var rightsElements = mods.GetAccessConditions(Constants.UseAndReproduction);
        Uri? rights = null;
        if (rightsElements.Count > 0)
        {
            // Present but not a URI is the "explicitly none" sentinel: effective rights null, and
            // inheritance stops here - the same reading as the parser's.
            Uri.TryCreate(rightsElements[0], UriKind.Absolute, out rights);
        }
        return new ChosenInputs(restrictions, statement, rightsElements.Count > 0, rights);
    }

    private async Task<EffectiveAccessInputs> ApplyDerivedToDirectoryTree(
        DerivedPass pass, DivType div, EffectiveAccessInputs parent, List<(DivType Div, EffectiveAccessInputs Parent)> fileDivs)
    {
        if (div.Fptr.Count > 0)
        {
            // A file: handled last, once the logical ranges it may fall back to are known.
            fileDivs.Add((div, parent));
            return parent;
        }
        var effective = await ApplyDerivedToDiv(pass, div, parent);
        foreach (var child in div.Div)
        {
            await ApplyDerivedToDirectoryTree(pass, child, effective, fileDivs);
        }
        return effective;
    }

    private async Task ApplyDerivedToLogicalTree(
        DerivedPass pass, DivType div, EffectiveAccessInputs parent, Dictionary<DivType, EffectiveAccessInputs> logicalInputs)
    {
        var effective = await ApplyDerivedToDiv(pass, div, parent);
        logicalInputs[div] = effective;
        foreach (var child in div.Div)
        {
            await ApplyDerivedToLogicalTree(pass, child, effective, logicalInputs);
        }
    }

    /// <summary>A directory or logical div: its managed types follow its own chosen inputs.</summary>
    private async Task<EffectiveAccessInputs> ApplyDerivedToDiv(DerivedPass pass, DivType div, EffectiveAccessInputs parent)
    {
        var mods = ModsManager.GetModsForDiv(pass.Mets.Mets, div);
        if (mods == null) return parent;
        await ApplyStatementRights(pass, div, mods);
        var chosen = ReadChosenInputs(pass, mods);
        var effective = chosen.ApplyTo(parent);
        await RewriteManaged(pass, div, mods, chosen.Any ? effective : null);
        return effective;
    }

    /// <summary>
    /// A file div: for an axis the physical tree is silent on, its effective input comes from its
    /// single whole-file logical range (MetsParser.SetFileEffective), in which case it is given
    /// its own managed types even without chosen inputs, since it cannot inherit the right value.
    /// </summary>
    private async Task ApplyDerivedToFileDiv(
        DerivedPass pass, DivType div, EffectiveAccessInputs parent,
        Dictionary<string, List<DivType>> wholeFileRanges, Dictionary<DivType, EffectiveAccessInputs> logicalInputs)
    {
        var mods = ModsManager.GetModsForDiv(pass.Mets.Mets, div);
        if (mods != null) await ApplyStatementRights(pass, div, mods);
        var chosen = mods == null ? ChosenInputs.Nothing : ReadChosenInputs(pass, mods);
        var physical = chosen.ApplyTo(parent);
        var effective = WithLogicalFallback(physical, chosen, div, wholeFileRanges, logicalInputs);

        if (chosen.Any || !effective.SameAs(physical))
        {
            mods ??= ModsManager.GetModsForDiv(pass.Mets.Mets, div, createDmd: true, pass.PathByDiv.GetValueOrDefault(div));
            if (mods == null) return;
            await RewriteManaged(pass, div, mods, effective);
        }
        else if (mods != null)
        {
            await RewriteManaged(pass, div, mods, null);
        }
    }

    private static EffectiveAccessInputs WithLogicalFallback(
        EffectiveAccessInputs physical, ChosenInputs chosen, DivType div,
        Dictionary<string, List<DivType>> wholeFileRanges, Dictionary<DivType, EffectiveAccessInputs> logicalInputs)
    {
        var fileId = div.Fptr[0].Fileid;
        if (fileId == null || !wholeFileRanges.TryGetValue(fileId, out var ranges) || ranges.Count != 1)
        {
            return physical;
        }
        var range = logicalInputs[ranges[0]];
        // Restriction and statement: the physical tree (own, then ancestors) wins whenever it says
        // anything at all.
        var restrictions = physical.Restrictions.Count > 0 ? physical.Restrictions : range.Restrictions;
        var statement = physical.Statement ?? range.Statement;
        // Rights: the file's own element (including the "none" sentinel) wins; else the nearest
        // ancestor with a rights statement; else the range's.
        var rights = chosen.RightsPresent ? physical.RightsStatement : physical.RightsStatement ?? range.RightsStatement;
        return new EffectiveAccessInputs(restrictions, statement, rights);
    }

    /// <summary>
    /// A div with a statement of its own carries that statement's rights statement as <c>use and
    /// reproduction</c> (issue #433), so that any METS reader sees a standard, long-lived rights URI
    /// and the div's effective rights always follow its effective statement. A statement that
    /// implies none gets the "none" sentinel, which stops a parent's rights being inherited; a
    /// statement the installation does not know leaves the div's rights as found.
    /// </summary>
    private async Task ApplyStatementRights(DerivedPass pass, DivType div, XmlGen.Mods.V3.ModsDefinition mods)
    {
        if (pass.StatementType == null) return;
        var statement = mods.GetAccessConditions(pass.StatementType).FirstOrDefault();
        if (statement == null) return;
        var rights = await resolver.GetStatementRightsAsync(statement);
        if (rights == null) return;

        var value = rights.Uri?.ToString() ?? Constants.NullRightsStatement;
        if (mods.GetAccessConditions(Constants.UseAndReproduction).SequenceEqual([value])) return;
        mods.RemoveAccessConditions(Constants.UseAndReproduction);
        mods.AddAccessCondition(value, Constants.UseAndReproduction);
        ModsManager.SetModsForDiv(pass.Mets.Mets, div, mods);
        MarkChanged(pass, div);
    }

    private static void MarkChanged(DerivedPass pass, DivType div)
    {
        var id = div.Id ?? string.Empty;
        if (!pass.Changed.Contains(id)) pass.Changed.Add(id);
    }

    /// <summary>
    /// FILEID -> the logical divs that paint the whole file: an fptr with no area, or with an area
    /// that picks out no part of it (<see cref="AreaExtent"/>, as the parser reads it).
    /// </summary>
    private static Dictionary<string, List<DivType>> WholeFileRangesByFileId(IEnumerable<DivType> logicalDivs)
    {
        var map = new Dictionary<string, List<DivType>>(StringComparer.Ordinal);
        foreach (var div in logicalDivs)
        {
            var wholeFileIds = div.Fptr
                .Where(f => IsWholeFile(f.Area))
                .Select(f => f.Fileid ?? f.Area?.Fileid)
                .OfType<string>()
                .Distinct();
            foreach (var fileId in wholeFileIds)
            {
                if (!map.TryGetValue(fileId, out var list))
                {
                    map[fileId] = list = [];
                }
                if (!list.Contains(div)) list.Add(div);
            }
        }
        return map;
    }

    /// <summary>No area, or one with none of the <see cref="AreaExtent.Attributes"/>: the whole file.</summary>
    private static bool IsWholeFile(AreaType? area) =>
        area == null
        || (area.Begin == null && area.End == null && area.Coords == null && area.Extent == null
            && !area.ShapeValueSpecified && !area.BetypeValueSpecified);

    /// <summary>
    /// Writes the managed types for <paramref name="inputs"/>; or, when that is null because the
    /// div has no inputs of its own, removes them if the platform minted this div's dmdSec and
    /// otherwise leaves the div as found.
    /// </summary>
    private async Task RewriteManaged(DerivedPass pass, DivType div, XmlGen.Mods.V3.ModsDefinition mods, EffectiveAccessInputs? inputs)
    {
        if (inputs == null && !IsPlatformMintedDmd(pass, div)) return;

        var before = ManagedSnapshot(mods, pass.Managed);
        foreach (var type in pass.Managed)
        {
            mods.RemoveAccessConditions(type);
        }
        if (inputs != null)
        {
            var derived = await resolver.ResolveAsync(inputs);
            foreach (var type in pass.Managed)
            {
                if (derived.TryGetValue(type, out var value)) mods.AddAccessCondition(value, type);
            }
        }
        if (!ManagedSnapshot(mods, pass.Managed).SequenceEqual(before))
        {
            ModsManager.SetModsForDiv(pass.Mets.Mets, div, mods);
            MarkChanged(pass, div);
        }
    }

    /// <summary>
    /// Whether this div's dmdSec is one the platform minted - <see cref="MetsIds.Dmd"/> of its
    /// path, <see cref="MetsIds.DmdFromDivId"/> of a logical div's ID, the root's, or an anonymous
    /// one. The writer owns the managed types in those and only those: a third party's dmdSec is
    /// never stripped of a value it arrived with.
    /// </summary>
    private static bool IsPlatformMintedDmd(DerivedPass pass, DivType div)
    {
        var dmdId = IdRefs.ResolveSingle(div.Dmdid, id => pass.Mets.Mets.DmdSec.FirstOrDefault(x => x.Id == id))?.Id;
        if (dmdId == null) return false;
        if (dmdId == Constants.DmdPhysRoot || dmdId.StartsWith(Constants.DmdIdPrefix + "ANON_", StringComparison.Ordinal))
        {
            return true;
        }
        if (pass.PathByDiv.TryGetValue(div, out var path))
        {
            return dmdId == MetsIds.Dmd(path);
        }
        return div.Id.HasText() && dmdId == MetsIds.DmdFromDivId(div.Id);
    }

    private static Dictionary<DivType, string> PathsByDiv(FullMets fullMets)
    {
        if (fullMets.PhysicalDivsByPath.Count == 0) MetsCache.Populate(fullMets);
        var map = new Dictionary<DivType, string>(ReferenceEqualityComparer.Instance);
        foreach (var (path, div) in fullMets.PhysicalDivsByPath)
        {
            map[div] = path;
        }
        return map;
    }

    private static List<string> ManagedSnapshot(XmlGen.Mods.V3.ModsDefinition mods, IReadOnlyCollection<string> managed) =>
        managed.Select(type => type + "=" + string.Join("|", mods.GetAccessConditions(type))).ToList();
}
