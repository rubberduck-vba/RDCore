using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.Runtime.LanguageCore.StdLib;
using System.Collections.Immutable;

namespace RDCore.Runtime.LanguageCore.Binding;

/// <summary>
/// The order the arguments of a call are evaluated in. MS-VBAL §5.3.1.11 binds the parameters in order from left to right and does not say when the
/// expression of each argument is evaluated; they are evaluated as MS-VBA evaluates them.
/// <list type="number">
/// <item>The calls among the arguments are made first, left to right, but for the last argument: an argument that is a call, and each call inside an
/// argument that is an expression.</item>
/// <item>In a call statement, the calls inside the arguments of the target's member calls are made next (<see cref="TargetCallsIn"/>).</item>
/// <item>Then every other argument is evaluated, right to left, what it calls already made: the last argument as it is written, the others as late as can
/// be, so that a variable they read is read after the calls.</item>
/// </list>
/// </summary>
/// <remarks>
/// The arguments are counted by parameter: the index of each is the index of the parameter it is given to, and the last argument is the last one given.
/// A call is a procedure called (a <c>Function</c>, a <c>Property Get</c>, a function of the standard library) or a member of an object, a public variable of
/// it too; a function the standard library has evaluated inline (<c>Len</c>, <c>Abs</c>, the conversions) is none, and neither is <c>New</c>.
/// </remarks>
public readonly struct ArgumentOrder
{
    private readonly ulong _calls;
    private readonly ulong _hoists;
    private readonly ArgumentEvaluation[]? _many;

    private ArgumentOrder(int count, ulong calls, ulong hoists, ArgumentEvaluation[]? many, bool lastCalls)
        => (Count, _calls, _hoists, _many, LastCalls) = (count, calls, hoists, many, lastCalls);

    public int Count { get; }

    // whether the last argument makes a call: it is evaluated in place, first of the arguments evaluated right to left.
    public bool LastCalls { get; }

    // whether an argument is evaluated with the calls, at its place left to right.
    public bool IsCall(int index) => _many is { } many ? many[index] is ArgumentEvaluation.Call : (_calls >> index & 1) != 0;

    // whether the calls inside an argument are made with the calls, and the rest of it evaluated after them.
    public bool Hoists(int index) => _many is { } many ? many[index] is ArgumentEvaluation.Hoisted : (_hoists >> index & 1) != 0;

    // whether any argument has its calls made ahead of the rest of it.
    public bool AnyHoisted => _many is { } many ? many.Contains(ArgumentEvaluation.Hoisted) : _hoists != 0;

    // whether any argument makes a call.
    public bool AnyCall => LastCalls || (_many is { } many ? many.Any(static each => each is not ArgumentEvaluation.Plain) : (_calls | _hoists) != 0);

    public static ArgumentOrder Of(ImmutableArray<BoundArgument> arguments)
    {
        if (arguments.IsDefaultOrEmpty)
        {
            return default;
        }

        var last = arguments.Length - 1;
        while (last >= 0 && arguments[last] is { Passing: ArgumentPassing.Omitted } or { Passing: ArgumentPassing.ParamArray, Elements.IsEmpty: true })
        {
            last--;
        }

        var lastCalls = last >= 0 && arguments[last] is { Value: { } lastValue } && ContainsCall(lastValue)
            || last >= 0 && arguments[last] is { Passing: ArgumentPassing.ParamArray, Elements: var lastElements } && lastElements.Any(ContainsCall);
        return Of(arguments.Length, (arguments, last), lastCalls, static (state, i) => state.arguments[i] switch
        {
            { Passing: ArgumentPassing.Omitted } => ArgumentEvaluation.Plain,
            { Passing: ArgumentPassing.ParamArray, Elements: var elements } => i != state.last && elements.Any(ContainsCall)
                ? ArgumentEvaluation.Hoisted
                : ArgumentEvaluation.Plain,
            { Value: { } value } => EvaluationOf(value, i == state.last),
            _ => ArgumentEvaluation.Plain,
        });
    }

    // the order of the arguments given to the parameters, each the node of the one given to the parameter at its index, or null for one that calls nothing;
    // last is the index of the last one given.
    public static ArgumentOrder Of(IReadOnlyList<BoundNode?> arguments, int last)
        => Of(arguments.Count, (arguments, last), last >= 0 && arguments[last] is { } lastValue && ContainsCall(lastValue),
            static (state, i) => state.arguments[i] is { } value ? EvaluationOf(value, i == state.last) : ArgumentEvaluation.Plain);

    // the order of arguments that call nothing: right to left.
    public static ArgumentOrder InPlace(int count) => new(count, 0, 0, count > 64 ? new ArgumentEvaluation[count] : null, lastCalls: false);

    // the last argument is evaluated in place, a call or not.
    private static ArgumentEvaluation EvaluationOf(BoundNode value, bool isLast)
        => isLast ? ArgumentEvaluation.Plain
            : IsCall(value) ? ArgumentEvaluation.Call
            : ContainsCall(value) ? ArgumentEvaluation.Hoisted
            : ArgumentEvaluation.Plain;

    private static ArgumentOrder Of<TState>(int count, TState state, bool lastCalls, Func<TState, int, ArgumentEvaluation> evaluationOf)
    {
        if (count > 64)
        {
            var many = new ArgumentEvaluation[count];
            for (var i = 0; i < count; i++)
            {
                many[i] = evaluationOf(state, i);
            }

            return new ArgumentOrder(count, 0, 0, many, lastCalls);
        }

        var (calls, hoists) = (0UL, 0UL);
        for (var i = 0; i < count; i++)
        {
            switch (evaluationOf(state, i))
            {
                case ArgumentEvaluation.Call:
                    calls |= 1UL << i;
                    break;
                case ArgumentEvaluation.Hoisted:
                    hoists |= 1UL << i;
                    break;
            }
        }

        return new ArgumentOrder(count, calls, hoists, null, lastCalls);
    }

    // whether evaluating the node is a call: a procedure called, a member of an object, what is bound late. A record's field, an element of an array, a
    // variable, New, an argument left out (the one `A()` is written with), and what the standard library evaluates inline are not, whatever their operands are.
    public static bool IsCall(BoundNode node) => node switch
    {
        BoundCall call => !StdLibInline.IsInline(call.Procedure),
        BoundMember member => member.DeclaredClass is not null || member.Member is not null || member.IsLateBound,
        BoundName { Symbol: VBProcedureMemberSymbol or VBFunctionMemberSymbol or VBPropertyGetMemberSymbol } => true,
        BoundQualifiedName { Member: VBProcedureMemberSymbol or VBFunctionMemberSymbol or VBPropertyGetMemberSymbol } => true,
        BoundIndex { Callee: var callee } => IsObject(callee),
        BoundLate { Expression: MissingArgumentNode } => false,
        BoundDictionaryAccess or BoundLate or BoundError => true,
        _ => false,
    };

    // whether evaluating the node makes a call, itself or among its operands.
    public static bool ContainsCall(BoundNode node) => IsCall(node) || OperandsOf(node).Any(ContainsCall);

    // the calls the node makes that no other of them makes, left to right: the node itself when it is one.
    public static IEnumerable<BoundNode> CallsIn(BoundNode node)
    {
        if (IsCall(node))
        {
            yield return node;
            yield break;
        }

        foreach (var operand in OperandsOf(node))
        {
            foreach (var call in CallsIn(operand))
            {
                yield return call;
            }
        }
    }

    // the member calls of the target of a call statement whose arguments' calls are made after the calls among the statement's arguments, before its last
    // argument: the member the target calls, then its owner, while each is a member of an object bound early. The rest of the target is evaluated after the
    // arguments, and so is a function, an element of an array, a member bound late, and the target of a call inside an expression (observed in Excel).
    public static IEnumerable<BoundNode> TargetCallsIn(BoundNode target)
    {
        for (var node = target; node is not null;)
        {
            switch (node)
            {
                case BoundMember { IsLateBound: false } member when IsCall(member):
                    yield return member;
                    node = member.Owner;
                    break;
                case BoundIndex { Callee: BoundMember { IsLateBound: false } member } index when IsCall(member):
                    yield return index;
                    node = member.Owner;
                    break;
                default:
                    yield break;
            }
        }
    }

    // the objects the node makes with New that no call it makes does, left to right.
    public static IEnumerable<BoundNew> NewIn(BoundNode node)
    {
        if (node is BoundNew created)
        {
            yield return created;
            yield break;
        }

        if (IsCall(node))
        {
            yield break;
        }

        foreach (var operand in OperandsOf(node))
        {
            foreach (var each in NewIn(operand))
            {
                yield return each;
            }
        }
    }

    // the operands of a node that is no call, which it evaluates, left to right.
    private static IEnumerable<BoundNode> OperandsOf(BoundNode node)
    {
        switch (node)
        {
            case BoundBinary binary:
                yield return binary.Left;
                yield return binary.Right;
                break;
            case BoundUnary unary:
                yield return unary.Operand;
                break;
            case BoundTypeOf typeOf:
                yield return typeOf.Operand;
                break;
            case BoundArrayBound arrayBound:
                yield return arrayBound.Array;
                if (arrayBound.Dimension is { } dimension)
                {
                    yield return dimension;
                }

                break;
            case BoundArray array:
                foreach (var element in array.Elements)
                {
                    yield return element;
                }

                break;
            case BoundIndex index:
                yield return index.Callee;
                foreach (var argument in index.Arguments)
                {
                    yield return argument;
                }

                break;
            case BoundMember { Owner: { } owner }:
                yield return owner;
                break;
            case BoundCall { BoundArguments: { } arguments }:
                foreach (var argument in arguments)
                {
                    if (argument.Value is { } value)
                    {
                        yield return value;
                    }

                    foreach (var element in argument.Elements)
                    {
                        yield return element;
                    }
                }

                break;
        }
    }

    // an element of an object variable is its default member, called.
    private static bool IsObject(BoundNode callee)
        => callee is BoundVariable { Variable: ITypedSymbol { ResolvedType: VBClassType or VBObjectType } } || IsCall(callee);

    public Enumerator GetEnumerator() => new(this);

    // the arguments in the order they are evaluated in, each with whether it is evaluated or only its calls are made: first the calls, left to right, then
    // the rest, right to left.
    public struct Enumerator(ArgumentOrder order)
    {
        private bool _rest;
        private int _at = -1;

        public readonly ArgumentStep Current => new(_at, !_rest && order.Hoists(_at), _rest);

        public bool MoveNext()
        {
            if (!_rest)
            {
                while (++_at < order.Count)
                {
                    if (order.IsCall(_at) || order.Hoists(_at))
                    {
                        return true;
                    }
                }

                _rest = true;
            }

            while (--_at >= 0)
            {
                if (!order.IsCall(_at))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

// how an argument is evaluated (ArgumentOrder): with the calls, after them, or its calls with them and the rest after.
public enum ArgumentEvaluation
{
    Plain,

    Call,

    Hoisted,
}

// an argument of a call, by the index of its parameter, evaluated now; or, when CallsOnly, only the calls inside it made now. AfterCalls: the argument is
// evaluated right to left, after the calls.
public readonly record struct ArgumentStep(int Index, bool CallsOnly, bool AfterCalls);
