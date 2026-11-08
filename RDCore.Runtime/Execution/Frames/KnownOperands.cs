using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.Runtime.Execution.Frames;

/// <summary>
/// What compiled code had evaluated of an instruction when it handed the activation to the interpreter in the middle of it: the value of each expression it
/// had evaluated - a call it made, a variable it read before the call - which the interpreter takes as it is, and does not evaluate again.
/// </summary>
/// <remarks>
/// An expression is named by where it is in its instruction's statement: its index in the statement's nodes, in the order they are written
/// (<see cref="IndexOf"/>). That is what names it in the edited body too, where the statement, kept as it was, is written by nodes of its own.
/// </remarks>
internal sealed class KnownOperands
{
    // how many activations hold known operands: none, almost always, and then no expression the interpreter evaluates is looked for.
    private static int s_held;

    private readonly Dictionary<SyntaxNode, ScalarPayload> _values;

    private KnownOperands(Dictionary<SyntaxNode, ScalarPayload> values) => _values = values;

    /// <summary>
    /// Whether any activation holds known operands.
    /// </summary>
    public static bool AnyHeld => Volatile.Read(ref s_held) != 0;

    /// <summary>
    /// Counts an activation that takes known operands, or one that lets them go.
    /// </summary>
    public static void Count(bool held) => _ = held ? Interlocked.Increment(ref s_held) : Interlocked.Decrement(ref s_held);

    /// <summary>
    /// The known operands of <paramref name="statement"/>, each given by its index in its nodes (<see cref="IndexOf"/>).
    /// </summary>
    /// <returns><see langword="null"/> when an index names no node of the statement.</returns>
    public static KnownOperands? Of(SyntaxNode statement, IReadOnlyList<(int Index, ScalarPayload Value)> known)
    {
        var nodes = Nodes(statement).ToList();
        var values = new Dictionary<SyntaxNode, ScalarPayload>(known.Count, ReferenceEqualityComparer.Instance);
        foreach (var (index, value) in known)
        {
            if (index < 0 || index >= nodes.Count)
            {
                return null;
            }

            values[nodes[index]] = value;
        }

        return new KnownOperands(values);
    }

    /// <summary>
    /// The index of <paramref name="node"/> in the nodes of <paramref name="statement"/>, in the order they are written; <c>-1</c> when it is none of them.
    /// </summary>
    public static int IndexOf(SyntaxNode statement, SyntaxNode node)
    {
        var index = 0;
        foreach (var each in Nodes(statement))
        {
            if (ReferenceEquals(each, node))
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    /// <summary>
    /// The value compiled code evaluated <paramref name="node"/> to, if it did.
    /// </summary>
    public bool TryGet(SyntaxNode node, out ScalarPayload value) => _values.TryGetValue(node, out value);

    // a statement's nodes, the statement first, each before what it is made of; the statements of a block are not the statement's.
    private static IEnumerable<SyntaxNode> Nodes(SyntaxNode statement)
    {
        var pending = new Stack<SyntaxNode>();
        pending.Push(statement);
        while (pending.TryPop(out var node))
        {
            yield return node;
            for (var child = node.Children.Length - 1; child >= 0; child--)
            {
                pending.Push(node.Children[child]);
            }
        }
    }
}
