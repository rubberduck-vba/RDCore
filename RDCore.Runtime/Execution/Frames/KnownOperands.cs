using System.Diagnostics.CodeAnalysis;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Runtime.Execution.Frames;

/// <summary>
/// What compiled code had evaluated of an instruction when it handed the activation to the interpreter in the middle of it: the value of each expression it
/// had evaluated (a call it made, a variable it read before the call), which the interpreter takes as it is, and does not evaluate again. The interpreter
/// knows so the calls among the arguments of a call it made ahead of the rest of them, too (<see cref="Hoist"/>).
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

    // the objects calls returned (a member of what a call returns, `S.Pos.Y`): each held by the statement's temporaries, and by nothing of these: an
    // object is known only for as long as its temporary holds it, which the activation lets go of when it lets go of these (CallStackFrame.Forget). The
    // Strings calls returned inside a text are known here too, as they are.
    private Dictionary<SyntaxNode, VBTypedValue>? _objects;

    private KnownOperands(Dictionary<SyntaxNode, ScalarPayload> values, Dictionary<SyntaxNode, VBTypedValue>? objects)
        => (_values, _objects) = (values, objects);

    /// <summary>
    /// Known operands of none of the instruction's expressions yet: what the interpreter is to know of the calls among the arguments of a call it makes
    /// ahead of the rest of them (<see cref="Hoist"/>).
    /// </summary>
    public static KnownOperands None() => new(new(ReferenceEqualityComparer.Instance), null);

    /// <summary>
    /// Knows <paramref name="value"/> as what <paramref name="call"/> evaluated to: a call made ahead of the rest of the argument it is written in
    /// (<see cref="LanguageCore.Binding.ArgumentOrder"/>), which is not made again when the rest is evaluated.
    /// </summary>
    /// <param name="call">The call expression.</param>
    /// <param name="value">What it returned, which the statement's temporaries hold when it holds an object: a copy, which shares no cell with it.</param>
    public void Hoist(SyntaxNode call, VBTypedValue value)
    {
        // a number is known as its payload, which evaluating on payloads finds too.
        if (value is not VBVariantValue && ScalarPayload.IsScalar(value.TypeInfo)
            && ScalarPayload.TryOf(value.TypeInfo, value.RuntimeValue, out var payload))
        {
            _values[call] = payload;
            return;
        }

        (_objects ??= new(ReferenceEqualityComparer.Instance))[call] = value;
    }

    /// <summary>
    /// Lets go of what <see cref="Hoist"/> knew of <paramref name="call"/>: the call the argument it is written in is given to was made.
    /// </summary>
    /// <param name="call">The call expression.</param>
    /// <returns>Whether nothing is known any more.</returns>
    public bool Unhoist(SyntaxNode call)
    {
        _values.Remove(call);
        _objects?.Remove(call);
        return _values.Count == 0 && _objects is null or { Count: 0 };
    }

    /// <summary>
    /// Whether any activation holds known operands.
    /// </summary>
    public static bool AnyHeld => Volatile.Read(ref s_held) != 0;

    /// <summary>
    /// Counts an activation that takes known operands, or one that lets them go.
    /// </summary>
    /// <param name="held">Whether the activation takes them; <see langword="false"/> when it lets them go.</param>
    public static void Count(bool held) => _ = held ? Interlocked.Increment(ref s_held) : Interlocked.Decrement(ref s_held);

    /// <summary>
    /// The known operands of <paramref name="statement"/>, each given by its index in its nodes (<see cref="IndexOf"/>).
    /// </summary>
    /// <param name="statement">The instruction's statement.</param>
    /// <param name="known">The index of each expression compiled code evaluated, and its value.</param>
    /// <param name="objects">The index of each call compiled code made that returned an object, and the object, which the statement's temporaries hold.</param>
    /// <returns><see langword="null"/> when an index names no node of the statement.</returns>
    public static KnownOperands? Of(SyntaxNode statement, IReadOnlyList<(int Index, ScalarPayload Value)> known,
        IReadOnlyList<(int Index, VBTypedValue Value)>? objects = null)
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

        Dictionary<SyntaxNode, VBTypedValue>? held = null;
        foreach (var (index, value) in objects ?? [])
        {
            if (index < 0 || index >= nodes.Count)
            {
                return null;
            }

            (held ??= new(ReferenceEqualityComparer.Instance))[nodes[index]] = value;
        }

        return new KnownOperands(values, held);
    }

    /// <summary>
    /// The index of <paramref name="node"/> in the nodes of <paramref name="statement"/>, in the order they are written; <c>-1</c> when it is none of them.
    /// </summary>
    /// <param name="statement">The instruction's statement.</param>
    /// <param name="node">The expression.</param>
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
    /// The node of <paramref name="statement"/> at <paramref name="index"/> in the order they are written (<see cref="IndexOf"/>); <see langword="null"/> when
    /// there is none.
    /// </summary>
    /// <param name="statement">The instruction's statement.</param>
    /// <param name="index">The index of the node.</param>
    public static SyntaxNode? At(SyntaxNode statement, int index) => index < 0 ? null : Nodes(statement).Skip(index).FirstOrDefault();

    /// <summary>
    /// The value compiled code evaluated <paramref name="node"/> to, if it did.
    /// </summary>
    /// <param name="node">The expression.</param>
    /// <param name="value">Its value, when compiled code evaluated it.</param>
    public bool TryGet(SyntaxNode node, out ScalarPayload value) => _values.TryGetValue(node, out value);

    /// <summary>
    /// The object a call compiled code made at <paramref name="node"/> returned, if it made it: the object the statement's temporaries hold.
    /// </summary>
    /// <param name="node">The call expression.</param>
    /// <param name="value">The object, when compiled code made the call.</param>
    public bool TryGetObject(SyntaxNode node, [NotNullWhen(true)] out VBTypedValue? value)
    {
        value = null;
        return _objects is not null && _objects.TryGetValue(node, out value);
    }

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
