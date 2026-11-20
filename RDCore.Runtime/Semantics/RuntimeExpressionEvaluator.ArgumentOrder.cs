using System.Runtime.InteropServices;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.LanguageCore.Binding;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace RDCore.Runtime.Semantics;

public sealed partial class RuntimeExpressionEvaluator
{
    // the order the arguments mapped to the parameters of a call written with no bound arguments are evaluated in, by the map (MappedArguments).
    private readonly ConcurrentDictionary<ExpressionNode?[], ArgumentOrder> _mappedOrders = new(ReferenceEqualityComparer.Instance);

    // MS-VBAL §5.3.1.11 does not say when an argument is evaluated: the order MS-VBA evaluates them in (ArgumentOrder), of the arguments as they are bound
    // with the call, or as they are mapped to the parameters.
    private ArgumentOrder OrderOf(IRuntimeSession session, RuntimeEvaluationContext context, int count, ImmutableArray<BoundArgument> given,
        ExpressionNode?[]? mapped, ImmutableArray<ExpressionNode> paramArrayArguments)
    {
        if (!given.IsDefault)
        {
            return ArgumentOrder.Of(given);
        }

        if (mapped is null || count < 2)
        {
            return ArgumentOrder.InPlace(count);
        }

        return _mappedOrders.TryGetValue(mapped, out var order) ? order : _mappedOrders[mapped] = MappedOrder();

        ArgumentOrder MappedOrder()
        {
            var values = new BoundNode?[count];
            var last = paramArrayArguments.IsDefaultOrEmpty ? -1 : count - 1;
            for (var i = 0; i < Math.Min(count, mapped.Length); i++)
            {
                if (mapped[i] is not ({ } argument and not MissingArgumentNode))
                {
                    continue;
                }

                last = Math.Max(last, i);
                var value = WrittenValueOf(argument);
                values[i] = value is AddressOfExpressionNode ? null : BoundTreeOf(session, value, context);
            }

            return ArgumentOrder.Of(values, last);
        }
    }

    // MS-VBA evaluates the target of a member call after its arguments (observed in Excel): the calls among them are made, and the objects they make with
    // New are made, in the order the arguments are evaluated in, before the target is; what they evaluated to is known when they are bound. A target that
    // makes a call has every argument passed by value evaluated before it, as the call could change what the argument reads; and in a call statement, the
    // calls inside the arguments of the target's member calls are made before the last argument is evaluated (ArgumentOrder.TargetCallsIn). The arguments
    // are given as they are bound with the call, or as they are written. Null when the arguments call nothing and the target nothing either, or reads
    // nothing the target could change: the target is evaluated first. Otherwise what End lets go of.
    private ArgumentCalls? EvaluateArgumentsFirst(IRuntimeSession session, RuntimeEvaluationContext context, BoundNode? target,
        ImmutableArray<BoundArgument> given, ImmutableArray<ExpressionNode> written, bool statement, out RuntimeSemanticsEvaluationResult? error)
    {
        error = null;
        if (session.CallStack.Current is not CallStackFrame || given.IsDefault && written.IsDefaultOrEmpty)
        {
            return null;
        }

        var values = given.IsDefault ? WrittenValues(session, context, written) : default;
        var order = given.IsDefault ? ArgumentOrder.Of(values, LastOf(values)) : ArgumentOrder.Of(given);
        var targetCalls = target is not null && (ArgumentOrder.ContainsCall(target) || ArgumentOrder.NewIn(target).Any());
        if (!order.AnyCall && !(targetCalls && AnyRead()))
        {
            return null;
        }

        var calls = ArgumentCalls.Begin(session);
        var targetHoisted = !statement || target is null;
        foreach (var step in order)
        {
            if (!targetHoisted && step.AfterCalls)
            {
                targetHoisted = true;
                if ((error = HoistTargetCalls()) is not null)
                {
                    calls.End();
                    return null;
                }
            }

            calls.Open(step.Index);
            var evaluation = step.CallsOnly ? Evaluation.CallsOnly : order.IsCall(step.Index) ? Evaluation.Whole : Evaluation.Rest;
            if (given.IsDefault)
            {
                error = values[step.Index] is { } value ? Evaluated(value, evaluation, byValue: false) : null;
            }
            else if (given[step.Index] is { Value: { } bound } argument)
            {
                error = Evaluated(bound, evaluation, argument.Passing is ArgumentPassing.Value);
            }
            else
            {
                // the arguments a ParamArray collects are evaluated in place, left to right.
                foreach (var element in given[step.Index].Elements)
                {
                    if ((error = Evaluated(element, ArgumentOrder.IsCall(element) ? Evaluation.Whole : Evaluation.Rest, byValue: true)) is not null)
                    {
                        break;
                    }
                }
            }

            if (error is not null)
            {
                calls.End();
                return null;
            }
        }

        if (!targetHoisted && (error = HoistTargetCalls()) is not null)
        {
            calls.End();
            return null;
        }

        calls.Open(int.MaxValue);
        return calls;

        // a call, evaluated; the calls an argument that is none makes, ahead of the rest of it; or the rest of it, after the calls: evaluated where that
        // can be seen, which a reference passed as it is cannot be, but for the calls and the objects made with New in it - and before a target that makes
        // a call, a value read by an argument passed by value.
        RuntimeSemanticsEvaluationResult? Evaluated(BoundNode node, Evaluation evaluation, bool byValue)
        {
            if (evaluation is Evaluation.Whole
                || evaluation is Evaluation.Rest && !IsReference(node) && (ArgumentOrder.ContainsCall(node) || ArgumentOrder.NewIn(node).Any())
                || evaluation is Evaluation.Rest && targetCalls && node is not BoundLiteral && (!IsReference(node) || byValue && IsScalarVariable(node)))
            {
                return calls.Know(this, session, node, context);
            }

            if (calls.KnowCallsIn(this, session, context, node) is { } callFailed)
            {
                return callFailed;
            }

            if (evaluation is Evaluation.Rest)
            {
                foreach (var created in ArgumentOrder.NewIn(node))
                {
                    if (calls.Know(this, session, created, context) is { } failed)
                    {
                        return failed;
                    }
                }
            }

            return null;
        }

        // whether an argument reads what a call could change: one passed by value, or one that is no reference, but a literal.
        bool AnyRead()
            => given.IsDefault
                ? values.Any(static value => value is not null and not BoundLiteral && !IsReference(value))
                : given.Any(static argument => argument is { Passing: ArgumentPassing.Value, Value: not null and not BoundLiteral }
                    || argument.Elements.Any(static element => element is not BoundLiteral));

        // the calls inside the arguments of the target's member calls, made as the target's.
        RuntimeSemanticsEvaluationResult? HoistTargetCalls()
        {
            calls.Open(int.MaxValue);
            foreach (var member in ArgumentOrder.TargetCallsIn(target!))
            {
                foreach (var argument in ArgumentsOf(session, context, member))
                {
                    if (calls.KnowCallsIn(this, session, context, argument) is { } failed)
                    {
                        return failed;
                    }
                }
            }

            return null;
        }
    }

    // the arguments of a member call of a target, left to right, as they are bound with it or as they are written.
    private IEnumerable<BoundNode> ArgumentsOf(IRuntimeSession session, RuntimeEvaluationContext context, BoundNode member)
    {
        switch (member)
        {
            case BoundIndex index:
                foreach (var argument in index.Arguments)
                {
                    yield return argument;
                }

                break;
            case BoundMember { BoundArguments: { } bound }:
                foreach (var argument in bound)
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
            case BoundMember { Arguments: { } index }:
                foreach (var value in WrittenValues(session, context, index.Arguments))
                {
                    if (value is not null)
                    {
                        yield return value;
                    }
                }

                break;
        }
    }

    // a variable of a scalar type or a String, which an argument passed by value reads.
    private static bool IsScalarVariable(BoundNode node)
        => node is BoundVariable { Variable: ITypedSymbol { ResolvedType: var type } } && (ScalarPayload.IsScalar(type) || type is VBStringType);

    private enum Evaluation
    {
        Whole,
        CallsOnly,
        Rest,
    }

    // an argument a parameter passed by reference can be given as the variable, the element or the field it is: not evaluated ahead of the binding.
    private static bool IsReference(BoundNode node) => node is BoundVariable || node is BoundIndex or BoundMember && !ArgumentOrder.IsCall(node);

    private ImmutableArray<BoundNode?> WrittenValues(IRuntimeSession session, RuntimeEvaluationContext context, ImmutableArray<ExpressionNode> written)
    {
        if (written.IsDefaultOrEmpty)
        {
            return [];
        }

        var values = new BoundNode?[written.Length];
        for (var i = 0; i < written.Length; i++)
        {
            var value = WrittenValueOf(written[i]);
            values[i] = value is MissingArgumentNode or AddressOfExpressionNode ? null : BoundTreeOf(session, value, context);
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(values);
    }

    // what an argument as it is written evaluates to: the value of a named one, and the operand of one written ByVal (MS-VBAL §5.6.13.1).
    private static ExpressionNode WrittenValueOf(ExpressionNode argument)
    {
        var value = argument is NamedArgumentNode named ? named.Value : argument;
        return value is ByValArgumentExpressionNode byVal ? byVal.Operand : value;
    }

    private static int LastOf(ImmutableArray<BoundNode?> values)
    {
        var last = values.Length - 1;
        while (last >= 0 && values[last] is null)
        {
            last--;
        }

        return last;
    }

    // what the arguments of a call made while they are evaluated in MS-VBA's order (ArgumentOrder): the values of the calls made ahead of the rest of their
    // argument, which the calling activation knows until the call is bound (CallStackFrame.Hoist), and the temporaries each argument and the target made,
    // which are put in the order they are let go of when the statement is over (ObjectReferences.SettleCall). Kept for the next call when it is over.
    private sealed class ArgumentCalls
    {
        [ThreadStatic]
        private static ArgumentCalls? t_free;

        private readonly List<SyntaxNode> _hoisted = [];
        private readonly List<TemporaryPiece> _pieces = [];
        private ArgumentCalls? _next;
        private IRuntimeSession _session = null!;
        private CallStackFrame? _caller;
        private int _start;
        private (int Owner, int Start, int Times) _open;

        public static ArgumentCalls Begin(IRuntimeSession session)
        {
            var calls = t_free ?? new ArgumentCalls();
            t_free = calls._next;
            calls._next = null;
            calls._session = session;
            calls._caller = session.CallStack.Current as CallStackFrame;
            calls._start = session.Objects.TemporaryCount;
            calls._open = (-1, 0, 0);
            return calls;
        }

        // whether a call can be made ahead of the rest of its argument: the activation that makes the call knows what it returned.
        public bool CanHoist => _caller is not null;

        // the temporaries made from now on are those of the argument given to the parameter at owner, or the target's (int.MaxValue).
        public void Open(int owner)
        {
            Close();
            _open = (owner, _session.Objects.TemporaryCount, _caller?.SettledTemporaries.Times ?? 0);
        }

        // evaluates the node, and knows what it evaluated to until the call is bound; what was known of it already is not evaluated again.
        public RuntimeSemanticsEvaluationResult? Know(RuntimeExpressionEvaluator evaluator, IRuntimeSession session, BoundNode node,
            RuntimeEvaluationContext context)
        {
            if (_caller is not { } caller || caller.Known is { } known && (known.TryGet(node.Expression, out _) || known.TryGetObject(node.Expression, out _)))
            {
                return null;
            }

            var result = evaluator.EvaluateBoundNode(session, node, context);
            if (!result.IsSuccess)
            {
                return Unvalued(result);
            }

            if (result.Result is { } value)
            {
                caller.Hoist(node.Expression, DataValues.Copy(value));
                _hoisted.Add(node.Expression);
            }

            return null;
        }

        // the calls inside an argument, made ahead of the rest of it: the argument as it is bound with the call, or as it is written.
        public RuntimeSemanticsEvaluationResult? KnowCallsIn(RuntimeExpressionEvaluator evaluator, IRuntimeSession session, RuntimeEvaluationContext context,
            BoundArgument? argument, ExpressionNode? written)
        {
            if (argument is not null)
            {
                return (argument.Value is { } value ? KnowCallsIn(evaluator, session, context, value) : null)
                    ?? argument.Elements.Select(element => KnowCallsIn(evaluator, session, context, element))
                        .FirstOrDefault(static failed => failed is not null);
            }

            var node = written is null ? null : WrittenValueOf(written);
            return node is null or MissingArgumentNode or AddressOfExpressionNode ? null
                : KnowCallsIn(evaluator, session, context, evaluator.BoundTreeOf(session, node, context));
        }

        public RuntimeSemanticsEvaluationResult? KnowCallsIn(RuntimeExpressionEvaluator evaluator, IRuntimeSession session, RuntimeEvaluationContext context,
            BoundNode node)
        {
            foreach (var call in ArgumentOrder.CallsIn(node))
            {
                if (Know(evaluator, session, call, context) is { } failed)
                {
                    return failed;
                }
            }

            return null;
        }

        // the temporaries made since the call began are put in the order they are let go of.
        public void Settle()
        {
            Close();
            ObjectReferences.SettleCall(_session, _caller, _start, CollectionsMarshal.AsSpan(_pieces));
            _pieces.Clear();
        }

        // lets go of what the activation knew, once the call is bound, and keeps this for the next call.
        public void End()
        {
            foreach (var node in _hoisted)
            {
                _caller?.Unhoist(node);
            }

            _hoisted.Clear();
            _pieces.Clear();
            (_session, _caller) = (null!, null);
            (_next, t_free) = (t_free, this);
        }

        private void Close()
        {
            if (_open.Owner < 0)
            {
                return;
            }

            var end = _session.Objects.TemporaryCount;
            var settled = _caller is { SettledTemporaries: var made } && made.Times != _open.Times ? Math.Clamp(made.Count, _open.Start, end) : _open.Start;
            _pieces.Add(new TemporaryPiece(_open.Owner, _open.Start, settled, end));
            _open = (-1, 0, 0);
        }
    }
}
