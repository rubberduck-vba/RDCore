using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;

namespace RDCore.Parsing.AST;

// replaces nodes of a syntax tree with others, and rebuilds the nodes that hold them: each is copied, as a record is copied `with` new values, with what it holds
// through its properties replaced - a node, an ImmutableArray or an array of nodes, a StatementBlock. A property that only reads what the node holds
// (VBBinaryOperatorExpressionNode.Left reads Children[0]) reads the new one. A node is a record: equal nodes are not the same node, and each is told apart by
// reference.
internal static class SyntaxTreePruning
{
    private static readonly MethodInfo Clone = typeof(object).GetMethod(nameof(MemberwiseClone), BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo ReplacedArrayOf = typeof(SyntaxTreePruning).GetMethod(nameof(ReplacedArray), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo ReplacedImmutableArrayOf = typeof(SyntaxTreePruning).GetMethod(nameof(ReplacedImmutableArray), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly ConcurrentDictionary<Type, Slot[]> Slots = new();

    // a property of a node that holds nodes: the type of each node it holds, and how it is given new ones.
    private sealed record Slot(PropertyInfo Property, Type HeldType, Func<object, IReadOnlyDictionary<SyntaxNode, SyntaxNode>, object>? Replace);

    /// <summary>
    /// Whether <paramref name="holder"/> holds <paramref name="node"/> only where an expression of any kind may be held: an operand, an argument, a child - not
    /// the name of a member access, which is a name and nothing else.
    /// </summary>
    public static bool HoldsAnyExpressionWhere(SyntaxNode holder, SyntaxNode node)
        => SlotsOf(holder.GetType()).All(slot => !HeldBy(slot, holder).Any(held => ReferenceEquals(held, node))
            || slot.HeldType.IsAssignableFrom(typeof(UnbuiltExpressionTriviaNode)));

    /// <summary>
    /// <paramref name="root"/> with each node of <paramref name="replacements"/> replaced, and each of <paramref name="holders"/> rebuilt with the nodes it holds
    /// replaced or rebuilt.
    /// </summary>
    /// <param name="root">The tree.</param>
    /// <param name="replacements">The nodes to replace, and what replaces each.</param>
    /// <param name="holders">The nodes that hold a node to replace, however deep: <paramref name="root"/> is one, if anything is replaced.</param>
    /// <exception cref="InvalidOperationException">A node holds a node to replace in a way it cannot be given another.</exception>
    public static SyntaxNode Replaced(SyntaxNode root, IReadOnlyDictionary<SyntaxNode, SyntaxNode> replacements, IReadOnlyDictionary<SyntaxNode, int> holders)
    {
        var rebuilt = new Dictionary<SyntaxNode, SyntaxNode>(replacements, ReferenceEqualityComparer.Instance);

        // what a node holds is rebuilt before it is: the deepest first.
        foreach (var holder in holders.OrderByDescending(holder => holder.Value).Select(holder => holder.Key))
        {
            if (!rebuilt.ContainsKey(holder))
            {
                rebuilt[holder] = Rebuilt(holder, rebuilt);
            }
        }

        return rebuilt.GetValueOrDefault(root, root);
    }

    private static SyntaxNode Rebuilt(SyntaxNode node, IReadOnlyDictionary<SyntaxNode, SyntaxNode> rebuilt)
    {
        var copy = (SyntaxNode)Clone.Invoke(node, null)!;
        foreach (var slot in SlotsOf(node.GetType()))
        {
            if (slot.Replace is not null && slot.Property.GetValue(node) is { } value)
            {
                slot.Property.SetValue(copy, slot.Replace(value, rebuilt));
            }
        }

        // a node that holds one in some other way still holds the one it was copied with.
        if (copy.ChildNodes().FirstOrDefault(child => rebuilt.ContainsKey(child)) is { } kept)
        {
            throw new InvalidOperationException($"{node.GetType().Name} holds a {kept.GetType().Name} that cannot be replaced.");
        }

        return copy;
    }

    private static IEnumerable<SyntaxNode> HeldBy(Slot slot, SyntaxNode holder) => slot.Property.GetValue(holder) switch
    {
        SyntaxNode node => [node],
        StatementBlock block => block.Children,
        ImmutableArray<ArrayDimensionBound> bounds => ExpressionsOf(bounds),
        IEnumerable nodes => nodes.OfType<SyntaxNode>(),
        _ => [],
    };

    private static Slot[] SlotsOf(Type type)
        => Slots.GetOrAdd(type, static type => [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0 && property.CanRead)
            .Select(SlotOf)
            .OfType<Slot>()]);

    // a property that only reads what the node holds is a slot that is not given anything: what it reads is.
    private static Slot? SlotOf(PropertyInfo property)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var writable = property.CanWrite;

        if (typeof(SyntaxNode).IsAssignableFrom(type))
        {
            return new Slot(property, type, writable ? static (value, rebuilt) => rebuilt.GetValueOrDefault((SyntaxNode)value, (SyntaxNode)value) : null);
        }

        if (type == typeof(StatementBlock))
        {
            return new Slot(property, typeof(StatementBlock), writable ? static (value, rebuilt) => ReplacedBlock((StatementBlock)value, rebuilt) : null);
        }

        // the bounds of a declared array hold their expressions in a struct of their own.
        if (type == typeof(ImmutableArray<ArrayDimensionBound>))
        {
            return new Slot(property, typeof(ExpressionNode),
                writable ? static (value, rebuilt) => ReplacedBounds((ImmutableArray<ArrayDimensionBound>)value, rebuilt) : null);
        }

        if (type.IsArray && type.GetElementType() is { } element && typeof(SyntaxNode).IsAssignableFrom(element))
        {
            return new Slot(property, element, writable ? Replacing(ReplacedArrayOf.MakeGenericMethod(element)) : null);
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ImmutableArray<>) && type.GetGenericArguments()[0] is var item
            && typeof(SyntaxNode).IsAssignableFrom(item))
        {
            return new Slot(property, item, writable ? Replacing(ReplacedImmutableArrayOf.MakeGenericMethod(item)) : null);
        }

        return null;
    }

    private static Func<object, IReadOnlyDictionary<SyntaxNode, SyntaxNode>, object> Replacing(MethodInfo replaced)
        => (value, rebuilt) => replaced.Invoke(null, [value, rebuilt])!;

    private static StatementBlock ReplacedBlock(StatementBlock block, IReadOnlyDictionary<SyntaxNode, SyntaxNode> rebuilt)
        => block with { Children = ReplacedImmutableArray(block.Children, rebuilt) };

    private static ImmutableArray<ArrayDimensionBound> ReplacedBounds(ImmutableArray<ArrayDimensionBound> bounds,
        IReadOnlyDictionary<SyntaxNode, SyntaxNode> rebuilt)
    {
        ExpressionNode? Replaced(ExpressionNode? expression) => expression is null ? null : (ExpressionNode)rebuilt.GetValueOrDefault(expression, expression);

        return !ExpressionsOf(bounds).Any(rebuilt.ContainsKey)
            ? bounds
            : [.. bounds.Select(bound => bound with { LowerExpression = Replaced(bound.LowerExpression), UpperExpression = Replaced(bound.UpperExpression) })];
    }

    // the expressions of the bounds of a declared array, each bound's lower one before its upper one.
    private static IEnumerable<SyntaxNode> ExpressionsOf(ImmutableArray<ArrayDimensionBound> bounds)
        => bounds.IsDefault ? [] : bounds.SelectMany(bound => new[] { bound.LowerExpression, bound.UpperExpression }).OfType<SyntaxNode>();

    // a node replaced with one of a type its holder cannot hold is a cast that fails, not a node that is silently left out.
    private static ImmutableArray<T> ReplacedImmutableArray<T>(ImmutableArray<T> nodes, IReadOnlyDictionary<SyntaxNode, SyntaxNode> rebuilt) where T : SyntaxNode
        => nodes.IsDefault || !nodes.Any(rebuilt.ContainsKey) ? nodes : [.. nodes.Select(node => (T)rebuilt.GetValueOrDefault(node, node))];

    private static T[] ReplacedArray<T>(T[] nodes, IReadOnlyDictionary<SyntaxNode, SyntaxNode> rebuilt) where T : SyntaxNode
        => !nodes.Any(rebuilt.ContainsKey) ? nodes : [.. nodes.Select(node => (T)rebuilt.GetValueOrDefault(node, node))];
}
