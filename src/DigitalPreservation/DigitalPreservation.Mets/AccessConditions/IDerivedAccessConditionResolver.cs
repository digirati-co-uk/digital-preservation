using DigitalPreservation.Common.Model.AccessConditions;

namespace DigitalPreservation.Mets.AccessConditions;

/// <summary>
/// The effective (post-inheritance) chosen access conditions of one METS div, from which its
/// derived conditions are computed.
/// </summary>
/// <param name="Restrictions">Effective <c>restriction on access</c> values; empty when none applies.</param>
/// <param name="Statement">Effective value of the profile's statement axis (for Leeds, the specific copyright statement), whose category is the matrix column; null when none applies or none is configured.</param>
/// <param name="RightsStatement">Effective <c>use and reproduction</c> URI; null when absent or explicitly suppressed. Not an input to the default matrix, but available to a custom resolver.</param>
public record EffectiveAccessInputs(IReadOnlyList<string> Restrictions, string? Statement, Uri? RightsStatement)
{
    /// <summary>No restriction, no statement, no rights statement: the physical root's parent, so to speak.</summary>
    public static readonly EffectiveAccessInputs None = new([], null, null);

    /// <summary>Value equality - the record's own compares the restriction lists by reference.</summary>
    public bool SameAs(EffectiveAccessInputs other) =>
        Restrictions.SequenceEqual(other.Restrictions, StringComparer.Ordinal)
        && Statement == other.Statement
        && RightsStatement == other.RightsStatement;
}

/// <summary>The rights statement a chosen statement implies; <see cref="Uri"/> is null when it implies none.</summary>
public sealed record StatementRights(Uri? Uri);

/// <summary>
/// Computes the derived access conditions (for Leeds, the Access Tier) a div should carry, from its
/// effective chosen ones. This is the platform's one customer-specific seam for access metadata
/// (issue #433): the default, <see cref="ProfileDerivedAccessConditionResolver"/>, is a matrix
/// lookup over the configured <see cref="AccessConditionProfile"/>, and an installation whose rule
/// depends on more than the chosen axes registers its own implementation in DI. There is no
/// plugin loader.
/// </summary>
public interface IDerivedAccessConditionResolver
{
    /// <summary>
    /// The <c>mods:accessCondition/@type</c> values this resolver owns. The METS writer removes and
    /// rewrites exactly these, and never touches any other typed access condition. Empty when
    /// nothing is derived.
    /// </summary>
    Task<IReadOnlyCollection<string>> GetManagedTypesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The <c>@type</c> of the chosen statement axis (the profile's <see cref="StatementProfile.Type"/>),
    /// which the writer reads as an input like the restriction; null when the installation has none.
    /// </summary>
    Task<string?> GetStatementTypeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The derived values for a div, keyed by type. A managed type absent from the result has no
    /// value for these inputs and is removed from the div.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> ResolveAsync(EffectiveAccessInputs inputs, CancellationToken cancellationToken = default);

    /// <summary>
    /// The rights statement a div with this statement of its own carries as <c>use and
    /// reproduction</c>; null when the statement is not one the installation knows, in which case
    /// the div's rights are left as found.
    /// </summary>
    Task<StatementRights?> GetStatementRightsAsync(string statement, CancellationToken cancellationToken = default);
}

/// <summary>
/// The default resolver: the matrices in the installation's <see cref="StatementProfile"/>. With the
/// standard access conditions there is none, and nothing is derived.
/// </summary>
public class ProfileDerivedAccessConditionResolver(IAccessConditionProfileProvider profileProvider) : IDerivedAccessConditionResolver
{
    private async Task<StatementProfile?> GetStatementsAsync(CancellationToken cancellationToken) =>
        (await profileProvider.GetProfileAsync(cancellationToken))?.Statements;

    public async Task<IReadOnlyCollection<string>> GetManagedTypesAsync(CancellationToken cancellationToken = default)
    {
        var statements = await GetStatementsAsync(cancellationToken);
        return statements?.Derived.Select(d => d.Type).ToList() ?? [];
    }

    public async Task<string?> GetStatementTypeAsync(CancellationToken cancellationToken = default)
    {
        var type = (await GetStatementsAsync(cancellationToken))?.Type;
        return string.IsNullOrWhiteSpace(type) ? null : type;
    }

    public async Task<IReadOnlyDictionary<string, string>> ResolveAsync(EffectiveAccessInputs inputs, CancellationToken cancellationToken = default)
    {
        var statements = await GetStatementsAsync(cancellationToken);
        return statements?.ResolveDerived(inputs.Restrictions, inputs.Statement) ?? new Dictionary<string, string>();
    }

    public async Task<StatementRights?> GetStatementRightsAsync(string statement, CancellationToken cancellationToken = default)
    {
        var statements = await GetStatementsAsync(cancellationToken);
        return statements?.Find(statement) == null ? null : new StatementRights(statements.RightsFor(statement));
    }
}
