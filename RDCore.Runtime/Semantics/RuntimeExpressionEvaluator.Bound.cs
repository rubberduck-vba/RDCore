using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Execution.Memory;
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
// is what is evaluated from then on. A node is evaluated by the run-time semantics of what it is - only what the binding decided is not decided again. What
// resolves only when it is evaluated (BoundLate), and the run-time counterpart of a compile error (BoundError), are evaluated late-bound (EvaluateLate).
public sealed partial class RuntimeExpressionEvaluator
{
    // a bound tree, and the scopes it was bound in: what a name is bound to is true of these scopes, and of no other.
    private sealed record class BoundRoot(ScopeTree Scopes, BoundNode Node);

    // keyed by the very expression - a node is a record, whose equality would compare trees - which a module that is loaded again parses anew. The table
    // holds what it binds for as long as the evaluator lives, which is one execution: a pipeline is composed for each run, evaluation and load, and goes
    // with it. That is also why it is not a ConditionalWeakTable, whose lookup costs as much as what binding saves a read.
    private readonly ConcurrentDictionary<ExpressionNode, BoundRoot> _boundRoots = new(ReferenceEqualityComparer.Instance);

    // the bound tree of an expression, bound now when it is not bound yet, or was bound in scopes that are no longer the session's.
    private BoundNode BoundTreeOf(IRuntimeSession session, ExpressionNode expression, RuntimeEvaluationContext context)
    {
        var scopes = session.Symbols.Scopes;
        if (_boundRoots.TryGetValue(expression, out var root) && ReferenceEquals(root.Scopes, scopes))
        {
            return root.Node;
        }

        var node = Bind(session, scopes, expression, context);
        _boundRoots[expression] = new BoundRoot(scopes, node);
        return node;
    }

    // the binding is the static evaluation of the expression, in the scope the evaluation resolves its names from. An expression written where the session
    // defines no scope - a procedure it does not know, whose names its resolver finds all the same - is bound in an empty scope of that procedure, as the
    // static pass types an owner it has no scope for. Its variables are bound to the slots of the procedure it is written in, when that is the procedure the
    // current activation is of (a Uri's equality ignores its fragment, which is what tells two procedures of a module apart).
    private BoundNode Bind(IRuntimeSession session, ScopeTree scopes, ExpressionNode expression, RuntimeEvaluationContext context)
    {
        if (!scopes.TryGetScope(context.Scope, out var scope))
        {
            scope = new LexicalScope(context.Scope, LexicalScopeKind.Procedure, null, []);
        }

        var frame = session.CallStack.Current is CallStackFrame { Procedure: { } procedure } && procedure.Uri.AbsoluteUri == context.Scope.AbsoluteUri
            ? FrameLayout.For(procedure)
            : null;

        return ExpressionBinder.Bind(StaticContextOf(session, scope, context), expression, frame).NodeOf(expression)
            ?? new BoundLate(expression);
    }

    /// <summary>
    /// The bound tree of an expression written in the body of <paramref name="procedure"/>, its variables bound to the slots of the procedure's layout: what
    /// compiled code is emitted from (IR lowering S2.1), whatever activation is current.
    /// </summary>
    internal BoundNode BindInProcedure(IRuntimeSession session, ExpressionNode expression, Symbol procedure)
    {
        var context = new RuntimeEvaluationContext(procedure.Uri);
        if (!session.Symbols.Scopes.TryGetScope(context.Scope, out var scope))
        {
            scope = new LexicalScope(context.Scope, LexicalScopeKind.Procedure, null, []);
        }

        return ExpressionBinder.Bind(StaticContextOf(session, scope, context), expression, FrameLayout.For(procedure)).NodeOf(expression)
            ?? new BoundLate(expression);
    }

    // a member written with no object before the dot is a member of the target of the enclosing With block, typed as the static pass types it: by the
    // declared type of the block's expression.
    private static StaticEvaluationContext StaticContextOf(IRuntimeSession session, LexicalScope scope, RuntimeEvaluationContext context)
    {
        var staticContext = new StaticEvaluationContext(session.Symbols.Resolver, scope, Environment: new StaticEnvironment(session.Environment.Is64Bit));
        return context.EnclosingWithTargetExpression is { } withTarget
            ? staticContext with { EnclosingWithTargetType = ExpressionStaticSemanticsEvaluator.Evaluate(staticContext with { Facts = null, Bindings = null }, withTarget).Result }
            : staticContext;
    }

    /// <summary>
    /// The declared type of an expression, as the static pass types it in the scope it is written in: what an assignment to it let-coerces to.
    /// </summary>
    /// <returns><see langword="null"/> when the session defines no scope there, or the expression has no declared type.</returns>
    internal VBType? DeclaredTypeOf(IRuntimeSession session, ExpressionNode expression, RuntimeEvaluationContext context)
        => session.Symbols.Scopes.TryGetScope(context.Scope, out var scope)
            ? ExpressionStaticSemanticsEvaluator.Evaluate(StaticContextOf(session, scope, context) with { Facts = null, Bindings = null }, expression).Result
            : null;

    // a variable of the current activation is read through its slot; any other - a Static variable, a field, a variable of an activation the slot is not
    // of - is looked up, as a name that is not bound is.
    private RuntimeSemanticsEvaluationResult ReadBoundVariable(IRuntimeSession session, BoundVariable variable, bool boundsOnly = false)
    {
        var (handle, referenced) = BindingOf(session, variable);
        return ReadVariable(session, (ITypedSymbol)variable.Variable, handle, referenced, variable.IsAutoInstantiated, boundsOnly);
    }

    /// <summary>
    /// Evaluates what an index expression indexes, or an assignment to one of its elements: a variable declared a fixed-size array is read for its bounds,
    /// which its declaration makes certain, when an evaluation <see cref="AssumesVariables">assumes</see> what variables hold.
    /// </summary>
    /// <param name="session">The session the expression is evaluated in.</param>
    /// <param name="callee">The callee of the index expression.</param>
    /// <param name="context">The scope it is written in.</param>
    internal RuntimeSemanticsEvaluationResult EvaluateIndexed(IRuntimeSession session, ExpressionNode callee, RuntimeEvaluationContext context)
        => AssumesVariables && BoundVariableOf(session, callee, context) is { } variable
            ? ReadBoundVariable(session, variable, boundsOnly: true)
            : Evaluate(session, callee, context);

    /// <summary>
    /// The variable a statement's target names, bound (IR lowering S0.11): what a <c>Set</c>, an <c>LSet</c> or an <c>RSet</c> writes to, found once.
    /// </summary>
    /// <param name="session">The session the statement runs in.</param>
    /// <param name="target">The target.</param>
    /// <param name="context">The scope the target is written in.</param>
    /// <returns>The bound variable; <see langword="null"/> for a target that is not a simple name, or names no variable - a function's result, which
    /// is the activation's.</returns>
    internal BoundVariable? BoundVariableOf(IRuntimeSession session, ExpressionNode target, RuntimeEvaluationContext context)
        => target is SimpleNameExpressionNode ? BoundTreeOf(session, target, context) as BoundVariable : null;

    /// <summary>
    /// The binding of a bound variable in the current activation - through its slot, when it has one - and the declared type of the variable a
    /// <c>Variant</c> reference parameter is bound to, if it is one (<strong>MS-VBAL §5.3.1.11</strong>).
    /// </summary>
    /// <param name="session">The session the variable lives in.</param>
    /// <param name="variable">The bound variable.</param>
    internal (IBindingHandle Handle, VBType? ReferencedType) BindingOf(IRuntimeSession session, BoundVariable variable)
    {
        if (variable.Slot is { } slot && session.CallStack.Current is CallStackFrame frame
            && frame.TryReadSlot(slot, variable.Variable, out var handle, out var referenced))
        {
            return (handle, referenced);
        }

        // a variable of the session - of a module, global, or a Static local - is the same cell from wherever it is read, for as long as nothing is
        // allocated, freed or rebound in storage: resolved once for each place it is named, rather than looked up by its symbol each time (S1.5).
        if (IsSessionVariable(variable.Variable, session.CallStack.Current) && session.Storage is SessionStorage storage)
        {
            if (_sessionCells.TryGetValue(variable, out var known) && ReferenceEquals(known.Storage, storage) && known.Generation == storage.Generation)
            {
                return (known.Cell, null);
            }

            var cell = session.Symbols.Resolver.GetValue(variable.Variable);
            _sessionCells[variable] = new SessionCell(storage, storage.Generation, cell);
            return (cell, null);
        }

        return (session.Symbols.Resolver.GetValue(variable.Variable),
            session.CallStack.Current is { } current && current.TryGetReferencedType(variable.Variable, out var type) ? type : null);
    }

    // a variable whose storage is the session's, which no activation and no object holds: one of a module, a global, a Static local of a procedure that
    // is a call on no object - one of a member of a class is the object's, a cell for each object.
    private static bool IsSessionVariable(Symbol variable, ICallStackFrame? frame)
        => variable.ScopeKind is ScopeKind.Module or ScopeKind.Global || variable is VBLocalVariableSymbol { IsStatic: true } && frame?.Target is null;

    private sealed record class SessionCell(SessionStorage Storage, int Generation, IBindingHandle Cell);

    private readonly ConcurrentDictionary<BoundVariable, SessionCell> _sessionCells = new(ReferenceEqualityComparer.Instance);

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

    private RuntimeSemanticsEvaluationResult EvaluateBoundNode(IRuntimeSession session, BoundNode node, RuntimeEvaluationContext context) => node switch
    {
        BoundLiteral literal => LiteralExpressionRuntimeSemantics.Instance.Evaluate(session, new(), literal.Literal),
        BoundVariable variable => ReadBoundVariable(session, variable),
        BoundName name => ReadSymbol(session, context, name.Symbol, nameOfEnclosingFunctionIsItsResult: true),
        BoundBinary binary => EvaluateBoundBinary(session, binary, context),
        BoundUnary unary => EvaluateBoundUnary(session, unary, context),
        BoundCall call => EvaluateBoundCall(session, call, context),
        BoundMember member => member.IsLateBound ? AsVariant(EvaluateBoundMember(session, member, context)) : EvaluateBoundMember(session, member, context),
        BoundNew created => CreateObject(session, created.New, created.Class),
        BoundTypeOf typeOf => EvaluateBoundTypeOf(session, typeOf, context),
        BoundIndex index => IndexValue(session, context, index.Index, index.Index,
            AssumesVariables && index.Callee is BoundVariable indexed ? ReadBoundVariable(session, indexed, boundsOnly: true) : EvaluateBoundNode(session, index.Callee, context)),
        BoundInstance instance => ReadInstance(session, instance),
        BoundArrayBound arrayBound => EvaluateBoundArrayBound(session, arrayBound, context),
        BoundArray array => EvaluateBoundArray(session, array, context),
        BoundDictionaryAccess dictionary => EvaluateBoundDictionaryAccess(session, dictionary, context),
        BoundQualifiedName qualified => ReadSymbol(session, context, qualified.Member, nameOfEnclosingFunctionIsItsResult: false),
        BoundPrecompilerName precompilerName => EvaluatePrecompilerConstant(session, context, precompilerName.Name),
        // a compile error evaluates to its run-time counterpart: the expression, late-bound.
        BoundError error => EvaluateBoundNode(session, error.Late, context),
        BoundLate late => EvaluateLate(session, late.Expression, context),
        _ => RuntimeSemanticsEvaluationResult.InternalError(),
    };

    // what EvaluateArrayBound does, its operands evaluated through their nodes.
    private RuntimeSemanticsEvaluationResult EvaluateBoundArrayBound(IRuntimeSession session, BoundArrayBound arrayBound, RuntimeEvaluationContext context)
    {
        var array = EvaluateBoundNode(session, arrayBound.Array, context);
        if (!array.IsSuccess)
        {
            return array;
        }

        VBTypedValue? dimension = null;
        if (arrayBound.Dimension is { } dimensionNode)
        {
            var evaluated = EvaluateBoundNode(session, dimensionNode, context);
            if (!evaluated.IsSuccess)
            {
                return evaluated;
            }

            dimension = evaluated.Result;
        }

        return ArrayBoundOf(arrayBound.ArrayBound, array.Result!, dimension);
    }

    // what EvaluateArray does, its elements evaluated through their nodes.
    private RuntimeSemanticsEvaluationResult EvaluateBoundArray(IRuntimeSession session, BoundArray array, RuntimeEvaluationContext context)
    {
        var lower = session.CallStack.Current?.Directives.Base ?? 0;
        var value = new VBResizableArrayValue([(lower, lower + array.Elements.Length - 1)], VBVariantType.TypeInfo);
        for (var index = 0; index < array.Elements.Length; index++)
        {
            var element = EvaluateBoundNode(session, array.Elements[index], context);
            if (!element.IsSuccess)
            {
                return element;
            }

            SetArrayElement(value, lower + index, element.Result!);
        }

        return RuntimeSemanticsEvaluationResult.Success(new VBVariantValue(value));
    }

    // what EvaluateDictionaryAccess does, its owner evaluated through its node - or the target of the enclosing With block, for one written with none.
    private RuntimeSemanticsEvaluationResult EvaluateBoundDictionaryAccess(IRuntimeSession session, BoundDictionaryAccess dictionary, RuntimeEvaluationContext context)
    {
        var owner = dictionary.Owner is { } ownerNode
            ? EvaluateBoundNode(session, ownerNode, context)
            : context.EnclosingWithTarget is { } withTarget
                ? RuntimeSemanticsEvaluationResult.Success(withTarget)
                : RuntimeSemanticsEvaluationResult.InternalError();
        return owner.IsSuccess ? AccessThroughDictionary(session, context, dictionary.DictionaryAccess, owner.Result!) : owner;
    }

    // Me is the object the activation was invoked on: what its parameter holds, read through its slot.
    private static RuntimeSemanticsEvaluationResult ReadInstance(IRuntimeSession session, BoundInstance instance)
        => RuntimeSemanticsEvaluationResult.Success(new VBObjectValue(
            instance.Slot is { } slot && session.CallStack.Current is CallStackFrame frame && frame.TryReadSlot(slot, instance.Me, out var handle, out _)
                ? handle
                : session.Symbols.Resolver.GetValue(instance.Me)));

    // what EvaluateMemberAccess and EvaluateIndex do with a member of an object declared as a class, the member resolved once (S0.10): the owner is evaluated
    // once, and the member the object's class invokes for it is found once for each class the expression meets. Anything else - no object, an object the
    // session has no instance of, a member the class does not have - is what the late-bound evaluation does with the owner it was given.
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
        else if (bound.DeclaredClass is not null
            && UnwrappedOwner(owner) is VBObjectValue objectValue && session.Symbols.TryGetInstance(objectValue.Value, out var instance)
            && ImplementationOf(bound, instance.ClassModule) is { } member)
        {
            // a public variable of the object is read, as EvaluateInstanceField reads it - and what follows it in parentheses indexes it.
            if (member.Kind is SymbolKindExt.Field or SymbolKindExt.Variable)
            {
                result = ReadVariable(session, member, instance.GetValue(member), referenced: null, member.GetProperty(SymbolProperties.AutoInstantiated));
                isCall = false;
            }
            else
            {
                // the arguments are bound for the member of the class the object is declared as: the member implementing it has parameters of its own.
                result = InvokeProcedure(session, context, member, bound.Arguments?.Arguments ?? [], objectValue.RuntimeValue,
                    ReferenceEquals(member, bound.Member) ? bound.BoundArguments : null);
            }
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

    // a public variable of a class by name, which a member that is no procedure of the class reads (EvaluateInstanceField).
    private static VBTypeMemberSymbol? FieldOf(VBClassModuleSymbol actual, string name)
        => actual.DefaultInterfaceMembers.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)) is
            { Kind: SymbolKindExt.Field or SymbolKindExt.Variable } field ? field : null;

    // what EvaluateTypeOfIs does, the class tested against resolved once.
    private RuntimeSemanticsEvaluationResult EvaluateBoundTypeOf(IRuntimeSession session, BoundTypeOf typeOf, RuntimeEvaluationContext context)
    {
        var operand = EvaluateBoundNode(session, typeOf.Operand, context);
        return operand.IsSuccess ? IsOfClass(session, operand, typeOf.Class, typeOf.TypeOf) : operand;
    }

    // the member each bound member expression invoked last, and the class of the object it was invoked on: an expression meets objects of one class, nearly
    // always, so the member is found once.
    private readonly ConcurrentDictionary<BoundMember, (VBClassModuleSymbol Class, VBTypeMemberSymbol? Member)> _memberSites = new(ReferenceEqualityComparer.Instance);

    // the member an object of `actual` invokes for a member of the class the expression is declared as (MS-VBAL §5.3.1.9): the member itself, of an object of
    // that class; the one implementing it, of a class that implements it as an interface; or the class's own member by that name, as it is found
    // late-bound.
    private VBTypeMemberSymbol? ImplementationOf(BoundMember bound, VBClassModuleSymbol actual)
    {
        if (_memberSites.TryGetValue(bound, out var site) && ReferenceEquals(site.Class, actual))
        {
            return site.Member;
        }

        // only a member of an object declared as a class is resolved here (EvaluateBoundMember asks of no other).
        var name = bound.Access.Member.IdentifierName;
        var declared = bound.DeclaredClass!.Uri.AbsoluteUri;
        var member = ReferenceEquals(actual, bound.DeclaredClass)
            ? bound.Member
            : (actual.ImplementedInterfaces.FirstOrDefault(implemented => implemented.Uri.AbsoluteUri == declared) is { } implementedInterface
                ? ImplementationIn(actual, implementedInterface, name)
                : null) ?? InvocableMemberOf(actual, name) ?? FieldOf(actual, name);

        _memberSites[bound] = (actual, member);
        return member;
    }

    private RuntimeSemanticsEvaluationResult EvaluateBoundBinary(IRuntimeSession session, BoundBinary binary, RuntimeEvaluationContext context)
    {
        // MS-VBAL §5.6.9.3: a Single, Double or Date operation of operands of declared types is computed in the extended format (ExtendedIntermediates),
        // and so is an operand of an operation that is not one.
        if (IsWide(binary))
        {
            return EvaluateStored(session, binary, context);
        }

        if (PlanOf(binary) is { IsDeferred: false } plan && (IsWide(binary.Left) || IsWide(binary.Right)))
        {
            return EvaluateWithWideOperand(session, binary, plan, context);
        }

        // an operand that raised an error raises it with no value: a Variant operation of it assigns nothing (MS-VBA: e = s * 10 + v leaves e - s42 E06).
        var leftResult = EvaluateBoundNode(session, binary.Left, context);
        if (!leftResult.IsSuccess)
        {
            return Unvalued(leftResult);
        }

        var rightResult = EvaluateBoundNode(session, binary.Right, context);
        if (!rightResult.IsSuccess)
        {
            return Unvalued(rightResult);
        }

        return OperatorProvider.EvaluateBinaryOperator(session, binary.Operation, leftResult.Result!, rightResult.Result!);
    }

    // what EvaluateIndex does with a callee that resolves to a procedure, its callee resolved once: a function that declares no parameter is not given what
    // follows it in parentheses, which indexes what it returns - and that is evaluated late-bound.
    // Abs, Int, Fix, CSng and CDbl of an extended intermediate are computed on it, CByte to CBool convert it, and Sgn takes its sign, none storing it first
    // (ExtendedIntermediates).
    private RuntimeSemanticsEvaluationResult EvaluateBoundCall(IRuntimeSession session, BoundCall call, RuntimeEvaluationContext context)
        => IsWide(call) ? EvaluateStored(session, call, context)
            : EvaluateWideConversion(session, call, context) is { } converted ? converted
            : call.Procedure is VBTypeMemberSymbol procedure && !ReturnsToBeIndexed(procedure, null, call.Call.Arguments)
                ? OfResultType(call, InvokeProcedure(session, context, procedure, call.Call.Arguments, boundArguments: call.BoundArguments))
                : EvaluateLate(session, call.Expression, context);

    // what a member reached late-bound returns is a Variant (BoundMember.IsLateBound): a scalar it returns is held by one. An object, an array, a record,
    // and a Variant already, are what they are.
    private static RuntimeSemanticsEvaluationResult AsVariant(RuntimeSemanticsEvaluationResult evaluated)
        => evaluated is { IsSuccess: true, Result: { } value } && value is not (VBVariantValue or VBObjectValue or VBArrayValue or VBUserDefinedTypeValue or SDK.Model.Values.VBVoidValue)
            ? RuntimeSemanticsEvaluationResult.Success(value.IsIndeterminate ? new VBVariantValue(value).AsIndeterminate() : new VBVariantValue(value))
            : evaluated;

    // a call of a declared type its procedure does not declare - Abs, Int, Fix of a number (ArgumentTypedFunctions) - returns a value of it: the Variant the
    // procedure returns holds one, which is what an operation of the call operates on, as MS-VBA has it (oracle\s46 F10: `Fix(n) * 7000` overflows).
    private static RuntimeSemanticsEvaluationResult OfResultType(BoundCall call, RuntimeSemanticsEvaluationResult returned)
        => call.ResultType is { } typed && returned is { IsSuccess: true, Result: VBVariantValue { TypedValue: var held } } && held.TypeInfo.Name == typed.Name
            ? RuntimeSemanticsEvaluationResult.Success(held)
            : returned;

    private RuntimeSemanticsEvaluationResult EvaluateBoundUnary(IRuntimeSession session, BoundUnary unary, RuntimeEvaluationContext context)
    {
        if (IsWide(unary))
        {
            return EvaluateStored(session, unary, context);
        }

        // Not of an extended intermediate: converted to the type it is coerced to, from its 80 bits.
        if (PlanOf(unary) is { IsDeferred: false } plan && IsWide(unary.Operand))
        {
            var converted = EvaluateOperandOf(session, unary.Operand, plan.OperandDestination, context);
            return converted.IsSuccess ? OperatorProvider.EvaluateUnaryOperator(session, unary.Operation, converted.Result!) : converted;
        }

        var operandResult = EvaluateBoundNode(session, unary.Operand, context);
        return operandResult.IsSuccess
            ? OperatorProvider.EvaluateUnaryOperator(session, unary.Operation, operandResult.Result!)
            : operandResult;
    }
}
