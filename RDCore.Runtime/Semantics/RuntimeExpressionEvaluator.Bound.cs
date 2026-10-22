using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.Literals;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Binding;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.Semantics;

// The evaluation of bound trees (IR lowering §3.3, S0.8a): what an expression binds to is decided once, the first time it is evaluated, and the bound tree
// is what is evaluated from then on. A node is evaluated by the same semantics that evaluate it from the syntax tree - only what the binding decided is not
// decided again.
public sealed partial class RuntimeExpressionEvaluator
{
    // a bound tree, and the scopes it was bound in: what a name is bound to is true of these scopes, and of no other.
    private sealed record class BoundRoot(ScopeTree Scopes, BoundNode Node);

    // keyed by the very expression - a node is a record, whose equality would compare trees - which a module that is loaded again parses anew. The table
    // holds what it binds for as long as the evaluator lives, which is one execution: a pipeline is composed for each run, evaluation and load, and goes
    // with it. That is also why it is not a ConditionalWeakTable, whose lookup costs as much as what binding saves a read.
    private readonly ConcurrentDictionary<ExpressionNode, BoundRoot> _boundRoots = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Whether every expression that has no effect of its own is evaluated twice - from its bound tree and from its syntax tree - and the two results
    /// compared, an <see cref="InvalidOperationException"/> being thrown when they differ (IR lowering S0.8a).
    /// </summary>
    /// <remarks>
    /// 🧪 For the tests only, which turn it on for the whole suite. An expression has an effect when it calls something, an object's default member
    /// included, or when it is evaluated by an analysis, which counts what it assumes: those are evaluated once.
    /// </remarks>
    internal static bool ComparesBoundEvaluation { get; set; }

    // the bound tree of an expression, bound now when it is not bound yet, or was bound in scopes that are no longer the session's.
    private BoundNode BoundTreeOf(IRuntimeSession session, ExpressionNode expression, RuntimeEvaluationContext context)
    {
        var scopes = session.Symbols.Scopes;
        if (_boundRoots.TryGetValue(expression, out var root) && ReferenceEquals(root.Scopes, scopes))
        {
            return root.Node;
        }

        var node = Bind(session, scopes, expression, context);        _boundRoots[expression] = new BoundRoot(scopes, node);
        return node;
    }

    // the layout of the activations of each definition of a procedure, made once for it: a procedure defined again is another symbol, with a layout of its own.
    private readonly ConditionalWeakTable<Symbol, FrameLayout> _layouts = [];

    // the binding is the static evaluation of the expression, in the scope the evaluation resolves its names from. An expression with no such scope - one
    // that is not written in code the session defines - is not bound. Its variables are bound to the slots of the procedure it is written in, when that is
    // the procedure the current activation is of (a Uri's equality ignores its fragment, which is what tells two procedures of a module apart).
    private BoundNode Bind(IRuntimeSession session, ScopeTree scopes, ExpressionNode expression, RuntimeEvaluationContext context)
    {
        if (!scopes.TryGetScope(context.Scope, out var scope))
        {
            return new BoundFallback(expression);
        }

        var frame = session.CallStack.Current is CallStackFrame { Procedure: { } procedure } && procedure.Uri.AbsoluteUri == context.Scope.AbsoluteUri
            ? _layouts.GetValue(procedure, FrameLayout.Of)
            : null;

        // a member written with no object before the dot is a member of the target of the enclosing With block, typed as the static pass types it: by the
        // declared type of the block's expression.
        var staticContext = new StaticEvaluationContext(session.Symbols.Resolver, scope);
        if (context.EnclosingWithTargetExpression is { } withTarget)
        {
            staticContext = staticContext with { EnclosingWithTargetType = ExpressionStaticSemanticsEvaluator.Evaluate(staticContext with { Facts = null, Bindings = null }, withTarget).Result };
        }

        return ExpressionBinder.Bind(staticContext, expression, frame).NodeOf(expression)
            ?? new BoundFallback(expression);
    }

    // a variable of the current activation is read through its slot; any other - a Static variable, a field, a variable of an activation the slot is not
    // of - is looked up, as a name that is not bound is.
    private RuntimeSemanticsEvaluationResult ReadBoundVariable(IRuntimeSession session, BoundVariable variable)
    {
        var (handle, referenced) = BindingOf(session, variable);
        return ReadVariable(session, (ITypedSymbol)variable.Variable, handle, referenced, variable.IsAutoInstantiated);
    }

    // the binding of a variable in the current activation, and the declared type of the variable a Variant reference parameter is bound to, if it is one.
    private static (IBindingHandle Handle, VBType? ReferencedType) BindingOf(IRuntimeSession session, BoundVariable variable)
        => variable.Slot is { } slot && session.CallStack.Current is CallStackFrame frame
            && frame.TryReadSlot(slot, variable.Variable, out var handle, out var referenced)
                ? (handle, referenced)
                : (session.Symbols.Resolver.GetValue(variable.Variable),
                    session.CallStack.Current is { } current && current.TryGetReferencedType(variable.Variable, out var type) ? type : null);

    /// <summary>
    /// Let-coerces a value by the plan of its coercion (<see cref="Plans.PlanExecutor.TryConvert"/>), when nothing observes the evaluation and the value and
    /// the destination are ones the plan can be executed for: what an argument passed to a <c>ByVal</c> parameter, and the value assigned to a function's
    /// result, are let-coerced by (IR lowering S0.9).
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="destination">The declared type it is let-coerced to.</param>
    /// <param name="converted">The let-coerced value.</param>
    /// <returns><see langword="false"/> when the coercion is to be evaluated by its strategy: an analysis observes it, the plan does not take the value, or
    /// the coercion fails - which the strategy raises.</returns>
    internal bool TryConvertUnobserved(VBTypedValue value, VBType destination, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out VBTypedValue? converted)
    {
        converted = null;
        return Observation is null && !AssumesVariables && Plans.PlanExecutor.IsPlannedDestination(destination)
            && Plans.PlanExecutor.TryConvert(value, destination, out converted);
    }

    /// <summary>
    /// Let-assigns a value to the variable a name is bound to (IR lowering S0.8c): through its slot, by the plan of its let-coercion
    /// (<see cref="Plans.PlanExecutor.TryConvert"/>), if the target and the value are ones the plan can be executed for.
    /// </summary>
    /// <param name="session">The session the assignment is evaluated in.</param>
    /// <param name="target">The expression assigned to.</param>
    /// <param name="value">The value assigned.</param>
    /// <param name="context">The scope the target is written in.</param>
    /// <returns>
    /// The value as it was let-coerced and stored; or <see langword="null"/> when the assignment is to be evaluated the way it always is - which is also how
    /// a coercion that fails raises its error, and what an analysis observes. A target that is not a variable (a function's result, a property, a member,
    /// an element), a variable of an object type (whose default member is assigned), and a Variant reference parameter bound to a variable of another type
    /// (which is assigned as that type) are not assigned here.
    /// </returns>
    internal RuntimeSemanticsEvaluationResult? TryAssignBound(IRuntimeSession session, ExpressionNode target, VBTypedValue value, RuntimeEvaluationContext context)
    {
        // a target whose type no plan converts to - a Variant, an object, an array, a user-defined type, a fixed-length string - is declined before anything
        // is looked up for it, which the assignment that is evaluated instead would look up again.
        if (Observation is not null || AssumesVariables || target is not SimpleNameExpressionNode
            || BoundTreeOf(session, target, context) is not BoundVariable { Variable: ITypedSymbol { ResolvedType: var declared } typed } variable
            || !Plans.PlanExecutor.IsPlannedDestination(declared)
            || !Plans.PlanExecutor.TryConvert(value, declared, out var coerced))
        {
            return null;
        }

        var (handle, referenced) = BindingOf(session, variable);
        if (referenced is not null || !handle.BindingCapabilities.HasFlag(BindingCapabilities.SetValue))
        {
            return null;
        }

        // a Variant may hold an object it was Set to, which it lets go of now that it holds a number (MS-VBAL §5.3.1.10); no other type a plan
        // converts to can hold one.
        var before = declared is VBVariantType ? Execution.ObjectReferences.HeldIn(session, handle) : Execution.ObjectReferences.Held.None;

        // what the plan converts to is a number, a Boolean, a date or a String, known: no array to store boxed, and nothing assumed.
        handle.Store(session.Symbols.Resolver, coerced.RuntimeValue, isKnown: true);
        if (declared is VBVariantType)
        {
            Execution.ObjectReferences.Rebind(session, handle, before);
        }

        return RuntimeSemanticsEvaluationResult.Success(coerced);
    }

    private RuntimeSemanticsEvaluationResult EvaluateBound(IRuntimeSession session, BoundNode node, RuntimeEvaluationContext context)
    {
        var result = EvaluateBoundNode(session, node, context);
        if (ComparesBoundEvaluation && !AssumesVariables && Observation is null && HasNoEffect(node))
        {
            var expected = EvaluateSyntax(session, node.Expression, context);
            if (Outcome(result) != Outcome(expected))
            {
                throw new InvalidOperationException(
                    $"The bound tree of the {node.Expression.GetType().Name} at {node.Expression.Location.Range} evaluated to {Outcome(result)}, and its syntax tree to {Outcome(expected)}.");
            }
        }

        return result;
    }

    private RuntimeSemanticsEvaluationResult EvaluateBoundNode(IRuntimeSession session, BoundNode node, RuntimeEvaluationContext context) => node switch
    {
        BoundLiteral literal => LiteralExpressionRuntimeSemantics.Instance.Evaluate(session, new(), literal.Literal),
        BoundVariable variable => ReadBoundVariable(session, variable),
        BoundName name => ReadSymbol(session, context, name.Symbol, nameOfEnclosingFunctionIsItsResult: true),
        BoundBinary binary => EvaluateBoundBinary(session, binary, context),
        BoundUnary unary => EvaluateBoundUnary(session, unary, context),
        BoundCall call => EvaluateBoundCall(session, call, context),
        BoundMember member => EvaluateBoundMember(session, member, context),
        BoundNew created => CreateObject(session, created.New, created.Class),
        _ => EvaluateSyntax(session, node.Expression, context),
    };

    // what EvaluateMemberAccess and EvaluateIndex do with a member of an object declared as a class, the member resolved once (S0.10): the owner is evaluated
    // once, and the member the object's class invokes for it is found once for each class the expression meets. Anything else - no object, an object the
    // session has no instance of, a member the class does not have - is what the syntax tree's evaluation does with the owner it was given.
    private RuntimeSemanticsEvaluationResult EvaluateBoundMember(IRuntimeSession session, BoundMember bound, RuntimeEvaluationContext context)
    {
        // no owner: the object is the target of the enclosing With block.
        var ownerResult = bound.Owner is { } ownerNode ? EvaluateBoundNode(session, ownerNode, context) : EvaluateOwner(session, context, bound.Access);
        if (!ownerResult.IsSuccess)
        {
            return ownerResult;
        }

        var owner = ownerResult.Result!;
        var memberName = bound.Access.Member.IdentifierName;
        var assumedBefore = AssumedSoFar;

        RuntimeSemanticsEvaluationResult result;
        var isCall = true;
        if (ObjectNotSet(bound.Access, owner) is { } notSet)
        {
            result = notSet;
        }
        else if (UnwrappedOwner(owner) is VBObjectValue objectValue && session.Symbols.TryGetInstance(objectValue.Value, out var instance)
            && ImplementationOf(bound, instance.ClassModule) is { } member)
        {
            result = InvokeProcedure(session, context, member, bound.Arguments?.Arguments ?? [], objectValue.RuntimeValue);
        }
        else if (bound.Arguments is { } call)
        {
            (result, isCall) = CallMember(session, context, call, bound.Access, owner);
        }
        else
        {
            result = AccessMember(session, context, bound.Access, owner, memberName);
        }

        ObserveMemberAccess(session, context, bound.Access, Tokens.MemberAccess, bound.Access.Owner, owner, memberName,
            MemberAccessOperationSemanticFlags.Explicit, assumedBefore, result);

        // a public variable of the object, given arguments, is what they index.
        return isCall || bound.Arguments is not { } index ? result : IndexValue(session, context, index, index, result);
    }

    // the member each bound member expression invoked last, and the class of the object it was invoked on: an expression meets objects of one class, nearly
    // always, so the member is found once.
    private readonly ConcurrentDictionary<BoundMember, (VBClassModuleSymbol Class, VBTypeMemberSymbol? Member)> _memberSites = new(ReferenceEqualityComparer.Instance);

    // the member an object of `actual` invokes for a member of the class the expression is declared as (MS-VBAL §5.3.1.9): the member itself, of an object of
    // that class; the one implementing it, of a class that implements it as an interface; or the class's own member by that name, as the syntax tree's
    // evaluation finds it.
    private VBTypeMemberSymbol? ImplementationOf(BoundMember bound, VBClassModuleSymbol actual)
    {
        if (_memberSites.TryGetValue(bound, out var site) && ReferenceEquals(site.Class, actual))
        {
            return site.Member;
        }

        var name = bound.Access.Member.IdentifierName;
        var declared = bound.DeclaredClass.Uri.AbsoluteUri;
        var member = ReferenceEquals(actual, bound.DeclaredClass)
            ? bound.Member
            : (actual.ImplementedInterfaces.FirstOrDefault(implemented => implemented.Uri.AbsoluteUri == declared) is { } implementedInterface
                ? ImplementationIn(actual, implementedInterface, name)
                : null) ?? InvocableMemberOf(actual, name);

        _memberSites[bound] = (actual, member);
        return member;
    }

    private RuntimeSemanticsEvaluationResult EvaluateBoundBinary(IRuntimeSession session, BoundBinary binary, RuntimeEvaluationContext context)
    {
        var leftResult = EvaluateBoundNode(session, binary.Left, context);
        if (!leftResult.IsSuccess)
        {
            return leftResult;
        }

        var rightResult = EvaluateBoundNode(session, binary.Right, context);
        if (!rightResult.IsSuccess)
        {
            return rightResult;
        }

        return OperatorProvider.EvaluateBinaryOperator(session, binary.Operation, leftResult.Result!, rightResult.Result!);
    }

    // what EvaluateIndex does with a callee that resolves to a procedure, its callee resolved once: a function that declares no parameter is not given what
    // follows it in parentheses, which indexes what it returns - and that is evaluated from the syntax tree.
    private RuntimeSemanticsEvaluationResult EvaluateBoundCall(IRuntimeSession session, BoundCall call, RuntimeEvaluationContext context)
        => call.Procedure is VBTypeMemberSymbol procedure && !ReturnsToBeIndexed(procedure, null, call.Call.Arguments)
            ? InvokeProcedure(session, context, procedure, call.Call.Arguments)
            : EvaluateSyntax(session, call.Expression, context);

    private RuntimeSemanticsEvaluationResult EvaluateBoundUnary(IRuntimeSession session, BoundUnary unary, RuntimeEvaluationContext context)
    {
        var operandResult = EvaluateBoundNode(session, unary.Operand, context);
        return operandResult.IsSuccess
            ? OperatorProvider.EvaluateUnaryOperator(session, unary.Operation, operandResult.Result!)
            : operandResult;
    }

    // a literal, and a variable or a constant of an intrinsic type, have none; nor does an operation of them, whose operands are no object with a default
    // member to call.
    private static bool HasNoEffect(BoundNode node) => node switch
    {
        BoundLiteral => true,
        BoundVariable { IsAutoInstantiated: false, Variable: ITypedSymbol { ResolvedType: VBNumericType or VBStringType or VBBooleanType or VBDateType } } => true,
        BoundName { Symbol: ITypedSymbol { ResolvedType: VBNumericType or VBStringType or VBBooleanType or VBDateType } and not VBTypeMemberSymbol } => true,
        BoundName { Symbol: VBTypeMemberSymbol { Kind: SymbolKindExt.Field or SymbolKindExt.Variable or SymbolKindExt.Constant } member }
            => member is ITypedSymbol { ResolvedType: VBNumericType or VBStringType or VBBooleanType or VBDateType },
        BoundBinary binary => HasNoEffect(binary.Left) && HasNoEffect(binary.Right),
        BoundUnary unary => HasNoEffect(unary.Operand),
        _ => false,
    };

    private static string Outcome(RuntimeSemanticsEvaluationResult result)
        => result.IsSuccess ? $"{result.Result!.TypeInfo.Name}({result.Result.RuntimeValue.BoxedValue})" : $"error {result.ErrorInfo?.ErrorId}";
}
