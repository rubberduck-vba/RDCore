using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using RDCore.SDK.Model.AST.Statements;

namespace RDCore.SDK.Model.AST.Abstract;

/// <summary>
/// Shared <see cref="System.Text.Json"/> configuration for (de)serializing a <see cref="SyntaxNode"/> tree.
/// </summary>
/// <remarks>
/// The AST crosses the ParseServer-to-LanguageServer process boundary as JSON on every parse
/// (<c>PlatformJson</c>). Most node types store the same children under two or three differently-named
/// properties: the generic inherited spine (<see cref="SyntaxNode.Children"/>, and
/// <see cref="ExpressionNode"/>/<see cref="StatementNode"/>'s own <c>Inputs</c>) and specific typed
/// properties (e.g. <c>InlineIfStatementNode.ConditionExpression</c>). Left alone,
/// <see cref="System.Text.Json"/> serializes all of them, so each nesting level <em>multiplies</em> the
/// payload rather than adding to it — an ordinary depth-16 nested expression (`1 + 1 + ... + 1`)
/// serializes to hundreds of megabytes.
/// <para>
/// <see cref="Options"/> installs a <see cref="DefaultJsonTypeInfoResolver"/> modifier that detects, per
/// concrete node type, whether the generic spine (<c>Children</c>/<c>Inputs</c>) is exactly
/// reconstructable from that type's <em>other</em>, non-<c>[JsonIgnore]</c>d node-bearing properties —
/// and if so, omits the spine from the JSON instead of the reverse. Never the reverse: a typed property
/// (read directly by consumers like <c>SymbolBuilder</c>, post-deserialize, in a different process)
/// silently losing data on the wire would be a far worse failure than a bloated payload. The check runs
/// per <em>instance</em>, not just per type, and falls back to keeping the spine on any shape it can't
/// prove — false negatives (a payload that could have shrunk further but didn't) are an acceptable
/// missed optimization; false positives (dropping data that wasn't actually reconstructable) are not.
/// </para>
/// </remarks>
public static class SyntaxNodeJson
{
    /// <summary>
    /// The options every AST (de)serialization call site should share — <see cref="PlatformJson"/> (the
    /// live wire path) and tests alike, so what a test exercises matches what actually ships.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { OmitRedundantSpine } },
    };

    private static readonly ConcurrentDictionary<Type, Func<object, IEnumerable<SyntaxNode>>[]> _alternateAccessorsByType = new();

    private static void OmitRedundantSpine(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object || !typeof(SyntaxNode).IsAssignableFrom(typeInfo.Type))
        {
            return;
        }

        var alternates = ResolveAlternateAccessors(typeInfo.Type);
        if (alternates.Length == 0)
        {
            return;
        }

        foreach (var property in typeInfo.Properties)
        {
            if (property.Name is not ("Children" or "Inputs") || property.PropertyType != typeof(ImmutableArray<SyntaxNode>))
            {
                continue;
            }

            property.ShouldSerialize = (instance, value) =>
            {
                try
                {
                    var actual = (ImmutableArray<SyntaxNode>)value!;
                    var reconstructed = alternates.SelectMany(get => get(instance)).ToArray();
                    // exact-match only: a partial reconstruction (e.g. a condition expression that
                    // covers only part of what Children holds) must not be treated as redundant.
                    return !(actual.Length == reconstructed.Length && actual.SequenceEqual(reconstructed));
                }
                catch
                {
                    // can't safely prove redundancy on this shape - keep the spine, matching the
                    // unoptimized (always-correct) behavior this mechanism refines.
                    return true;
                }
            };
        }
    }

    /// <summary>
    /// The node-bearing properties of <paramref name="concreteType"/> other than the generic
    /// <c>Children</c>/<c>Inputs</c> spine itself, excluding any already marked <c>[JsonIgnore]</c> —
    /// an ignored property (e.g. <c>VBBinaryOperatorExpressionNode.Left</c>/<c>Right</c>, computed views
    /// that intentionally rely on <c>Children</c> remaining the JSON source of truth) is invisible on
    /// the wire, so it can never justify dropping the one property that actually carries the data.
    /// </summary>
    private static Func<object, IEnumerable<SyntaxNode>>[] ResolveAlternateAccessors(Type concreteType)
        => _alternateAccessorsByType.GetOrAdd(concreteType, static type => [.. type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name is not ("Children" or "Inputs") && p.GetIndexParameters().Length == 0
                && p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            .Select(TryBuildAccessor)
            .OfType<Func<object, IEnumerable<SyntaxNode>>>()]);

    private static Func<object, IEnumerable<SyntaxNode>>? TryBuildAccessor(PropertyInfo property)
    {
        var type = property.PropertyType;

        if (typeof(SyntaxNode).IsAssignableFrom(type))
        {
            return instance => property.GetValue(instance) is SyntaxNode node ? [node] : [];
        }

        if (type == typeof(StatementBlock))
        {
            return instance => property.GetValue(instance) is StatementBlock block ? block.Children : [];
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ImmutableArray<>)
            && typeof(SyntaxNode).IsAssignableFrom(type.GetGenericArguments()[0]))
        {
            return instance => property.GetValue(instance) is IEnumerable items ? items.Cast<SyntaxNode>() : [];
        }

        if (type.IsArray && typeof(SyntaxNode).IsAssignableFrom(type.GetElementType()!))
        {
            return instance => property.GetValue(instance) is IEnumerable items ? items.Cast<SyntaxNode>() : [];
        }

        return null;
    }
}
