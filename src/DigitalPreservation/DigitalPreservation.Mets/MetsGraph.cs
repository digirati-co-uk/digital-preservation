using System.Collections;
using System.Reflection;
using System.Xml.Serialization;

namespace DigitalPreservation.Mets;

/// <summary>
/// Every node of a METS document, and the ID-bearing attributes on each, discovered by reflection
/// over the generated XmlGen classes.
/// </summary>
/// <remarks>
/// Reflection rather than a hand-written visitor, because the question this answers - "where can an
/// ID appear in a METS document?" - is already answered, exactly and exhaustively, by the
/// <see cref="XmlAttributeAttribute"/> declarations the schema generated. A visitor would be a
/// second, hand-transcribed copy of that answer: about thirty types to enumerate, silently wrong
/// the day one is missed, and no compiler to say so. Reading the declarations instead means a type
/// added to the schema is covered before anyone notices it exists.
/// <para>
/// The walk stops at anything outside the generated namespaces, which is what keeps it away from
/// the <c>mdWrap/xmlData</c> payload: PREMIS and MODS arrive as <see cref="System.Xml.XmlElement"/>
/// and hold content, not METS IDs, so nothing in there is ours to rewrite.
/// </para>
/// </remarks>
internal static class MetsGraph
{
    /// <summary>The generated namespaces. A property typed outside these is a leaf.</summary>
    private static readonly string[] GeneratedNamespaces =
        ["DigitalPreservation.XmlGen.Mets", "DigitalPreservation.XmlGen.Xlink"];

    private static readonly Dictionary<Type, TypePlan> Plans = new();
    private static readonly object PlansLock = new();

    /// <summary>
    /// The reflected shape of one generated type: which properties hold child nodes, and which
    /// hold ID-typed attribute values. Computed once per type and reused - the walk runs over
    /// every div in a deposit, and a deposit can hold thousands.
    /// </summary>
    private sealed class TypePlan
    {
        public required PropertyInfo[] Children { get; init; }
        public required PropertyInfo[] ChildCollections { get; init; }
        public PropertyInfo? Id { get; init; }
        public required PropertyInfo[] IdRefs { get; init; }
        public required PropertyInfo[] IdRefsCollections { get; init; }
    }

    /// <summary>
    /// The IDREF/IDREFS attributes of METS. The complete set, taken from the schema's own
    /// documentation of every attribute it declares: ADMID, DMDID, FILEID, STRUCTID. The xlink
    /// attributes that also hold IDs are handled by <see cref="MetsIdNormaliser"/> directly,
    /// because their meaning depends on the element carrying them - <c>smLink/@xlink:from</c> is
    /// an IDREF, while <c>smArcLink/@xlink:from</c> is an xlink label and must not be touched.
    /// </summary>
    private static readonly HashSet<string> IdRefAttributeNames = ["ADMID", "DMDID", "FILEID", "STRUCTID"];

    private const string IdAttributeName = "ID";

    /// <summary>
    /// Every node reachable from <paramref name="root"/>, including the root, depth first. Visited
    /// nodes are tracked by reference: a deserialised document is a tree, but nothing here depends
    /// on that being true.
    /// </summary>
    internal static IEnumerable<object> Nodes(object root)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<object>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!seen.Add(node))
            {
                continue;
            }
            yield return node;

            var plan = PlanFor(node.GetType());
            foreach (var property in plan.Children)
            {
                if (property.GetValue(node) is { } child)
                {
                    stack.Push(child);
                }
            }
            foreach (var property in plan.ChildCollections)
            {
                if (property.GetValue(node) is not IEnumerable collection)
                {
                    continue;
                }
                foreach (var item in collection)
                {
                    if (item is not null)
                    {
                        stack.Push(item);
                    }
                }
            }
        }
    }

    /// <summary>The node's xs:ID, or null if it has no ID attribute or has not been given one.</summary>
    internal static string? GetId(object node) => PlanFor(node.GetType()).Id?.GetValue(node) as string;

    internal static void SetId(object node, string id) => PlanFor(node.GetType()).Id?.SetValue(node, id);

    /// <summary>The node's single-valued IDREF attributes (FILEID), as get/set pairs.</summary>
    internal static IEnumerable<(string Attribute, string Value, Action<string> Set)> IdRefs(object node)
    {
        foreach (var property in PlanFor(node.GetType()).IdRefs)
        {
            if (property.GetValue(node) is string value && value.Length > 0)
            {
                yield return (AttributeName(property), value, newValue => property.SetValue(node, newValue));
            }
        }
    }

    /// <summary>
    /// The node's IDREFS attributes (ADMID, DMDID, STRUCTID) as the live token collections the
    /// XmlSerializer split them into. Mutating one rewrites the attribute.
    /// </summary>
    internal static IEnumerable<(string Attribute, IList<string> Tokens)> IdRefsCollections(object node)
    {
        foreach (var property in PlanFor(node.GetType()).IdRefsCollections)
        {
            if (property.GetValue(node) is IList<string> tokens && tokens.Count > 0)
            {
                yield return (AttributeName(property), tokens);
            }
        }
    }

    private static string AttributeName(PropertyInfo property) =>
        property.GetCustomAttribute<XmlAttributeAttribute>()?.AttributeName ?? property.Name;

    private static TypePlan PlanFor(Type type)
    {
        lock (PlansLock)
        {
            if (Plans.TryGetValue(type, out var existing))
            {
                return existing;
            }
            var plan = BuildPlan(type);
            Plans[type] = plan;
            return plan;
        }
    }

    private static TypePlan BuildPlan(Type type)
    {
        var children = new List<PropertyInfo>();
        var childCollections = new List<PropertyInfo>();
        var idRefs = new List<PropertyInfo>();
        var idRefsCollections = new List<PropertyInfo>();
        PropertyInfo? id = null;

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || property.GetMethod is null)
            {
                continue;
            }

            // XmlIgnore marks the generated *Specified companions and backing views, which are
            // duplicates of properties already covered.
            if (property.GetCustomAttribute<XmlIgnoreAttribute>() is not null)
            {
                continue;
            }

            var attributeName = property.GetCustomAttribute<XmlAttributeAttribute>()?.AttributeName;
            if (attributeName == IdAttributeName && property.PropertyType == typeof(string))
            {
                id = property;
                continue;
            }
            if (attributeName is not null && IdRefAttributeNames.Contains(attributeName))
            {
                if (property.PropertyType == typeof(string))
                {
                    idRefs.Add(property);
                }
                else if (typeof(IList<string>).IsAssignableFrom(property.PropertyType))
                {
                    idRefsCollections.Add(property);
                }
                continue;
            }

            if (IsGenerated(property.PropertyType))
            {
                children.Add(property);
            }
            else if (CollectionElementType(property.PropertyType) is { } element && IsGenerated(element))
            {
                childCollections.Add(property);
            }
        }

        return new TypePlan
        {
            Children = children.ToArray(),
            ChildCollections = childCollections.ToArray(),
            Id = id,
            IdRefs = idRefs.ToArray(),
            IdRefsCollections = idRefsCollections.ToArray()
        };
    }

    private static bool IsGenerated(Type type) =>
        type is { IsClass: true, Namespace: not null }
        && Array.Exists(GeneratedNamespaces, ns => type.Namespace!.StartsWith(ns, StringComparison.Ordinal));

    private static Type? CollectionElementType(Type type)
    {
        if (type == typeof(string))
        {
            return null;
        }
        if (type.IsArray)
        {
            return type.GetElementType();
        }
        foreach (var candidate in type.GetInterfaces().Append(type))
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return candidate.GetGenericArguments()[0];
            }
        }
        return null;
    }
}
