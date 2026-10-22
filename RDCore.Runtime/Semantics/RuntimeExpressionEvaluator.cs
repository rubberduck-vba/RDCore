using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.External;
using RDCore.Runtime.Semantics.Expressions;
using System.Collections.Concurrent;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Literals;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Binding;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;
using System.Collections.Immutable;

namespace RDCore.Runtime.Semantics;

/// <summary>
/// Recursively evaluates a real, arbitrarily-nested expression tree at runtime — the runtime analogue
/// of <see cref="ExpressionStaticSemanticsEvaluator"/>. Where that walker determines a declared
/// <c>VBType</c>, this one produces the actual bound <see cref="VBTypedValue"/>, dispatching by the
/// node's own C# type (and, for operator nodes, by <c>Token</c>) to the matching runtime-semantics
/// rule, evaluating children first and short-circuiting the moment one is not a
/// <see cref="RuntimeSemanticsEvaluationResult.IsSuccess"/> — an error or an internal error alike, so a
/// gap deep in a tree never gets mistaken for a valid operand further up.
/// </summary>
/// <remarks>
/// Literal/<c>Me</c>/<c>New</c>/every operator are wired straight to their runtime semantics.
/// <c>SimpleName</c> reads a variable/constant's current bound value. <c>Index</c> resolves array-element
/// access (<c>Callee</c> evaluating to a <see cref="VBArrayValue"/>). <c>MemberAccess</c> reads a
/// <c>Field</c>/<c>Variable</c>-kind member off a live object instance. <c>TypeOfIs</c> checks the
/// operand's actual runtime class against the named class/interface, walking
/// <see cref="VBClassModuleSymbol.ImplementedInterfaces"/>.
/// <para>
/// Reading a value and invoking a procedure are different operations: a bare reference to a
/// <c>Function</c>/<c>Property Get</c>, a call through <c>MemberAccess</c> whose target isn't a plain
/// field, <c>DictionaryAccess</c> (<strong>MS-VBAL §5.6.14</strong>'s sugar for a call through the
/// owner's default member), and <c>AddressOf</c> (which must never be evaluated as a value read at all —
/// its whole point is to reference a procedure, not call it) all return
/// <see cref="RuntimeSemanticsEvaluationResult.InternalError"/> here rather than being misread as values.
/// A bare reference to a <c>Sub</c>, and an <c>Index</c> whose <c>Callee</c> is a bare name resolving to
/// one — the S9a walking skeleton's own scope — invoke it instead, ByVal parameters only, same module,
/// no <c>Me</c>: see <c>InvokeSub</c>. <c>ProcedureInvoker</c> is <c>null</c>-checked at each of those two
/// call sites rather than required, so every existing caller that never passes one keeps working exactly
/// as before — those two sites just fall back to <c>InternalError</c> the same way they always did.
/// </para>
/// <para>
/// A jump statement's own label operand is never evaluated here — <see cref="LabelOperands"/> reads it
/// directly, the same way <see cref="StatementStaticSemanticsEvaluator"/> does; a label is not a symbol.
/// </para>
/// <para>
/// Every operator's runtime semantics comes from <see cref="IOperatorRuntimeSemanticsProvider"/> - one
/// instance per operator token, owned by the provider and reused for every evaluation, never rebuilt on
/// the fly.
/// </para>
/// </remarks>
public sealed partial class RuntimeExpressionEvaluator(IOperatorRuntimeSemanticsProvider OperatorProvider)
{
    /// <summary>
    /// The invoker a bare <c>Sub</c> call (S9a's own scope) runs through. Settable rather than a
    /// constructor parameter, to break the circular dependency wiring one naturally creates:
    /// <c>RuntimeProcedureInvoker</c> itself needs a <c>ProcedureExecutor</c>, built from a
    /// <c>StatementRuntimeSemanticsProvider</c>, built from THIS evaluator — so the evaluator has to
    /// exist first, and this gets assigned once every other collaborator is composed. <c>null</c> until
    /// then, matching every existing caller that never needs procedure calls at all.
    /// </summary>
    public IProcedureInvoker? ProcedureInvoker { get; set; }

    /// <summary>
    /// Binds a member to whatever runs it - the workspace's own invoker, or the dispatcher that reaches
    /// outside it. Settable for the same construction-order reason as <see cref="ProcedureInvoker"/>.
    /// </summary>
    public ICallableBindingFactory? Bindings { get; set; }

    /// <summary>
    /// Let-coerces each argument of a bare <c>Sub</c> call to its own parameter's declared type
    /// (<strong>MS-VBAL §5.5.1.2</strong>) before passing it to <see cref="ProcedureInvoker"/> - ByVal
    /// parameter passing is itself a Let-target, the same as any other. Settable for the same
    /// construction-order reason as <see cref="ProcedureInvoker"/>.
    /// </summary>
    public ILetCoercionRuntimeSemanticsProvider? LetCoercionProvider { get; set; }

    /// <summary>
    /// Set-coerces an object argument to the declared class of its parameter (<strong>MS-VBAL §5.3.1.11</strong>:
    /// "the argument's data value is Set-assigned to the new local variable"), where a Let-coercion would take the value
    /// of the object's default member instead. Settable for the same construction-order reason as
    /// <see cref="LetCoercionProvider"/>; without one, an object argument is Let-coerced as before.
    /// </summary>
    public ISetCoercionRuntimeSemantics? SetCoercion { get; set; }

    /// <summary>
    /// Evaluates <paramref name="expression"/>, recursively evaluating its children first wherever a
    /// rule needs their bound values as operands.
    /// </summary>
    /// <param name="session">The runtime session the expression is evaluated against.</param>
    /// <param name="expression">The expression to evaluate — may be arbitrarily nested.</param>
    /// <param name="context">
    /// The scope a <c>SimpleName</c>, <c>Me</c>, <c>New</c>, or <c>MemberAccess</c>/<c>DictionaryAccess</c>
    /// with-relative expression resolves from.
    /// </param>
    /// <returns>
    /// The result of the first rule (own or a child's) that failed, or the final value when the whole
    /// tree evaluates successfully.
    /// </returns>
    public RuntimeSemanticsEvaluationResult Evaluate(IRuntimeSession session, ExpressionNode expression, RuntimeEvaluationContext context)
        => BoundTreeOf(session, expression, context) is { } bound and not BoundFallback
            ? EvaluateBound(session, bound, context)
            : EvaluateSyntax(session, expression, context);

    // the evaluation of the syntax tree, which is what an expression the binder does not bind yet is evaluated by (IR lowering D3).
    private RuntimeSemanticsEvaluationResult EvaluateSyntax(IRuntimeSession session, ExpressionNode expression, RuntimeEvaluationContext context)
        => expression switch
        {
            LiteralExpressionNode => LiteralExpressionRuntimeSemantics.Instance.Evaluate(session, new(), expression),
            SimpleNameExpressionNode simpleName => EvaluateSimpleName(session, context, simpleName),
            PrecompilerNameExpressionNode precompilerName => EvaluatePrecompilerConstant(session, context, precompilerName),
            InstanceExpressionNode => EvaluateInstance(session, context, expression),
            NewExpressionNode newExpression => EvaluateNew(session, context, expression, newExpression),
            MemberAccessExpressionNode memberAccess => EvaluateMemberAccess(session, context, expression, memberAccess),
            IndexExpressionNode indexExpression => EvaluateIndex(session, context, expression, indexExpression),
            DictionaryAccessExpressionNode dictionaryAccess => EvaluateDictionaryAccess(session, context, expression, dictionaryAccess),
            TypeOfIsExpressionNode typeOfIs => EvaluateTypeOfIs(session, context, expression, typeOfIs),
            ArrayBoundExpressionNode arrayBound => EvaluateArrayBound(session, context, arrayBound),
            ArrayExpressionNode array => EvaluateArray(session, context, array),
            VBBinaryOperatorExpressionNode binaryOperator => EvaluateBinaryOperator(session, context, binaryOperator),
            VBUnaryOperatorExpressionNode unaryOperator => EvaluateUnaryOperator(session, context, unaryOperator),
            // ByVal flags how the argument is passed, which is what a node that is not a variable is: a value, never
            // aliased to a ByRef parameter (see TryResolveByRefArgument).
            ByValArgumentExpressionNode byVal => Evaluate(session, byVal.Operand, context),
            _ => RuntimeSemanticsEvaluationResult.InternalError(),
        };

    // The Array keyword: a Variant holding a resizable array of Variant, with an element for each argument. Its lower bound is the
    // Option Base of the module the expression is written in, which rides on the executing frame; the library's own member of the
    // same name is always zero-based, and is what a qualified call reaches instead.
    private RuntimeSemanticsEvaluationResult EvaluateArray(
        IRuntimeSession session, RuntimeEvaluationContext context, ArrayExpressionNode arrayExpression)
    {
        var lower = session.CallStack.Current?.Directives.Base ?? 0;
        var array = new VBResizableArrayValue([(lower, lower + arrayExpression.Elements.Length - 1)], VBVariantType.TypeInfo);

        for (var index = 0; index < arrayExpression.Elements.Length; index++)
        {
            var element = Evaluate(session, arrayExpression.Elements[index], context);
            if (!element.IsSuccess)
            {
                return element;
            }

            SetArrayElement(array, lower + index, element.Result!);
        }

        return RuntimeSemanticsEvaluationResult.Success(new VBVariantValue(array));
    }

    // an element is a Variant holding a copy of what the argument came to - not a view of the variable it was read from, which would be whatever that
    // variable holds next. The array is a value the expression is, not a variable: an object in it is held by whatever the array is assigned to, which is
    // given a copy in cells of its own (ObjectReferences), and not by these.
    private static void SetArrayElement(VBResizableArrayValue array, int index, VBTypedValue element)
    {
        var value = DataValues.Copy(element is VBVariantValue variant ? variant : new VBVariantValue(element));
        array.TrySetElement(BindingKnowledge.NewCell(value), index);
    }

    // MS-VBAL 3.3.5.2: the array operand is evaluated as the expression it is - for a variable, that is the array it
    // holds (VBArrayType.CreateValue hands back the stored instance, not a copy) - and only its bounds are read.
    private RuntimeSemanticsEvaluationResult EvaluateArrayBound(
        IRuntimeSession session, RuntimeEvaluationContext context, ArrayBoundExpressionNode arrayBound)
    {
        var array = Evaluate(session, arrayBound.Array, context);
        if (!array.IsSuccess)
        {
            return array;
        }

        VBTypedValue? dimension = null;
        if (arrayBound.Dimension is { } dimensionExpression)
        {
            var evaluated = Evaluate(session, dimensionExpression, context);
            if (!evaluated.IsSuccess)
            {
                return evaluated;
            }

            dimension = evaluated.Result;
        }

        return ArrayBoundOf(arrayBound, array.Result!, dimension);
    }

    // an array that is not known has bounds that are not known, and is not known to be no array: the error it would raise is the assumed value's, which is
    // not stated. The same goes for a dimension that is not known.
    private static RuntimeSemanticsEvaluationResult ArrayBoundOf(ArrayBoundExpressionNode arrayBound, VBTypedValue array, VBTypedValue? dimension)
        => array.IsIndeterminate || dimension is { IsIndeterminate: true }
            ? RuntimeSemanticsEvaluationResult.Success(VBLongType.TypeInfo.CreateIndeterminateValue())
            : ArrayBoundRuntimeSemantics.Evaluate(arrayBound, array, dimension);

    private RuntimeSemanticsEvaluationResult EvaluateSimpleName(IRuntimeSession session, RuntimeEvaluationContext context, SimpleNameExpressionNode simpleName)
        => ReadSymbol(
            session, context, session.Symbols.Resolver.ResolveValue(simpleName, ScopeKind.Local, context.Scope).Symbol,
            nameOfEnclosingFunctionIsItsResult: true);

    /// <summary>
    /// What a name that resolved to <paramref name="symbol"/> evaluates to when nothing supplies arguments to it: a
    /// call of a Sub, Function or Property Get with none, a constant's value, or a variable's.
    /// </summary>
    /// <remarks>
    /// Shared by a bare name and a name qualified by a project or module (<strong>MS-VBAL §5.6.12</strong>), which
    /// resolve differently and then mean the same thing. They differ in one rule only:
    /// <paramref name="nameOfEnclosingFunctionIsItsResult"/>.
    /// </remarks>
    private RuntimeSemanticsEvaluationResult ReadSymbol(
        IRuntimeSession session, RuntimeEvaluationContext context, Symbol? symbol, bool nameOfEnclosingFunctionIsItsResult)
    {
        if (symbol is VBProcedureMemberSymbol sub)
        {
            // a bare reference to a Sub, with no enclosing Index to supply arguments, is a call with
            // none (MS-VBAL §5.6.10) - "Foo" alone, or Call Foo's own Callee.
            return InvokeProcedure(session, context, sub, []);
        }

        if (symbol is VBFunctionMemberSymbol or VBPropertyGetMemberSymbol)
        {
            var returningMember = (VBTypeMemberSymbol)symbol;
            // Deliberately NOT VBReturningMemberSymbol (its own base type): that also covers
            // Const/EnumConst/module-and-instance fields/UDT fields, every one of them a value to fold or
            // read below, not a call - a pre-existing bug this fix surfaced (never reachable before,
            // since nothing had read a module-level field by bare name until S9a's own tests did).
            //
            // A bare reference to the ENCLOSING Function/Property Get's own name - context.Scope is
            // always that procedure's own Uri (RuntimeProcedureInvoker.Invoke's own RuntimeEvaluationContext
            // construction), so resolving the SAME name from that SAME scope and getting the SAME symbol
            // back proves self-reference - reads its function result variable (MS-VBAL §5.3.1) instead of
            // recursing; Foo(args), even with zero args via Call Foo(), goes through EvaluateIndex's own
            // TryResolveCallableSub before ever reaching here, which is the only way to actually recurse.
            // A bare reference to any OTHER Function/Property Get is also an implicit call (§5.6.10), with
            // none of its own arguments to supply. Only a BARE name is the function's result variable: `Module1.Foo`
            // names the function, from anywhere, and is a call.
            return nameOfEnclosingFunctionIsItsResult
                && returningMember.Uri.AbsoluteUri == context.Scope.AbsoluteUri && session.CallStack.Current is { } enclosing
                    ? Assumed(RuntimeSemanticsEvaluationResult.Success(enclosing.ReturnValue!))
                    : InvokeProcedure(session, context, returningMember, []);
        }

        // MS-VBAL 5.4.3.2 / 5.2.3.3: a Const statically evaluates to a value and is substituted at each
        // of its use sites, so the session allocates it no storage at all - reading one through
        // GetValue below threw "no runtime binding exists yet" for a module Const, and a local Const
        // never even reached the host. Its folded value stands in here instead.
        if (ConstantValueOf(symbol) is { } constantValue)
        {
            return FoldConstant(session, context, symbol!, constantValue);
        }

        // a member of an enumeration that a library or the standard library stated has its value on the symbol, there being no declaration to fold
        // and no storage: MS-VBAL §5.2.3.4 gives an enumeration member the type Long, and a value of a library that does not fit in one wraps as VBA's does.
        if (symbol is VBEnumConstMemberSymbol && symbol.TryGetProperty(SymbolProperties.EnumValue, out var enumValue))
        {
            return Assumed(RuntimeSemanticsEvaluationResult.Success(new VBLongValue(unchecked((int)enumValue))));
        }

        if (symbol is VBEnumConstMemberSymbol { DeclaredValue: { } declaredValue } workspaceMember)
        {
            return EnumMemberValueOf(session, workspaceMember, declaredValue);
        }

        // a constant with no value to fold or state has no storage either, so there is nothing below for it to read:
        // a Const that carries no expression, and an Enum's own name evaluated as a value. Each is an interpreter gap,
        // not a program error, and GetValue would throw for want of a binding.
        if (symbol is { Kind: SymbolKindExt.Constant or SymbolKindExt.EnumMember or SymbolKindExt.Enum })
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        // static semantics should already have rejected an unresolved, ambiguous, or duplicate name;
        // reaching here means that check was skipped.
        if (symbol is not ITypedSymbol typed)
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        return ReadVariable(session, typed, session.Symbols.Resolver.GetValue(symbol),
            session.CallStack.Current is { } frame && frame.TryGetReferencedType(symbol, out var referenced) ? referenced : null,
            symbol.GetProperty(SymbolProperties.AutoInstantiated));
    }

    /// <summary>
    /// The value of a variable, read through its binding: what a name that resolves to a variable evaluates to, whether it is resolved now or was bound
    /// already (<see cref="SDK.Semantics.Binding.BoundVariable"/>).
    /// </summary>
    /// <param name="session">The session the variable is read in.</param>
    /// <param name="variable">The variable.</param>
    /// <param name="handle">Its binding, in the current activation.</param>
    /// <param name="referenced">The declared type of the variable a Variant reference parameter is bound to, if the variable is such a parameter.</param>
    /// <param name="isAutoInstantiated">Whether the variable is declared <c>As New</c>.</param>
    private RuntimeSemanticsEvaluationResult ReadVariable(
        IRuntimeSession session, ITypedSymbol variable, IBindingHandle handle, VBType? referenced, bool isAutoInstantiated)
    {
        // MS-VBAL §5.3.1.11: a Variant reference parameter bound to a variable of another type "is treated as having a declared type of Variant": what it reads is a
        // Variant, holding the variable's value as the type the variable is declared to be.
        if (referenced is not null)
        {
            return Assumed(RuntimeSemanticsEvaluationResult.Success(new VBVariantValue(referenced.CreateValue(handle.ForReading()))));
        }

        var value = variable.ResolvedType.CreateValue(handle.ForReading());

        // MS-VBAL §5.2.3.1.4 / §2.5.1: a variable declared As New - a class module's default instance among them - is
        // never Nothing when it is referred to: the reference creates the object it was waiting for, which is also
        // why `Is Nothing` of it can never be true.
        return isAutoInstantiated && value is VBObjectValue { } held && held.IsNothing()
            && variable.ResolvedType is VBClassType { Symbol: { } classModule }
                ? Assumed(AutoInstantiate(session, handle, classModule))
                : Assumed(RuntimeSemanticsEvaluationResult.Success(value));
    }

    /// <summary>
    /// Whether what a variable holds is assumed rather than read: a read of a variable gives the value its declared type allows, which is not known
    /// (<see cref="VBTypedValue.IsIndeterminate"/>), whatever the variable was last assigned.
    /// </summary>
    /// <remarks>
    /// 👉 An evaluation of code that does not follow its paths cannot say which assignment a read sees. A value that was assigned along one path is no
    /// fact about a read on another, so no read is trusted; what is written is still written, and the evaluation of the assignment is still observed.
    /// </remarks>
    public bool AssumesVariables { get; set; }

    /// <summary>
    /// The observation of the analysis the evaluator belongs to, if it belongs to one: told of every member the code accesses, and of every read that is
    /// <see cref="AssumesVariables">assumed</see>.
    /// </summary>
    public AnalysisObservation? Observation { get; set; }

    private RuntimeSemanticsEvaluationResult Assumed(RuntimeSemanticsEvaluationResult read)
    {
        if (!AssumesVariables || !read.IsSuccess || read.Result is not { } value)
        {
            return read;
        }

        Observation?.Assume();
        return RuntimeSemanticsEvaluationResult.Success(value.AsIndeterminate());
    }

    /// <summary>
    /// How many times the evaluation has assumed something, so far: what <see cref="ObserveMemberAccess"/> is given, taken before the member is accessed.
    /// </summary>
    internal long AssumedSoFar => Observation?.Assumptions ?? 0;

    /// <summary>
    /// States a member access that was just evaluated as a <see cref="MemberAccessFact"/>, if the evaluation is observed.
    /// </summary>
    /// <remarks>
    /// The object the member is accessed on carries what it is, and nothing of how it is declared, which is what makes an access late-bound: the declared type
    /// of the expression it is reached through is asked of the same rules that type it at compile time, as an interface member is.
    /// </remarks>
    /// <param name="session">The session the access was evaluated in.</param>
    /// <param name="context">The scope of the access; its <c>With</c> block, for an access that names no object.</param>
    /// <param name="access">The expression the access is evaluated for.</param>
    /// <param name="token">The operator of the access: <see cref="Tokens.MemberAccess"/> or <see cref="Tokens.DictionaryAccess"/>.</param>
    /// <param name="ownerExpression">The expression of the object, or <see langword="null"/> for the object of the enclosing <c>With</c> block.</param>
    /// <param name="owner">The object, as it was evaluated.</param>
    /// <param name="memberName">The name the code gives the member, or <see langword="null"/> when it gives none.</param>
    /// <param name="kind">What the access is, of what the code writes: <c>Explicit</c>, or <c>Implicit</c> with the member it implies.</param>
    /// <param name="assumedBefore">What <see cref="AssumedSoFar"/> was before the member was accessed.</param>
    /// <param name="result">What the access evaluated to: its error, if it is the code's, is the fact's.</param>
    internal void ObserveMemberAccess(
        IRuntimeSession session,
        RuntimeEvaluationContext context,
        ExpressionNode access,
        string token,
        ExpressionNode? ownerExpression,
        VBTypedValue? owner,
        string? memberName,
        MemberAccessOperationSemanticFlags kind,
        long assumedBefore,
        RuntimeSemanticsEvaluationResult result)
    {
        if (Observation is not { IsSuspended: false } observation)
        {
            return;
        }

        var flags = kind | (ownerExpression is null ? MemberAccessOperationSemanticFlags.WithBlockVariable : 0);
        var declared = DeclaredTypeOfOwner(session, context, ownerExpression ?? context.EnclosingWithTargetExpression);

        VBTypeMemberSymbol? member = null;
        if (declared is VBVariantType or VBObjectType)
        {
            flags |= MemberAccessOperationSemanticFlags.LateBound;
        }
        else if (declared is IVBMemberOwnerType { Members: var members })
        {
            var defaultMember = (declared as VBClassType)?.DefaultMember;
            member = memberName is not null
                ? members.FirstOrDefault(candidate => string.Equals(candidate.Name, memberName, StringComparison.OrdinalIgnoreCase))
                : kind.HasFlag(MemberAccessOperationSemanticFlags.DefaultMember) ? defaultMember
                : kind.HasFlag(MemberAccessOperationSemanticFlags.NewEnumMember) && declared is VBClassType { Symbol: var classModule } ? EnumerationMemberOf(classModule)
                : null;
            if (member is not null && defaultMember is not null && defaultMember.SemanticId == member.SemanticId)
            {
                flags |= MemberAccessOperationSemanticFlags.DefaultMember;
            }
        }

        // the error is the access's when the object it is accessed on is known, and nothing it took to access the member was assumed.
        var isKnown = owner is { IsIndeterminate: false };
        var error = isKnown && observation.Assumptions == assumedBefore ? StatedErrors.Of(result.ErrorInfo) : null;

        observation.OnOperation(new MemberAccessFact(
            access.Identity,
            access.Location,
            token,
            (member as ITypedSymbol)?.ResolvedType,
            isKnown,
            error,
            memberName ?? member?.Name,
            flags));
    }

    /// <summary>
    /// States the access of the enumeration member of the object a <c>For Each</c> goes through (<strong>MS-VBAL 5.4.2.4</strong>) as a
    /// <see cref="MemberAccessFact"/>, if the evaluation is observed: an array is enumerated without one, and so is not.
    /// </summary>
    /// <param name="session">The session the statement was evaluated in.</param>
    /// <param name="context">The scope of the statement.</param>
    /// <param name="collectionExpression">The expression of what is enumerated.</param>
    /// <param name="collection">What it evaluated to.</param>
    /// <param name="assumedBefore">What <see cref="AssumedSoFar"/> was before the enumeration began.</param>
    /// <param name="error">What the enumeration raised, if it did.</param>
    internal void ObserveEnumeration(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode collectionExpression, VBTypedValue collection, long assumedBefore, IVBRaisableError? error)
    {
        if (Observation is null || UnwrappedOwner(collection) is VBArrayValue)
        {
            return;
        }

        // an object that is not known is still declared as one: anything else that is not known may be an array.
        if (UnwrappedOwner(collection) is not VBObjectValue
            && !(collection.IsIndeterminate && DeclaredTypeOfOwner(session, context, collectionExpression) is VBClassType or VBObjectType))
        {
            return;
        }

        ObserveMemberAccess(session, context, collectionExpression, Tokens.MemberAccess, collectionExpression, collection, null,
            MemberAccessOperationSemanticFlags.Implicit | MemberAccessOperationSemanticFlags.NewEnumMember, assumedBefore,
            error is null ? RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void) : RuntimeSemanticsEvaluationResult.Error(error));
    }

    /// <summary>
    /// Whether <paramref name="expression"/> is classified as a variable: a name of a variable, a field of the module or a parameter; that name qualified
    /// by its module (<c>Module1.Kept</c>); a field of a record (<c>r.Item</c>); a public variable of an object whose class is declared (<c>obj.Child</c>);
    /// an element of an array (<c>items(1)</c>).
    /// </summary>
    /// <remarks>
    /// Anything else is taken to be a value - <c>New</c>, a call, a property, and a member of an object that is late bound, which MS-VBAL classifies as an
    /// unbound member. Classified by the declared types the static semantics give the expression, not by evaluating it: a call in it is made once, by
    /// whoever evaluates it.
    /// </remarks>
    internal bool DenotesAVariable(IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode expression) => expression switch
    {
        SimpleNameExpressionNode name => IsAVariable(session.Symbols.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope).Symbol),
        MemberAccessExpressionNode access when ResolveNamespaceQualified(session, context, access) is { } qualified => IsAVariable(qualified),
        MemberAccessExpressionNode { Owner: { } owner } access => DeclaredTypeOfOwner(session, context, owner) switch
        {
            VBUserDefinedType => true,
            VBClassType declared => declared.Members.FirstOrDefault(member
                => member.Name.Equals(access.Member.IdentifierName, StringComparison.OrdinalIgnoreCase)) is { Kind: SymbolKindExt.Variable or SymbolKindExt.Field },
            _ => false,
        },
        IndexExpressionNode { Callee: var callee } => DeclaredTypeOfOwner(session, context, callee) is VBArrayType,
        _ => false,
    };

    private static bool IsAVariable(Symbol? symbol) => symbol is { Kind: SymbolKindExt.Variable or SymbolKindExt.Field } or VBParameterSymbol;

    // the declared type of an expression, as the static semantics read it - without evaluating it, which a caller that only needs to classify an
    // expression must not do a second time (a call in it would be made twice).
    internal static VBType? DeclaredTypeOfOwner(IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode? ownerExpression)
    {
        if (ownerExpression is null)
        {
            return null;
        }

        var scope = new LexicalScope(context.Scope, LexicalScopeKind.Procedure, null, []);
        return ExpressionStaticSemanticsEvaluator.Evaluate(new StaticEvaluationContext(session.Symbols.Resolver, scope), ownerExpression).Result;
    }

    private static RuntimeSemanticsEvaluationResult AutoInstantiate(IRuntimeSession session, IBindingHandle handle, VBClassModuleSymbol classModule)
    {
        // a declared type carries the class as it was when the type was built, and the class is what the instance is
        // made from: its members at the moment of the reference.
        // A class of a library is looked up by that library, as the name that declared it was qualified or resolved to it: only a name that is not qualified is
        // decided by the order the references are in, and the class here has already been decided.
        if (VBProjectSymbol.ResolveClass(session.Symbols.Resolver, classModule) is { } current)
        {
            classModule = current;
        }

        var objectId = session.Objects.CreateObject();
        session.Symbols.CreateInstance(objectId, classModule);
        if (ExternalObjectCreation.Create(session, classModule, objectId, default) is { } notCreated)
        {
            return notCreated;
        }

        var created = new VBObjectValue(objectId);

        // the variable holds the object before Initialize runs, as it holds one a Set stored: the handler can already
        // reach it through the variable, and the reference must not be lost if it does.
        var before = ObjectReferences.HeldIn(session, handle);
        handle.SetValue(session.Symbols.Resolver, created.RuntimeValue);
        ObjectReferences.Rebind(session, handle, before);

        if (session.Lifecycle?.Initialize(objectId) is { IsSuccess: false } failed)
        {
            return failed;
        }

        return RuntimeSemanticsEvaluationResult.Success(created);
    }

    // the project or procedural module an expression names, when it names one (MS-VBAL §5.6.12) - decided by the
    // classification the compiler uses, so that what is compiled as a namespace is run as one.
    private static Symbol? TryClassifyNamespace(IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode expression)
        => session.Symbols.Resolver.NamespaceOf(expression, context.Scope);

    // the enumeration an expression names - `Mode`, or `Widgets.Mode` - when it names one: classified by what the name resolves to, as a namespace is.
    private static VBEnumType? TryClassifyEnumeration(IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode expression)
    {
        var named = expression switch
        {
            SimpleNameExpressionNode name => session.Symbols.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope).Symbol,
            MemberAccessExpressionNode { Owner: { } owner } access when TryClassifyNamespace(session, context, owner) is { } qualifier
                => ResolveNamespaceMember(session, context, qualifier, access),
            _ => null,
        };

        return named is VBEnumMemberSymbol { ResolvedType: VBEnumType enumeration } ? enumeration : null;
    }

    /// <summary>
    /// What a member access names when its owner is a project or a procedural module (<strong>MS-VBAL §5.6.12</strong>) - <c>Module1.Count</c>, a
    /// variable of the module - rather than a member of a value: the symbol the name means unqualified there, for an assignment to write to as it
    /// writes to that bare name.
    /// </summary>
    /// <remarks>
    /// A member access bound as a member of a value (<see cref="BoundMember"/>: its owner is a variable, or an object of a class) is none, which the
    /// binding decided once (IR lowering S0.11): its owner is not classified again.
    /// </remarks>
    /// <returns>The member, or <see langword="null"/> when the owner is no namespace, or names no such member.</returns>
    internal Symbol? ResolveNamespaceQualified(IRuntimeSession session, RuntimeEvaluationContext context, MemberAccessExpressionNode memberAccess)
        => memberAccess.Owner is { } owner && BoundTreeOf(session, memberAccess, context) is not BoundMember
            && TryClassifyNamespace(session, context, owner) is { } qualifier
            ? ResolveNamespaceMember(session, context, qualifier, memberAccess)
            : null;

    // the member of a namespace a qualified name refers to, or null when there is none to refer to - which static
    // semantics should have rejected, as it should an unresolved bare name.
    private static Symbol? ResolveNamespaceMember(
        IRuntimeSession session, RuntimeEvaluationContext context, Symbol qualifier, MemberAccessExpressionNode access)
        => session.Symbols.Resolver.ResolveMember(qualifier, access.Member, context.Scope).Symbol is { } member
            && !NamespaceExpressions.IsNamespace(member) ? member : null;

    // null for anything that is not a Const, and for a Const whose declaration carried no expression. No
    // constant has a binding to read instead: a member of a library's enumeration has its value on the
    // symbol (ReadSymbol), and one that has neither is an internal error.
    private static ExpressionNode? ConstantValueOf(Symbol? symbol) => symbol switch
    {
        VBConstantMemberSymbol { Value: { } value } => value,
        VBLocalConstantSymbol { Value: { } value } => value,
        _ => null,
    };

    private readonly Dictionary<SemanticId, VBTypedValue> _foldedConstants = [];
    private readonly HashSet<SemanticId> _foldingConstants = [];

    /// <summary>
    /// Reduces <paramref name="constantExpression"/> — the constant expression declared by
    /// <paramref name="owner"/> — to its value, once for the whole run.
    /// </summary>
    /// <param name="session">The session the expression resolves its own names against.</param>
    /// <param name="owner">The symbol that declares it: a <c>Const</c>, or a parameter with a default.</param>
    /// <param name="constantExpression">The expression to reduce.</param>
    /// <returns>Its value, or <c>null</c> when it could not be reduced.</returns>
    /// <remarks>
    /// For a caller that holds a declared constant expression but has no evaluation of its own to fold
    /// it into — an omitted <c>Optional</c> argument on a default-member call, which
    /// <c>VBObjectLetCoercionRuntimeSemantics</c> fills in itself. It shares this evaluator's memo, so a
    /// constant expression is reduced once however many callers ask for it.
    /// </remarks>
    public VBTypedValue? Fold(IRuntimeSession session, Symbol owner, ExpressionNode constantExpression)
    {
        var result = FoldConstant(session, new RuntimeEvaluationContext(owner.ParentUri), owner, constantExpression);
        return result.IsSuccess ? result.Result : null;
    }

    /// <summary>
    /// The value an optional parameter takes when the call supplies no argument for it.
    /// </summary>
    /// <remarks>
    /// <strong>MS-VBAL §5.3.1.11</strong>: "If the parameter has a specified default value other than <c>Nothing</c>, this default value is
    /// <c>Let</c>-assigned to the new local variable. If the parameter has a specified default value of <c>Nothing</c>, this default value is
    /// <c>Set</c>-assigned to the new local variable. If the parameter has no specified default value, the new local variables is initialized
    /// to the default value for its declared type" - a <c>Variant</c>'s being Missing (<see cref="RuntimeProcedureInvoker.OmittedArgumentValue"/>).
    /// <para>
    /// The default is a constant expression (<strong>MS-VBAL §5.3.1.5</strong>), reduced by the same fold a <c>Const</c>'s is and therefore
    /// once however many times the procedure is called without it; then <c>Let</c>-coerced to the parameter's declared type, as an argument
    /// would be: <c>Optional w As Variant = 5</c> holds an <c>Integer</c> in a <c>Variant</c>, <c>Optional b As Byte = 300</c> overflows. A
    /// default of <c>Nothing</c> is not coerced, which would ask <c>Nothing</c> for its default member and fail with error 91: an object or a
    /// class parameter takes it as it is, and a <c>Variant</c> holds it. A parameter of any other type cannot be <c>Set</c>-assigned at all,
    /// and gets the coercion's error.
    /// </para>
    /// <para>
    /// A standard-library <c>Compare</c> parameter that takes the calling module's <c>Option Compare</c> when omitted
    /// (<see cref="SymbolProperties.DefaultsToOptionCompare"/>) takes that instead of its declared default.
    /// </para>
    /// <para>
    /// A call's argument binding, the call a Let-coercion makes to an object's default member, and the declared defaults a variable
    /// starts as (<see cref="DeclaredVariableDefaults"/>) all ask here, so that they agree.
    /// </para>
    /// <para>
    /// 🚧 <c>CallByName</c> fills an omitted argument on its own, with its parameter type's default value: a declared default is not
    /// used, and a <c>Variant</c> is Empty rather than Missing. TODO.
    /// </para>
    /// </remarks>
    /// <param name="session">The session the default's names resolve against.</param>
    /// <param name="parameter">The optional parameter the call left out.</param>
    /// <returns>The value, or the error reducing or coercing the default raised.</returns>
    public RuntimeSemanticsEvaluationResult OmittedArgument(IRuntimeSession session, VBParameterSymbol parameter)
    {
        // MS-VBAL 6.1.2.11: a comparing member's omitted Compare "uses the <option-compare-directive> of the calling module". A call
        // binds its arguments before the callee's frame is pushed, and an external call pushes none, so the current frame is still the
        // caller's; and CurrentCompareMode has already resolved Option Compare Database the way the comparison operators do. The value is
        // a VbCompareMethod constant, which needs no coercion. The declared default is what a caller with no calling module gets.
        if (parameter.TryGetProperty(SymbolProperties.DefaultsToOptionCompare, out var optionCompare) && optionCompare)
        {
            return RuntimeSemanticsEvaluationResult.Success(new VBLongValue(
                (int)(session.CurrentCompareMode() is OptionCompare.Text ? VBCompareMethod.VBTextCompare : VBCompareMethod.VBBinaryCompare)));
        }

        if (parameter.DefaultValue is not { } declaredDefault)
        {
            return RuntimeSemanticsEvaluationResult.Success(RuntimeProcedureInvoker.OmittedArgumentValue(parameter));
        }

        if (LetCoercionProvider is null)
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        var foldResult = FoldConstant(session, new RuntimeEvaluationContext(parameter.ParentUri), parameter, declaredDefault);
        if (!foldResult.IsSuccess)
        {
            return foldResult;
        }

        var folded = foldResult.Result!;
        if ((folded is VBVariantValue { TypedValue: var held } ? held : folded) is VBObjectValue nothing && nothing.IsNothing())
        {
            switch (parameter.ResolvedType)
            {
                case VBObjectType or VBClassType:
                    return RuntimeSemanticsEvaluationResult.Success(nothing);
                case VBVariantType:
                    return RuntimeSemanticsEvaluationResult.Success(new VBVariantValue(nothing));
            }
        }

        // the conversion is located at the default itself, the expression it converts, which is in the callee's declaration.
        var defaultFrame = new LetCoercionStackFrame(declaredDefault.Identity, InputIndex.CoercionSourceValue,
            folded, new VBTypeDescValue(parameter.ResolvedType), ConversionSite.Argument);
        var defaultCoercion = LetCoercionProvider.EvaluateLetCoercionSemantics(session.Symbols.Resolver, declaredDefault, defaultFrame);
        if (!defaultCoercion.IsApplicable)
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        return defaultCoercion.IsSuccess
            ? RuntimeSemanticsEvaluationResult.Success(defaultCoercion.Result!)
            : RuntimeSemanticsEvaluationResult.Error(defaultCoercion.ErrorInfo!);
    }

    /// <summary>
    /// Reduces every <c>Const</c> in <paramref name="constants"/> to its value, once, ahead of the run.
    /// </summary>
    /// <remarks>
    /// A constant expression is constant: reducing it again at each of its use sites would give the
    /// same answer for more work every time. This is the fold, and <c>EvaluateSimpleName</c> reads its
    /// result. A constant this is never called for is still folded — once — the first time a use site
    /// asks for it, which is what reaches a constant declared by a module other than the one being run.
    /// </remarks>
    /// <param name="session">The session the constant expressions resolve their own names against.</param>
    /// <param name="constants">The constant symbols to fold. Anything else is ignored.</param>
    public void FoldConstants(IRuntimeSession session, IEnumerable<Symbol> constants)
    {
        foreach (var constant in constants)
        {
            if (ConstantValueOf(constant) is { } value)
            {
                // the declaring scope, not the run's: a constant's expression is written where the
                // constant is, and a name in it binds from there.
                FoldConstant(session, new RuntimeEvaluationContext(constant.ParentUri), constant, value);
            }
        }
    }

    // MS-VBAL 5.2.3.2: "the constant binding of a constant ... is the data value of the <constant-expression> Let-coerced to the declared
    // type of the constant" - which, without an As clause or a type character, is the expression's own, so there is nothing to coerce to.
    // MS-VBAL 5.2.3.4: an Enum member's expression is "coerced to value type Long", and an Enum named in an As clause means Long. A
    // parameter's default is coerced by its call instead.
    private static VBType? DeclaredTypeOfConstant(Symbol constant) => constant switch
    {
        VBEnumConstMemberSymbol => VBLongType.TypeInfo,
        VBConstantMemberSymbol { ResolvedType: not VBUnknownType and var declared } => VBEnumType.AsDeclaredType(declared),
        VBLocalConstantSymbol { ResolvedType: not (null or VBUnknownType) and var declared } => VBEnumType.AsDeclaredType(declared),
        _ => null,
    };

    private RuntimeSemanticsEvaluationResult CoerceConstant(IRuntimeSession session, ExpressionNode value, VBTypedValue folded, VBType declaredType)
    {
        if (LetCoercionProvider is null)
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        var frame = new LetCoercionStackFrame(value.Identity, InputIndex.CoercionSourceValue,
            folded, new VBTypeDescValue(declaredType), ConversionSite.ConstantDeclaration);
        var coercion = LetCoercionProvider.EvaluateLetCoercionSemantics(session.Symbols.Resolver, value, frame);
        if (!coercion.IsApplicable)
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        return coercion.IsSuccess
            ? RuntimeSemanticsEvaluationResult.Success(coercion.Result!)
            : RuntimeSemanticsEvaluationResult.Error(coercion.ErrorInfo!);
    }

    // MS-VBAL 5.2.3.4: a member of a workspace Enum is the nearest expression at or before it, as a Long, plus how far past that member it
    // is - or that distance alone, the first member being 0. The expression is written in the Enum's module, and its names bind from there.
    private RuntimeSemanticsEvaluationResult EnumMemberValueOf(IRuntimeSession session, VBEnumConstMemberSymbol member, EnumMemberValue declared)
    {
        long value = declared.Offset;
        if (declared.BaseValue is { } expression)
        {
            var folded = FoldConstant(session, new RuntimeEvaluationContext(member.ParentUri), member, expression);
            if (!folded.IsSuccess)
            {
                return folded;
            }

            value += ((VBLongValue)folded.Result!).Value;
        }

        return value <= int.MaxValue
            ? Assumed(RuntimeSemanticsEvaluationResult.Success(new VBLongValue((int)value)))
            : RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.Overflow, declared.BaseValue?.Location ?? default,
                Exceptions.VBRuntimeError_ArithmeticOverflow));
    }

    private RuntimeSemanticsEvaluationResult FoldConstant(IRuntimeSession session, RuntimeEvaluationContext context, Symbol constant, ExpressionNode value)
    {
        var id = constant.SemanticId;
        if (_foldedConstants.TryGetValue(id, out var folded))
        {
            return RuntimeSemanticsEvaluationResult.Success(folded);
        }

        // a constant expression may name another constant, so folding one can fold others; a cycle
        // between them is a compile error MS-VBA reports and nothing here reports yet, so without this
        // guard it would recurse until the stack ran out.
        // 🚧 TODO: report circular constant declarations as a compile-time diagnostic instead.
        if (!_foldingConstants.Add(id))
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        try
        {
            var result = Evaluate(session, value, context);
            if (result.IsSuccess && DeclaredTypeOfConstant(constant) is { } declaredType)
            {
                result = CoerceConstant(session, value, result.Result!, declaredType);
            }

            if (result.IsSuccess)
            {
                _foldedConstants[id] = result.Result!;
            }
            return result;
        }
        finally
        {
            _foldingConstants.Remove(id);
        }
    }

    // MS-VBAL §5.6.16.2: a conditional-compilation constant that names nothing is the value 0 - not a
    // compile error, and Option Explicit (a variable-declaration concern) has no bearing on it.
    // A constant the module declares itself shadows the project's of the same name (§3.4.1).
    private static RuntimeSemanticsEvaluationResult EvaluatePrecompilerConstant(IRuntimeSession session, RuntimeEvaluationContext context, PrecompilerNameExpressionNode name)
    {
        if (context.ConditionalConstant?.Invoke(name.Name) is { } moduleConstant)
        {
            return RuntimeSemanticsEvaluationResult.Success(moduleConstant);
        }

        var result = session.Symbols.Resolver.ResolveConditionalConstant(name.Name, ScopeKind.Global, StaticSymbol.GlobalUri);
        return RuntimeSemanticsEvaluationResult.Success(result.Symbol is PrecompilerConstantSymbol constant ? constant.Value : new VBIntegerValue(0));
    }

    private static RuntimeSemanticsEvaluationResult EvaluateInstance(IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode expression)
    {
        // "Me" is a reserved word - the parser never produces a SimpleNameExpressionNode for it - but
        // the symbol it resolves to (the implicit parameter 0 of a class module member) really is named
        // "Me", so the same name-resolution machinery a SimpleName read uses finds it here too.
        var result = session.Symbols.Resolver.ResolveValue(Tokens.Me, ScopeKind.Local, context.Scope);
        return result.Symbol is { } meSymbol
            ? InstanceExpressionRuntimeSemantics.Instance.Evaluate(session, new(), expression, new VBSymbolDescValue(meSymbol))
            : RuntimeSemanticsEvaluationResult.InternalError();
    }

    private static RuntimeSemanticsEvaluationResult EvaluateNew(IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode expression, NewExpressionNode newExpression)
    {
        if (TryGetQualifiedTypeName(newExpression.TypeExpression) is not var (qualifier, typeName) || typeName is null)
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        var result = VBProjectSymbol.ResolveQualifiedType(session.Symbols.Resolver, qualifier, typeName, context.Scope);
        return result.Symbol is VBClassModuleSymbol classModule
            ? CreateObject(session, expression, classModule)
            : RuntimeSemanticsEvaluationResult.InternalError();
    }

    // an object of a class, created for a New expression: held by the statement, and initialized.
    private static RuntimeSemanticsEvaluationResult CreateObject(IRuntimeSession session, ExpressionNode expression, VBClassModuleSymbol classModule)
    {
        var created = NewExpressionRuntimeSemantics.Instance.Evaluate(session, new(), expression, new VBSymbolDescValue(classModule));

        // the new object is the value of the expression, which the statement holds until it is over (ObjectReferences.HoldTemporarily): whatever
        // it is assigned to holds it by then, and one nothing kept - `Call (New Widget).Run` - is destroyed then. Its Initialize runs on a live object.
        if (created is { IsSuccess: true, Result: VBObjectValue made })
        {
            ObjectReferences.HoldTemporarily(session, made);
        }

        // MS-VBAL §5.3.1.10: Initialize runs before a reference to the new object is returned from the operation
        // that creates it, and an error it leaves unhandled is that operation's.
        if (!created.IsSuccess || created.Result is not VBObjectValue { } instance || session.Lifecycle is not { } lifecycle)
        {
            return created;
        }

        var initialized = lifecycle.Initialize(instance.Value);
        return initialized.IsSuccess ? created : initialized;
    }

    private RuntimeSemanticsEvaluationResult EvaluateMemberAccess(IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode expression, MemberAccessExpressionNode memberAccess)
    {
        // MS-VBAL §5.6.12: a member of a project or a procedural module is not a member of a value - there is
        // no object to evaluate. `Constants.vbCrLf`, `Information.Erl`, `VBA.Strings.LenB`: the name is resolved
        // in the namespace and means what it would unqualified.
        if (memberAccess.Owner is { } namespaceExpression
            && TryClassifyNamespace(session, context, namespaceExpression) is { } qualifier)
        {
            return ReadSymbol(
                session, context, ResolveNamespaceMember(session, context, qualifier, memberAccess), nameOfEnclosingFunctionIsItsResult: false);
        }

        // MS-VBAL §5.6.12: likewise a member of an enumeration named by its type is a constant to read, not a member of a value.
        if (memberAccess.Owner is { } enumerationExpression && TryClassifyEnumeration(session, context, enumerationExpression) is { } enumeration)
        {
            return ReadSymbol(
                session, context, enumeration.Members.FirstOrDefault(member => string.Equals(member.Name, memberAccess.Member.IdentifierName, StringComparison.OrdinalIgnoreCase)),
                nameOfEnclosingFunctionIsItsResult: false);
        }

        var ownerResult = EvaluateOwner(session, context, memberAccess);
        if (!ownerResult.IsSuccess)
        {
            return ownerResult;
        }

        var owner = ownerResult.Result!;
        var memberName = memberAccess.Member.IdentifierName;
        var assumedBefore = AssumedSoFar;
        var result = AccessMember(session, context, memberAccess, owner, memberName);
        ObserveMemberAccess(session, context, expression, Tokens.MemberAccess, memberAccess.Owner, owner, memberName,
            MemberAccessOperationSemanticFlags.Explicit, assumedBefore, result);
        return result;
    }

    private RuntimeSemanticsEvaluationResult CallDefaultMember(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode expression, IndexExpressionNode indexExpression, VBObjectValue indexed)
    {
        if (indexed.IsNothing())
        {
            return RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet, expression.Location, Exceptions.VBMemberAccess_ObjectVariableNotSet_Verbose));
        }

        return TryResolveDefaultMember(session, indexed) is { } defaultMember
            ? InvokeProcedure(session, context, defaultMember.Member, indexExpression.Arguments, defaultMember.Receiver)
            : RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, expression.Location, "The object has no default member to index."));
    }

    // a member of an object called with the arguments of the index (IsCall), or read to be indexed by them: a function that declares no parameters returns what is indexed.
    private (RuntimeSemanticsEvaluationResult Result, bool IsCall) CallMember(
        IRuntimeSession session, RuntimeEvaluationContext context, IndexExpressionNode indexExpression, MemberAccessExpressionNode qualified, VBTypedValue owner)
    {
        if (ObjectNotSet(qualified, owner) is { } notSet)
        {
            return (notSet, true);
        }

        if (TryResolveInvocableMember(session, context, qualified.Owner, owner, qualified.Member.IdentifierName) is not { } found)
        {
            return (EvaluateInstanceField(session, qualified, owner, qualified.Member.IdentifierName), false);
        }

        return ReturnsToBeIndexed(found.Member, found.Receiver, indexExpression.Arguments)
            ? (InvokeProcedure(session, context, found.Member, [], found.Receiver), false)
            : (InvokeProcedure(session, context, found.Member, indexExpression.Arguments, found.Receiver), true);
    }

    private RuntimeSemanticsEvaluationResult AccessMember(
        IRuntimeSession session, RuntimeEvaluationContext context, MemberAccessExpressionNode memberAccess, VBTypedValue owner, string memberName)
    {
        if (ObjectNotSet(memberAccess, owner) is { } notSet)
        {
            return notSet;
        }

        // a Property Get, Function or Sub of the object is an invocation with no arguments of its own; a field is a read.
        return TryResolveInvocableMember(session, context, memberAccess.Owner, owner, memberName) is { } found
            ? InvokeProcedure(session, context, found.Member, [], found.Receiver)
            : EvaluateInstanceField(session, memberAccess, owner, memberName);
    }

    // a member of an object variable that holds no object - never set, or set to Nothing - is error 91: there is no
    // object to find the member on. An As New variable is never in that state when it is referred to, which is what
    // made it the object the member is on.
    // A member of what is neither an object nor a record - what a Variant holds when it holds a number, a String, Empty - is error 424: there is no object
    // to find the member on (MS-VBA, recorded in Excel). What is not known is not judged.
    private static RuntimeSemanticsEvaluationResult? ObjectNotSet(MemberAccessExpressionNode memberAccess, VBTypedValue owner)
        => UnwrappedOwner(owner) switch
        {
            VBObjectValue unset when unset.IsNothing() => RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet, memberAccess.Location, Exceptions.VBMemberAccess_ObjectVariableNotSet_Verbose)),
            VBObjectValue or VBUserDefinedTypeValue => null,
            { IsIndeterminate: false } notAnObject when owner is VBVariantValue { IsIndeterminate: false } => RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.ObjectRequired, memberAccess.Location, string.Format(Exceptions.VBMemberAccess_ObjectRequired_Verbose, notAnObject.TypeInfo.Name))),
            _ => null,
        };

    // the value a member is accessed on: the expression written before the dot, or the enclosing With block's target.
    private RuntimeSemanticsEvaluationResult EvaluateOwner(IRuntimeSession session, RuntimeEvaluationContext context, MemberAccessExpressionNode memberAccess)
    {
        if (memberAccess.Owner is { } ownerExpression)
        {
            return Evaluate(session, ownerExpression, context);
        }

        // MS-VBAL §5.6.15: invalid with no enclosing With block - static semantics should already
        // have rejected this.
        return context.EnclosingWithTarget is { } withTarget
            ? RuntimeSemanticsEvaluationResult.Success(withTarget)
            : RuntimeSemanticsEvaluationResult.InternalError();
    }

    /// <summary>
    /// The member of an object that a qualified call invokes, and the object it is invoked on.
    /// </summary>
    /// <remarks>
    /// Any object the session has an instance record for has one, whether the class is the workspace's or the
    /// library's: the error object is an instance of the library's <c>ErrObject</c>, and finds its members here the
    /// same way. A Property Let or Set of the same name is an assignment's business, not a read's, and is not
    /// considered.
    /// </remarks>
    private static (IRuntimeValue Receiver, VBTypeMemberSymbol Member)? TryResolveInvocableMember(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode? ownerExpression, VBTypedValue owner, string memberName)
    {
        if (UnwrappedOwner(owner) is not VBObjectValue { } objectValue || objectValue.IsNothing()
            || !session.Symbols.TryGetInstance(objectValue.Value, out var instance))
        {
            return null;
        }

        // MS-VBAL §5.3.1.9: "When the target object of an invocation has a declared type that is an interface class of
        // the actual target object's class and the method name is the name of an interface member of that interface
        // class then the actual invoked method is the method defined by the corresponding implemented method declaration
        // of target's object's class."
        if (ImplementationThroughDeclaredInterface(session, context, ownerExpression, instance.ClassModule, memberName) is { } implementation)
        {
            return (objectValue.RuntimeValue, implementation);
        }

        return InvocableMemberOf(instance.ClassModule, memberName) is { } member ? (objectValue.RuntimeValue, member) : null;
    }

    // a member of a class's own interface that a read or a call invokes by name. A Property Let or Set derives from the subroutine's symbol and is not one:
    // reading the property is its Get.
    private static VBTypeMemberSymbol? InvocableMemberOf(VBClassModuleSymbol classModule, string memberName)
        => classModule.DefaultInterfaceMembers.FirstOrDefault(candidate =>
            candidate is VBPropertyGetMemberSymbol or VBFunctionMemberSymbol or VBProcedureMemberSymbol and not (VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol)
            && string.Equals(candidate.Name, memberName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The default member of an object - the one a class marks with <c>VB_UserMemId = 0</c> - and the object it is invoked on
    /// (<strong>MS-VBAL §5.6.13</strong>: <c>c(1)</c> is <c>c.Item(1)</c> where <c>Item</c> is the default member of <c>c</c>'s class).
    /// </summary>
    /// <remarks>
    /// The library's classes mark theirs the same way (<c>Collection.Item</c>), so one that is a class of the workspace's and one that is the library's are found alike.
    /// </remarks>
    private static (IRuntimeValue Receiver, VBTypeMemberSymbol Member)? TryResolveDefaultMember(IRuntimeSession session, VBObjectValue objectValue)
    {
        if (objectValue.IsNothing() || !session.Symbols.TryGetInstance(objectValue.Value, out var instance))
        {
            return null;
        }

        var member = instance.ClassModule.DefaultInterfaceMembers.FirstOrDefault(candidate =>
            candidate is VBPropertyGetMemberSymbol or VBFunctionMemberSymbol or VBProcedureMemberSymbol and not (VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol)
            && candidate.TryGetProperty(SymbolProperties.UserMemId, out var userMemId) && userMemId == WellKnownDispIds.Value);

        return member is null ? null : (objectValue.RuntimeValue, member);
    }

    /// <summary>
    /// Whether the class of <paramref name="owner"/> has an enumeration member: a public property or function marked <c>VB_UserMemId = -4</c>
    /// (<strong>MS-VBAL §5.4.2.4</strong>), commonly named <c>_NewEnum</c>.
    /// </summary>
    public static bool HasEnumerationMember(IRuntimeSession session, VBObjectValue owner)
        => !owner.IsNothing() && session.Symbols.TryGetInstance(owner.Value, out var instance) && EnumerationMemberOf(instance.ClassModule) is not null;

    private static VBTypeMemberSymbol? EnumerationMemberOf(VBClassModuleSymbol classModule)
        => classModule.DefaultInterfaceMembers.FirstOrDefault(candidate =>
            candidate is VBPropertyGetMemberSymbol or VBFunctionMemberSymbol
            && candidate.TryGetProperty(SymbolProperties.UserMemId, out var userMemId) && userMemId == WellKnownDispIds.NewEnum);

    /// <summary>
    /// Invokes the enumeration member of <paramref name="owner"/>, the object a <c>For Each</c> over it enumerates with.
    /// </summary>
    /// <param name="session">The session the object lives in.</param>
    /// <param name="context">The scope of the statement.</param>
    /// <param name="owner">The object being enumerated.</param>
    /// <returns>What the member returned, or the error it raised; an internal error when the object has no enumeration member.</returns>
    public RuntimeSemanticsEvaluationResult InvokeEnumerationMember(IRuntimeSession session, RuntimeEvaluationContext context, VBObjectValue owner)
    {
        if (owner.IsNothing() || !session.Symbols.TryGetInstance(owner.Value, out var instance) || EnumerationMemberOf(instance.ClassModule) is not { } member)
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        return InvokeProcedure(session, context, member, [], owner.RuntimeValue);
    }

    /// <summary>
    /// Invokes a member of <paramref name="owner"/> that takes no arguments, by name: what a <c>For Each</c> asks an enumerator for.
    /// </summary>
    /// <param name="session">The session the object lives in.</param>
    /// <param name="context">The scope of the statement.</param>
    /// <param name="owner">The object the member is called on.</param>
    /// <param name="memberName">The member's name.</param>
    /// <returns>What the member returned, or the error it raised; <c>438</c> when the object has no such member.</returns>
    public RuntimeSemanticsEvaluationResult InvokeMember(IRuntimeSession session, RuntimeEvaluationContext context, VBObjectValue owner, string memberName)
        => TryResolveInvocableMember(session, context, null, owner, memberName) is { } found
            ? InvokeProcedure(session, context, found.Member, [], found.Receiver)
            : RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, default, $"The object has no member '{memberName}'."));

    // What the expression an object is reached through is declared as is what decides which of its interfaces a member
    // is a member of: the value carries the object, and nothing of how it was declared. So the declaration is asked of
    // the same rules that type the expression at compile time. It is asked only of an object whose class implements an
    // interface it declares, since for any other the answer would be its own class.
    private static VBTypeMemberSymbol? ImplementationThroughDeclaredInterface(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode? ownerExpression, VBClassModuleSymbol actual, string memberName)
    {
        return DeclaredInterfaceOf(session, context, ownerExpression, actual) is { } implementedInterface
            ? ImplementationIn(actual, implementedInterface, memberName)
            : null;
    }

    // the member of a class that implements a member of an interface it implements, by the interface member's name.
    private static VBTypeMemberSymbol? ImplementationIn(VBClassModuleSymbol actual, VBClassModuleSymbol implementedInterface, string memberName)
    {
        // a public variable or a method: not a Property Let or Set, which are an assignment's business, and not an event.
        var interfaceMember = implementedInterface.Members.FirstOrDefault(candidate
            => string.Equals(candidate.Name, memberName, StringComparison.OrdinalIgnoreCase)
            && (candidate is VBPropertyGetMemberSymbol or VBFunctionMemberSymbol or VBProcedureMemberSymbol and not (VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol)
                || candidate.Kind == SymbolKindExt.Field));

        return interfaceMember is null ? null : actual.FindImplementation(implementedInterface, interfaceMember);
    }

    // the interface class an expression is declared as, when the object it holds is an instance of a class that implements
    // it and the expression is not declared as that class: what the object's members are looked up in.
    private static VBClassModuleSymbol? DeclaredInterfaceOf(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode? ownerExpression, VBClassModuleSymbol actual)
    {
        // a with-relative member (`.Area`) is a member of the target of the With block, which is declared as whatever its expression is.
        ownerExpression ??= context.EnclosingWithTargetExpression;

        var lifecycle = ClassLifecycleInterface.Interface.Uri.AbsoluteUri;
        if (ownerExpression is null || !actual.ImplementedInterfaces.Any(implemented => implemented.Uri.AbsoluteUri != lifecycle))
        {
            return null;
        }

        var scope = new LexicalScope(context.Scope, LexicalScopeKind.Procedure, null, []);
        var declared = ExpressionStaticSemanticsEvaluator.Evaluate(new StaticEvaluationContext(session.Symbols.Resolver, scope), ownerExpression);
        return declared.Result is VBClassType { Symbol: var declaredClass }
            ? actual.ImplementedInterfaces.FirstOrDefault(implemented => implemented.Uri.AbsoluteUri == declaredClass.Uri.AbsoluteUri)
            : null;
    }

    /// <summary>
    /// What a member of an object that is assigned to is: a field, which is the storage of the object it is a field of, or a
    /// <c>Property Let</c> or <c>Property Set</c>, which the assignment invokes.
    /// </summary>
    /// <param name="Field">The field, when it is one.</param>
    /// <param name="Instance">The object the field is a field of.</param>
    /// <param name="Accessor">The accessor, when it is one.</param>
    /// <param name="Receiver">The object the accessor is invoked on.</param>
    public readonly record struct AssignableMember(
        VBTypeMemberSymbol? Field, IObjectInstance? Instance, VBTypeMemberSymbol? Accessor, IRuntimeValue? Receiver);

    /// <summary>
    /// Finds the member of <paramref name="owner"/> that an assignment to <paramref name="memberAccess"/> writes to
    /// (<strong>MS-VBAL §5.4.3.8</strong>, <strong>§5.4.3.9</strong>).
    /// </summary>
    /// <remarks>
    /// A public variable is the storage it names, and a property is assigned by its <c>Property Let</c>, or by its
    /// <c>Property Set</c> in a <c>Set</c> assignment. Through a declared interface (<strong>§5.3.1.9</strong>) the
    /// accessor is the one the object's class implements it with, and a public variable of the interface, which the
    /// class implements with properties, is assigned through the property that implements it.
    /// <para>
    /// Which member that is depends on the class of the object, and on how the owner is declared, which is the expression's: it is found once for each
    /// member access and class it meets (IR lowering S0.11), as the member a bound member expression reads is.
    /// </para>
    /// </remarks>
    /// <param name="session">The session the objects live in.</param>
    /// <param name="context">The scope of the assignment.</param>
    /// <param name="memberAccess">The member access assigned to, whose owner expression says how the object is declared.</param>
    /// <param name="owner">The object.</param>
    /// <param name="isSet">Whether it is a <c>Set</c> assignment.</param>
    /// <returns>The member, or <see langword="null"/> when the object has none that can be assigned.</returns>
    public AssignableMember? ResolveAssignableMember(
        IRuntimeSession session, RuntimeEvaluationContext context, MemberAccessExpressionNode memberAccess, VBObjectValue owner, bool isSet)
    {
        if (owner.IsNothing() || !session.Symbols.TryGetInstance(owner.Value, out var instance))
        {
            return null;
        }

        var actual = instance.ClassModule;
        var scopes = session.Symbols.Scopes;
        VBTypeMemberSymbol? member;
        bool isAccessor;
        if (_assignmentSites.TryGetValue(memberAccess, out var site) && ReferenceEquals(site.Class, actual) && site.IsSet == isSet && ReferenceEquals(site.Scopes, scopes))
        {
            (member, isAccessor) = (site.Member, site.IsAccessor);
        }
        else
        {
            (member, isAccessor) = AssignableMemberOf(session, context, memberAccess, actual, isSet);
            _assignmentSites[memberAccess] = new AssignmentSite(scopes, actual, isSet, member, isAccessor);
        }

        return member is null ? null
            : isAccessor ? new AssignableMember(null, null, member, owner.RuntimeValue)
            : new AssignableMember(member, instance, null, null);
    }

    // the member each member access was last assigned through, for the class of the object and the kind of assignment it was: the declared type of the
    // owner, which picks the interface, is true of the scopes it was found in.
    private sealed record class AssignmentSite(ScopeTree Scopes, VBClassModuleSymbol Class, bool IsSet, VBTypeMemberSymbol? Member, bool IsAccessor);

    private readonly ConcurrentDictionary<MemberAccessExpressionNode, AssignmentSite> _assignmentSites = new(ReferenceEqualityComparer.Instance);

    // the accessor or the public variable of `actual` an assignment to the member writes through.
    private static (VBTypeMemberSymbol? Member, bool IsAccessor) AssignableMemberOf(
        IRuntimeSession session, RuntimeEvaluationContext context, MemberAccessExpressionNode memberAccess, VBClassModuleSymbol actual, bool isSet)
    {
        var memberName = memberAccess.Member.IdentifierName;
        var access = isSet ? ImplementationAccess.Set : ImplementationAccess.Let;
        if (DeclaredInterfaceOf(session, context, memberAccess.Owner, actual) is { } implementedInterface)
        {
            var interfaceMembers = implementedInterface.Members
                .Where(candidate => string.Equals(candidate.Name, memberName, StringComparison.OrdinalIgnoreCase)).ToArray();
            var interfaceMember = interfaceMembers.FirstOrDefault(candidate => candidate.Kind == SymbolKindExt.Field)
                ?? interfaceMembers.FirstOrDefault(candidate => isSet ? candidate is VBPropertySetMemberSymbol : candidate is VBPropertyLetMemberSymbol);

            if (interfaceMember is not null && actual.FindImplementation(implementedInterface, interfaceMember, access) is { } implementation)
            {
                return (implementation, true);
            }
        }

        var candidates = actual.DefaultInterfaceMembers
            .Where(candidate => string.Equals(candidate.Name, memberName, StringComparison.OrdinalIgnoreCase)).ToArray();

        if (candidates.FirstOrDefault(candidate => isSet ? candidate is VBPropertySetMemberSymbol : candidate is VBPropertyLetMemberSymbol) is { } accessor)
        {
            return (accessor, true);
        }

        return (candidates.FirstOrDefault(candidate => candidate.Kind == SymbolKindExt.Field), false);
    }

    /// <summary>
    /// Invokes the <c>Property Let</c> or <c>Property Set</c> an assignment to a property is, with the index arguments written
    /// after the property's name and the value assigned as the last argument (<strong>MS-VBAL §5.3.1.7</strong>).
    /// </summary>
    /// <param name="session">The session the call runs in.</param>
    /// <param name="context">The scope the arguments are written in.</param>
    /// <param name="accessor">The accessor.</param>
    /// <param name="receiver">The object it is invoked on; <see langword="null"/> for a property of a standard module, which is invoked on none.</param>
    /// <param name="indexArguments">The arguments written between the property's name and the assignment.</param>
    /// <param name="value">The value assigned, which is Let- or Set-coerced to the type of the accessor's value parameter.</param>
    /// <param name="source">The expression the value came from, for the location of an error.</param>
    /// <param name="isSet">Whether it is a <c>Set</c> assignment.</param>
    public RuntimeSemanticsEvaluationResult InvokeAssignment(
        IRuntimeSession session, RuntimeEvaluationContext context, VBTypeMemberSymbol accessor, IRuntimeValue? receiver,
        ImmutableArray<ExpressionNode> indexArguments, VBTypedValue value, ExpressionNode source, bool isSet)
    {
        var borrowed = new List<MemoryAddress>();
        try
        {
            return InvokeAssignment(session, context, accessor, receiver, indexArguments, value, source, isSet, borrowed);
        }
        finally
        {
            GiveBack(session, borrowed);
        }
    }

    private RuntimeSemanticsEvaluationResult InvokeAssignment(
        IRuntimeSession session, RuntimeEvaluationContext context, VBTypeMemberSymbol accessor, IRuntimeValue? receiver,
        ImmutableArray<ExpressionNode> indexArguments, VBTypedValue value, ExpressionNode source, bool isSet, List<MemoryAddress> borrowed)
    {
        var parameters = RuntimeProcedureInvoker.GetParameters(accessor);
        parameters = receiver is not null && parameters is [{ Name: "Me" }, ..] ? parameters.RemoveAt(0) : parameters;
        if (ProcedureInvoker is null || LetCoercionProvider is null || parameters.IsEmpty)
        {
            // static semantics rejects a Property Let or Set with no value parameter (VBC09321).
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        // the value parameter is the last one, and the ones before it are what the property is indexed by.
        var valueParameter = parameters[^1];
        if (BindArguments(session, context, parameters.RemoveAt(parameters.Length - 1), indexArguments, out var arguments, out _, borrowed) is { } bindingError)
        {
            return bindingError;
        }

        VBTypedValue coerced;
        if (isSet)
        {
            if (SetCoercion is null)
            {
                return RuntimeSemanticsEvaluationResult.InternalError();
            }

            var setResult = SetCoercion.EvaluateSetCoercion(session, source, value, valueParameter.ResolvedType);
            if (!setResult.IsSuccess)
            {
                return RuntimeSemanticsEvaluationResult.Error(setResult.ErrorInfo!);
            }

            coerced = setResult.Result!;
        }
        else
        {
            var letResult = LetCoercionProvider.EvaluateLetCoercionSemantics(
                session.Symbols.Resolver, source,
                new LetCoercionStackFrame(source.Identity, InputIndex.CoercionSourceValue, value, new VBTypeDescValue(valueParameter.ResolvedType), ConversionSite.Argument));
            if (!letResult.IsApplicable)
            {
                return RuntimeSemanticsEvaluationResult.InternalError();
            }

            if (!letResult.IsSuccess)
            {
                return RuntimeSemanticsEvaluationResult.Error(letResult.ErrorInfo!);
            }

            coerced = letResult.Result!;
        }

        IRuntimeValue[] callArguments = receiver is null ? [.. arguments, coerced.RuntimeValue] : [receiver, .. arguments, coerced.RuntimeValue];
        return Bindings is { } bindings
            ? bindings.ForMember(accessor).Call(session.Symbols.Resolver, callArguments)
            : ProcedureInvoker.Invoke(accessor, session.Symbols.Resolver, callArguments);
    }

    // A late-bound Variant/Object member, and a call through a Property/Function/Sub member, are both
    // invocations, not reads - only a Field/Variable-kind member is readable this way.
    private RuntimeSemanticsEvaluationResult EvaluateInstanceField(IRuntimeSession session, MemberAccessExpressionNode memberAccess, VBTypedValue owner, string memberName)
    {
        // a UDT field is not reached through an instance the session knows about: a UDT has location identity
        // but no instance record, and its fields live on the value itself (MS-VBAL §2.1 - a UDT data value is
        // "a linear concatenation of the aggregated data values"). The value in hand is already everything
        // needed to read one, whether it arrived declared or wrapped in a Variant.
        if (UnwrappedOwner(owner) is VBUserDefinedTypeValue udt)
        {
            return udt[memberName] is { } field
                ? RuntimeSemanticsEvaluationResult.Success(field)
                : RuntimeSemanticsEvaluationResult.InternalError();
        }

        // an object held by a Variant - an element of an array of Variant, a Variant parameter - is the object.
        if (UnwrappedOwner(owner) is not VBObjectValue objectValue || objectValue.IsNothing()
            || !session.Symbols.TryGetInstance(objectValue.Value, out var instance))
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        var member = instance.ClassModule.DefaultInterfaceMembers
            .FirstOrDefault(candidate => string.Equals(candidate.Name, memberName, StringComparison.OrdinalIgnoreCase));

        // an object whose class has no member of the name - late-bound, or reached through a Variant - does not support it: error 438 (MS-VBA).
        return member is { Kind: SymbolKindExt.Field or SymbolKindExt.Variable }
            ? Assumed(RuntimeSemanticsEvaluationResult.Success(member.ResolvedType.CreateValue(instance.GetValue(member).ForReading())))
            : member is null
                ? RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, memberAccess.Member.Location,
                    string.Format(Exceptions.VBMemberAccess_NoSuchMember_Verbose, memberName)))
                : RuntimeSemanticsEvaluationResult.InternalError();
    }

    // a Variant's own TypeInfo mirrors what it wraps while the instance stays a VBVariantValue, so a member
    // access on a Variant holding a UDT has to see past the wrapper - the same unwrapping EvaluateIndex does
    // for a Variant holding an array.
    private static VBTypedValue UnwrappedOwner(VBTypedValue owner)
    {
        while (owner is VBVariantValue { TypedValue: var wrapped } && wrapped is not null)
        {
            owner = wrapped;
        }

        return owner;
    }

    private RuntimeSemanticsEvaluationResult EvaluateIndex(IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode expression, IndexExpressionNode indexExpression)
    {
        // A bare name Callee that resolves to a Sub/Function/Property Get is a call with
        // indexExpression's own arguments (Foo(1, 2), or Call Foo(1, 2)'s own Callee) - checked BEFORE
        // recursing into Evaluate below, which would otherwise reach EvaluateSimpleName's own bare-call
        // path and wrongly invoke it with zero arguments instead of leaving the call to this method,
        // arguments and all. This is also the ONLY way a Function/Property Get recurses into itself:
        // Foo(n - 1) always resolves its own Callee here, never through EvaluateSimpleName's own
        // self-reference check for a bare Foo with no parentheses at all.
        RuntimeSemanticsEvaluationResult calleeResult;
        if (TryResolveCallableSub(session, context, indexExpression.Callee) is { } sub)
        {
            // a function that declares no parameters is not given what follows it in parentheses: it is called, and what it
            // returns is what is indexed - `Items(1)` of a function that returns a Collection.
            if (!ReturnsToBeIndexed(sub, null, indexExpression.Arguments))
            {
                return InvokeProcedure(session, context, sub, indexExpression.Arguments);
            }

            calleeResult = InvokeProcedure(session, context, sub, []);
        }

        // a call written on an object - Err.Raise 5, obj.Item(1) - invokes the member the object's class has by that
        // name. The owner is evaluated once, here, because evaluating it again to read the member as an array would
        // repeat whatever it does.
        else if (indexExpression.Callee is MemberAccessExpressionNode { Owner: { } namespaceExpression } namespaced
            && TryClassifyNamespace(session, context, namespaceExpression) is { } qualifier)
        {
            // MS-VBAL §5.6.12: a call of a member of a project or a procedural module - Strings.LenB("42"),
            // VBA.LenB("42") - is a call of the procedure it names, with no object to evaluate and no receiver.
            // Anything else it names is a value, which the arguments then index.
            var member = ResolveNamespaceMember(session, context, qualifier, namespaced);
            if (member is VBProcedureMemberSymbol or VBFunctionMemberSymbol or VBPropertyGetMemberSymbol)
            {
                return InvokeProcedure(session, context, (VBTypeMemberSymbol)member, indexExpression.Arguments);
            }

            calleeResult = ReadSymbol(session, context, member, nameOfEnclosingFunctionIsItsResult: false);
        }
        else if (indexExpression.Callee is MemberAccessExpressionNode qualified)
        {
            var ownerResult = EvaluateOwner(session, context, qualified);
            if (!ownerResult.IsSuccess)
            {
                return ownerResult;
            }

            var assumedBefore = AssumedSoFar;
            var (accessed, isCall) = CallMember(session, context, indexExpression, qualified, ownerResult.Result!);
            ObserveMemberAccess(session, context, qualified, Tokens.MemberAccess, qualified.Owner, ownerResult.Result!, qualified.Member.IdentifierName,
                MemberAccessOperationSemanticFlags.Explicit, assumedBefore, accessed);
            if (isCall)
            {
                return accessed;
            }

            calleeResult = accessed;
        }
        else
        {
            calleeResult = Evaluate(session, indexExpression.Callee, context);
        }

        return IndexValue(session, context, expression, indexExpression, calleeResult);
    }

    // what the arguments of an index expression do to the value its callee evaluated to, when it is not a call: an element of an array, the default member of
    // an object called with them.
    private RuntimeSemanticsEvaluationResult IndexValue(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode expression, IndexExpressionNode indexExpression, RuntimeSemanticsEvaluationResult calleeResult)
    {
        if (!calleeResult.IsSuccess)
        {
            return calleeResult;
        }

        // a Variant holding an array reports its own TypeInfo as the array's, but stays a VBVariantValue
        // instance - unwrap it here (recursively) so "v(0)" on a Variant-typed array works the same as
        // on a declared one.
        var calleeValue = calleeResult.Result;
        while (calleeValue is VBVariantValue { TypedValue: var wrapped })
        {
            calleeValue = wrapped;
        }

        // what is indexed is not known: it is not known to be an array, nor an object with a default member, nor to be Nothing, and so what is read is not
        // known, and the error it would raise is the assumed value's. The arguments are the code's, and are evaluated as they would be.
        if (calleeResult.Result!.IsIndeterminate && calleeValue is not VBArrayValue)
        {
            var assumedBefore = AssumedSoFar;
            foreach (var argument in indexExpression.Arguments)
            {
                if (EvaluateIndexArgument(session, argument, context) is { IsSuccess: false } failed)
                {
                    return failed;
                }
            }

            var unknown = RuntimeSemanticsEvaluationResult.Success(VBVariantType.TypeInfo.CreateIndeterminateValue());

            // what is not known is still declared: an object that is indexed is a call of its default member, whichever object it is.
            if (Observation is not null && DeclaredTypeOfOwner(session, context, indexExpression.Callee) is VBClassType or VBObjectType)
            {
                ObserveMemberAccess(session, context, expression, Tokens.MemberAccess, indexExpression.Callee, calleeResult.Result, null,
                    MemberAccessOperationSemanticFlags.Implicit | MemberAccessOperationSemanticFlags.DefaultMember, assumedBefore, unknown);
            }

            return unknown;
        }

        // an object that is indexed is a call of its default member: `c(1)` is `c.Item(1)`.
        if (calleeValue is VBObjectValue indexed)
        {
            var assumedBefore = AssumedSoFar;
            var called = CallDefaultMember(session, context, expression, indexExpression, indexed);
            ObserveMemberAccess(session, context, expression, Tokens.MemberAccess, indexExpression.Callee, calleeResult.Result, null,
                MemberAccessOperationSemanticFlags.Implicit | MemberAccessOperationSemanticFlags.DefaultMember, assumedBefore, called);
            return called;
        }

        if (calleeValue is not VBArrayValue array)
        {
            // any other Callee shape is a function/property call, not an element read.
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        if (EvaluateSubscripts(session, context, indexExpression.Arguments, out var subscripts, out var subscriptsKnown) is { } subscriptFailure)
        {
            return subscriptFailure;
        }

        // a subscript that is not known selects an element that could be any of them, so what is read is not known; the error 9 a subscript
        // that is out of the bounds would raise is the same, and so is the one an array that is not known would: it is not known to happen.
        if (!subscriptsKnown)
        {
            return RuntimeSemanticsEvaluationResult.Success(array.ItemType.CreateIndeterminateValue());
        }

        var element = array[subscripts];
        return element is not null
            ? RuntimeSemanticsEvaluationResult.Success(element)
            : array.IsIndeterminate
                ? RuntimeSemanticsEvaluationResult.Success(array.ItemType.CreateIndeterminateValue())
                : RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.SubscriptOutOfRange, expression.Location,
                    string.Join(", ", subscripts)));
    }

    // a Function or Property Get that declares no parameters, called with arguments: they are not its own, and index what it returns.
    private static bool ReturnsToBeIndexed(VBTypeMemberSymbol member, IRuntimeValue? receiver, ImmutableArray<ExpressionNode> arguments)
    {
        // `F()` is written with an empty argument list, which is not an argument: it is the call itself.
        if (member is not (VBFunctionMemberSymbol or VBPropertyGetMemberSymbol) || arguments.IsDefaultOrEmpty || arguments is [MissingArgumentNode])
        {
            return false;
        }

        var parameters = RuntimeProcedureInvoker.GetParameters(member);
        return (receiver is not null && parameters is [{ Name: "Me" }, ..] ? parameters.RemoveAt(0) : parameters).IsEmpty;
    }

    /// <summary>
    /// Evaluates the subscripts of an element of an array, <c>a(i, j)</c>, to the integers that select it.
    /// </summary>
    /// <param name="session">The session the expressions are evaluated in.</param>
    /// <param name="context">The scope of the expressions.</param>
    /// <param name="arguments">The subscript expressions, one per dimension.</param>
    /// <param name="subscripts">What each evaluated to; empty when evaluating failed.</param>
    /// <returns>The error or internal error that stopped it, or <see langword="null"/> when every subscript has a value.</returns>
    public RuntimeSemanticsEvaluationResult? EvaluateSubscripts(
        IRuntimeSession session, RuntimeEvaluationContext context, ImmutableArray<ExpressionNode> arguments, out int[] subscripts)
        => EvaluateSubscripts(session, context, arguments, out subscripts, out _);

    /// <inheritdoc cref="EvaluateSubscripts(IRuntimeSession, RuntimeEvaluationContext, ImmutableArray{ExpressionNode}, out int[])"/>
    /// <param name="isKnown">
    /// Whether every subscript is known. When one is not, <paramref name="subscripts"/> holds the value its type assumes in its place, which
    /// selects an element that is no more likely than any other, or none at all.
    /// </param>
    public RuntimeSemanticsEvaluationResult? EvaluateSubscripts(
        IRuntimeSession session, RuntimeEvaluationContext context, ImmutableArray<ExpressionNode> arguments, out int[] subscripts, out bool isKnown)
    {
        subscripts = [];
        isKnown = true;
        var evaluated = new int[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            var argumentResult = EvaluateIndexArgument(session, arguments[i], context);
            if (argumentResult is { } failed && !failed.IsSuccess)
            {
                return failed;
            }

            if (argumentResult is null || !TryGetIntegralSubscript(argumentResult.Value.Result, out var subscript))
            {
                return RuntimeSemanticsEvaluationResult.InternalError();
            }

            isKnown &= !argumentResult.Value.Result!.IsIndeterminate;
            evaluated[i] = subscript;
        }

        subscripts = evaluated;
        return null;
    }

    private static VBTypeMemberSymbol? TryResolveCallableSub(IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode callee)
    {
        if (callee is not SimpleNameExpressionNode simpleName)
        {
            return null;
        }

        var result = session.Symbols.Resolver.ResolveValue(simpleName, ScopeKind.Local, context.Scope);
        return result.Symbol is VBProcedureMemberSymbol or VBFunctionMemberSymbol or VBPropertyGetMemberSymbol
            ? (VBTypeMemberSymbol)result.Symbol
            : null;
    }

    /// <summary>
    /// Invokes the procedure <paramref name="callee"/> names, with <paramref name="argumentNodes"/> as
    /// its arguments (<strong>MS-VBAL §5.4.2.1</strong>).
    /// </summary>
    /// <param name="session">The session the call runs in.</param>
    /// <param name="context">The scope the callee and its arguments resolve from.</param>
    /// <param name="callee">The call target.</param>
    /// <param name="argumentNodes">The call's arguments.</param>
    /// <returns>
    /// What the callee returned, or an internal error when <paramref name="callee"/> does not name a
    /// procedure this evaluator can call.
    /// </returns>
    /// <remarks>
    /// For the bare call statement, whose argument list belongs to the statement rather than to any
    /// expression under it — <c>Foo 1, 2</c>, which MS-VBAL grants no parenthesized <c>lExpression</c>
    /// equivalent. Every parenthesized shape carries its arguments inside the callee's own
    /// <c>IndexExpressionNode</c> and reaches the same invocation through <c>Evaluate</c> instead.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult Invoke(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode callee, ImmutableArray<ExpressionNode> argumentNodes)
    {
        if (TryResolveCallableSub(session, context, callee) is { } procedure)
        {
            return InvokeProcedure(session, context, procedure, argumentNodes);
        }

        // a call on an object - Err.Raise 5 - is the same call with the same arguments, written without parentheses: the same expression every time the
        // statement runs, so that what is known of it once (its binding) is known the next time.
        return callee is MemberAccessExpressionNode
            ? Evaluate(session, _statementCalls.GetOrAdd(callee, static (node, arguments) => new IndexExpressionNode(node.Identity, node.Location, node, arguments), argumentNodes), context)
            : RuntimeSemanticsEvaluationResult.InternalError();
    }

    // the call expression of each call statement written on an object, by its callee (one statement, one callee node): the evaluator lives one execution.
    private readonly ConcurrentDictionary<ExpressionNode, IndexExpressionNode> _statementCalls = new(ReferenceEqualityComparer.Instance);

    private RuntimeSemanticsEvaluationResult InvokeProcedure(IRuntimeSession session, RuntimeEvaluationContext context, VBTypeMemberSymbol procedure, ImmutableArray<ExpressionNode> argumentNodes, IRuntimeValue? receiver = null)
    {
        var allParameters = RuntimeProcedureInvoker.GetParameters(procedure);
        // a call made on an object supplies its own implicit Me (parameter 0), so the arguments written at the call
        // site map onto the parameters after it.
        var parameters = receiver is not null && allParameters is [{ Name: "Me" }, ..] ? allParameters.RemoveAt(0) : allParameters;
        if (ProcedureInvoker is null)
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        var borrowed = new List<MemoryAddress>();
        try
        {
            if (BindArguments(session, context, parameters, argumentNodes, out var arguments, out var argumentsKnown, borrowed,
                    external: procedure is VBExternalFunctionMemberSymbol or VBExternalSubMemberSymbol)
                is { } bindingError)
            {
                return bindingError;
            }

            IRuntimeValue[] callArguments = receiver is null ? arguments : [receiver, .. arguments];

            // through a binding rather than straight to the invoker: whether this member's code is the workspace's
            // is the factory's decision, and a call site has no business knowing.
            var returned = Bindings is { } bindings
                ? bindings.ForMember(procedure).Call(session.Symbols.Resolver, callArguments)
                : ProcedureInvoker.Invoke(procedure, session.Symbols.Resolver, callArguments);
            return argumentsKnown ? returned : NotKnown(returned);
        }
        finally
        {
            GiveBack(session, borrowed);
        }
    }

    /// <summary>
    /// What a call made with an argument that is not known returns: the value it returned, which is not known either
    /// (<see cref="VBTypedValue.IsIndeterminate"/>).
    /// </summary>
    /// <remarks>
    /// An analysis runs the standard library for real, and an argument that is not known reaches it as the value its type assumes in its place, which
    /// carries no knowledge: the library computes as it would for that value, and what it returns is derived from an assumed value - a stand-in, not a
    /// fact about the code. A call that returns nothing has nothing to be known, and an error the call raised is returned as it was raised.
    /// <para>
    /// 👉 A call that is made with no argument, or with arguments that are all known, returns what the callee returned, known or not as the callee says.
    /// </para>
    /// </remarks>
    private static RuntimeSemanticsEvaluationResult NotKnown(RuntimeSemanticsEvaluationResult returned)
        => returned is { IsSuccess: true, Result: { } value } && value is not VBVoidValue
            ? RuntimeSemanticsEvaluationResult.Success(value.AsIndeterminate())
            : returned;

    /// <summary>
    /// Raises <paramref name="eventName"/>, an event of the class of the object whose code this is, on that object
    /// (<strong>MS-VBAL §5.4.2.20</strong>): the procedures that handle it are invoked, in the order their
    /// <c>WithEvents</c> variables were assigned, with the arguments written after the event's name.
    /// </summary>
    /// <remarks>
    /// The arguments are evaluated once, whatever the number of handlers. A <c>ByRef</c> event parameter whose argument
    /// is a variable is aliased to it, which is what makes the value one handler leaves in it the one the next
    /// handler starts with, and the one the raiser finds afterwards. An error a handler leaves unhandled stops the
    /// invocations and is the error of the <c>RaiseEvent</c>.
    /// <para>
    /// A <c>ByRef</c> parameter whose argument is not a variable is given a location of its own for the handlers of the one event, so the value one
    /// leaves in it is the next one's argument too.
    /// </para>
    /// </remarks>
    /// <param name="session">The session the event is raised in.</param>
    /// <param name="context">The scope of the <c>RaiseEvent</c> statement, from which <c>Me</c> is the source.</param>
    /// <param name="eventName">The name of the event.</param>
    /// <param name="argumentNodes">The event arguments, as written.</param>
    public RuntimeSemanticsEvaluationResult RaiseEvent(
        IRuntimeSession session, RuntimeEvaluationContext context, string eventName, ImmutableArray<ExpressionNode> argumentNodes)
    {
        if (ProcedureInvoker is null
            || EventAttachments.MeOf(session, context) is not { } source
            || !session.Symbols.TryGetInstance(source, out var live)
            || live.ClassModule.FindEvent(eventName) is not { } raised)
        {
            // static semantics rejects a RaiseEvent outside a class module and one of an event it does not declare.
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        // the elements of arrays passed by reference are given back with the temporaries, once every handler has run.
        var temporaries = new List<MemoryAddress>();
        if (BindArguments(session, context, raised.Parameters, argumentNodes, out var arguments, out _, temporaries) is { } bindingError)
        {
            GiveBack(session, temporaries);
            return bindingError;
        }

        // MS-VBAL §5.4.2.20: the next invocation's argument for a ByRef parameter is what the parameter last contained.
        // An argument that names a variable is that variable; any other has nowhere to be left a value in, so it is
        // given a location of its own for the handlers of this one event.
        for (var i = 0; i < raised.Parameters.Length; i++)
        {
            var parameter = raised.Parameters[i];
            if (RuntimeProcedureInvoker.IsByRef(parameter.ParameterKind) && parameter is not ParamArrayParameterSymbol
                && arguments[i] is not VBRuntimeReference
                && session.Storage.TryAllocate(parameter.ResolvedType.DefaultValue.Size, new ValueBindingHandle(arguments[i]), out var temporary))
            {
                temporaries.Add(temporary);
                arguments[i] = new VBRuntimeReference(temporary);
            }
        }

        try
        {
            foreach (var subscription in session.Objects.EventSubscribers(source))
            {
                if (!session.Symbols.TryGetInstance(subscription.Subscriber, out var subscriber)
                    || subscription.Variable is not VBTypeMemberSymbol variable
                    || subscriber.ClassModule.FindEventHandler(variable, raised) is not { } handler)
                {
                    continue;
                }

                IRuntimeValue[] callArguments = [new VBObjectValue(subscription.Subscriber).RuntimeValue, .. arguments];
                var handled = Bindings is { } bindings
                    ? bindings.ForMember(handler).Call(session.Symbols.Resolver, callArguments)
                    : ProcedureInvoker.Invoke(handler, session.Symbols.Resolver, callArguments);
                if (!handled.IsSuccess)
                {
                    return handled;
                }
            }

            return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
        }
        finally
        {
            foreach (var temporary in temporaries)
            {
                session.Storage.TryDeallocate(temporary);
            }
        }
    }

    /// <summary>
    /// Binds the arguments written at a call site to <paramref name="parameters"/> (<strong>MS-VBAL §5.3.1.11</strong>):
    /// each is evaluated, a <c>ByRef</c> one that names a variable is aliased to it, and any other is Let-coerced to the
    /// parameter's declared type.
    /// </summary>
    /// <param name="session">The session the arguments are evaluated against.</param>
    /// <param name="context">The scope the arguments are written in.</param>
    /// <param name="parameters">The parameters of whatever is called, without its <c>Me</c>.</param>
    /// <param name="argumentNodes">The arguments, as written.</param>
    /// <param name="arguments">What the call is made with, one per parameter. Empty when binding failed.</param>
    /// <param name="isKnown">
    /// Whether every argument the binding evaluated is known. A variable passed by reference, and the elements of a <c>ParamArray</c>, are not
    /// judged here: they are passed as storage that keeps whether what it holds is known (<see cref="IKnowledgeBinding"/>).
    /// </param>
    /// <param name="external">
    /// Whether what is called is a <c>Declare</c>d procedure, whose arguments are passed the way a native library takes them: a <c>ByVal String</c> is the
    /// buffer of the variable it names, which the library may write to and which takes what it wrote, and an <c>As Any</c> parameter takes a variable of any
    /// type by reference.
    /// </param>
    /// <param name="borrowed">
    /// Where the addresses an element of an array passed by reference is given are added: the caller gives them back (<see cref="GiveBack"/>) once the call
    /// made with the arguments is over, which is as long as the elements are passed as variables.
    /// </param>
    /// <returns>The error that stopped the binding, or <see langword="null"/> when every argument was bound.</returns>
    private RuntimeSemanticsEvaluationResult? BindArguments(
        IRuntimeSession session, RuntimeEvaluationContext context, ImmutableArray<VBParameterSymbol> parameters,
        ImmutableArray<ExpressionNode> argumentNodes, out IRuntimeValue[] arguments, out bool isKnown, List<MemoryAddress> borrowed, bool external = false)
    {
        arguments = [];
        isKnown = true;
        if (LetCoercionProvider is null)
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        var mapResult = MapArguments(parameters, argumentNodes);
        if (mapResult.Error is { } mappingError)
        {
            return mappingError;
        }

        var mapped = mapResult.Mapped!;
        var bound = new IRuntimeValue[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];

            if (parameter is ParamArrayParameterSymbol)
            {
                var collected = CollectParamArrayArguments(session, context, mapResult.ParamArrayArguments);
                if (collected.Error is { } collectError)
                {
                    return collectError;
                }

                bound[i] = collected.Value!;
                continue;
            }

            var argumentNode = mapped[i];

            if (argumentNode is null or MissingArgumentNode)
            {
                // Unmapped, always Optional here (MapArguments already errored otherwise) - no caller
                // expression to Let-coerce or alias, so the value an omitted argument gives the parameter.
                var omitted = OmittedArgument(session, parameter);
                if (!omitted.IsSuccess)
                {
                    return omitted;
                }

                bound[i] = omitted.Result!.RuntimeValue;
                continue;
            }

            var aliased =RuntimeProcedureInvoker.IsByRef(parameter.ParameterKind) || external && parameter.ResolvedType is VBStringType;
            RuntimeSemanticsEvaluationResult? evaluatedInstead = null;
            if (aliased && TryResolveByRefArgument(session, context, argumentNode, parameter, borrowed, out var reference, out evaluatedInstead, external))
            {
                bound[i] = reference;
                continue;
            }

            // an argument is evaluated once: what binding it by reference already evaluated is what is passed, and not evaluated again.
            var argumentResult = evaluatedInstead ?? EvaluateIndexArgument(session, argumentNode, context);
            if (argumentResult is { } evaluated && !evaluated.IsSuccess)
            {
                return evaluated;
            }
            if (argumentResult is null)
            {
                return RuntimeSemanticsEvaluationResult.InternalError();
            }

            // what is passed is a value, which keeps nothing of whether it is known: the call is told here.
            isKnown &= !argumentResult.Value.Result!.IsIndeterminate;

            // an object passed to a parameter declared as a class or as Object is Set-assigned to the parameter's new
            // local (MS-VBAL §5.3.1.11): the parameter holds the reference, and nothing asks the object for a value. So is
            // one passed to a Variant, which holds the object itself: "if the value type of the argument is a specific class
            // or Nothing, its data value is Set-assigned" to the local - not what the object's default member returns. That is
            // the argument's value type, which is the object's class whether the object is held by an object variable or by a Variant.
            if (SetCoercion is { } setCoercion
                && HeldObject(argumentResult.Value.Result) is { } passedObject
                && parameter.ResolvedType is VBClassType or VBObjectType or VBVariantType)
            {
                var setResult = setCoercion.EvaluateSetCoercion(session, argumentNode, passedObject, parameter.ResolvedType);
                if (!setResult.IsSuccess)
                {
                    return RuntimeSemanticsEvaluationResult.Error(setResult.ErrorInfo!);
                }

                bound[i] = setResult.Result!.RuntimeValue;
                continue;
            }

            // `As Any` has no type to coerce to: the argument is passed as what it is.
            if (external && parameter.ResolvedType is VBUnknownType)
            {
                bound[i] = argumentResult.Value.Result!.RuntimeValue;
                continue;
            }

            // ByVal parameter passing Let-coerces the argument to the parameter's own declared type
            // (MS-VBAL §5.5.1.2) before it's ever wrapped into a fresh binding - the same rule any other
            // Let-target follows, a literal "5" (Integer) passed to a Long parameter included. A ByRef
            // parameter whose argument wasn't recognized as an aliasable variable above falls through to
            // this exact same path (MS-VBAL §5.3.1.11's own "otherwise" case) - a fresh local, never a
            // reported error.
            // by its plan when it can be (IR lowering S0.9): a number, a Boolean, a date or a String let-coerced to a parameter of a type a plan converts to.
            if (TryConvertUnobserved(argumentResult.Value.Result!, parameter.ResolvedType, out var planned))
            {
                bound[i] = planned.RuntimeValue;
                continue;
            }

            var coercionFrame = new LetCoercionStackFrame(argumentNode.Identity, InputIndex.CoercionSourceValue,
                argumentResult.Value.Result!, new VBTypeDescValue(parameter.ResolvedType), ConversionSite.Argument);
            var coercionResult = LetCoercionProvider.EvaluateLetCoercionSemantics(session.Symbols.Resolver, argumentNode, coercionFrame);
            if (!coercionResult.IsApplicable)
            {
                return RuntimeSemanticsEvaluationResult.InternalError();
            }
            if (!coercionResult.IsSuccess)
            {
                return RuntimeSemanticsEvaluationResult.Error(coercionResult.ErrorInfo!);
            }

            // an array coerced to an array parameter is a fresh copy nothing has bound yet, so it has no
            // RuntimeValue of its own to hand over: it is boxed around itself instead, the same shape
            // SymbolAddressTable.FreshBinding gives an array variable and a ParamArray is collected into. A
            // standard-library array parameter always arrives here, being ByVal; a ByRef one does when its
            // argument is not a variable it can alias.
            // 🚧 TODO: that includes a fixed-size array passed to a ByRef dynamic array parameter, which VBA passes
            // by reference and this copies - TryResolveByRefArgument aliases only an argument of the parameter's
            // own declared type.
            bound[i] = coercionResult.Result is VBArrayValue array
                ? new VBRuntimeValue<VBRuntimeArrayValue>(new VBRuntimeArrayValue(array))
                : coercionResult.Result!.RuntimeValue;
        }

        arguments = bound;
        return null;
    }

    private readonly record struct ParamArrayCollectResult(IRuntimeValue? Value, RuntimeSemanticsEvaluationResult? Error);

    // MS-VBAL §5.3.1.11: the trailing extra positional arguments become a fresh, 0-based Variant array
    // bound to the ParamArray parameter - never an alias onto anything the caller passed, since there's
    // no single caller variable the whole collection could reference. Each element is Let-coerced to
    // Variant, the same rule any other ByVal argument follows. Boxed as VBRuntimeArrayValue (matching
    // SymbolAddressTable.FreshBinding's own shape for an array value) so VBArrayType.CreateValue can
    // unbox it correctly once RuntimeProcedureInvoker pushes it onto the callee's frame.
    private ParamArrayCollectResult CollectParamArrayArguments(IRuntimeSession session, RuntimeEvaluationContext context, ImmutableArray<ExpressionNode> argumentNodes)
    {
        // a ParamArray that collects nothing is still an array of one dimension, with no elements: UBound of it is -1 (whatever Option Base says,
        // it is 0-based), and IsMissing of it is True, as MS-VBA has it.
        var array = new VBFixedSizeArrayValue([(0, argumentNodes.Length - 1)]);
        for (var i = 0; i < argumentNodes.Length; i++)
        {
            var argumentResult = EvaluateIndexArgument(session, argumentNodes[i], context);
            if (argumentResult is { } evaluated && !evaluated.IsSuccess)
            {
                return new ParamArrayCollectResult(null, evaluated);
            }
            if (argumentResult is null)
            {
                return new ParamArrayCollectResult(null, RuntimeSemanticsEvaluationResult.InternalError());
            }

            // an object is Set-assigned to the Variant it is collected into, as it is to a Variant parameter of its own (MS-VBAL §5.3.1.11): the element holds the
            // object, and nothing asks it for a default value it may not have - which is what CallByName(obj, "Prop", VbSet, other) passes.
            if (SetCoercion is { } setCoercion && argumentResult.Value.Result is VBObjectValue)
            {
                var setResult = setCoercion.EvaluateSetCoercion(session, argumentNodes[i], argumentResult.Value.Result!, VBVariantType.TypeInfo);
                if (!setResult.IsSuccess)
                {
                    return new ParamArrayCollectResult(null, RuntimeSemanticsEvaluationResult.Error(setResult.ErrorInfo!));
                }

                array.TrySetElement(BindingKnowledge.NewCell(setResult.Result!), i);
                continue;
            }

            var coercionFrame = new LetCoercionStackFrame(argumentNodes[i].Identity, InputIndex.CoercionSourceValue,
                argumentResult.Value.Result!, new VBTypeDescValue(VBVariantType.TypeInfo), ConversionSite.Argument);
            var coercionResult = LetCoercionProvider!.EvaluateLetCoercionSemantics(session.Symbols.Resolver, argumentNodes[i], coercionFrame);
            if (!coercionResult.IsApplicable)
            {
                return new ParamArrayCollectResult(null, RuntimeSemanticsEvaluationResult.InternalError());
            }
            if (!coercionResult.IsSuccess)
            {
                return new ParamArrayCollectResult(null, RuntimeSemanticsEvaluationResult.Error(coercionResult.ErrorInfo!));
            }

            array.TrySetElement(BindingKnowledge.NewCell(coercionResult.Result!), i);
        }

        return new ParamArrayCollectResult(new VBRuntimeValue<VBRuntimeArrayValue>(new VBRuntimeArrayValue(array)), null);
    }

    private readonly record struct ArgumentMapResult(ExpressionNode?[]? Mapped, ImmutableArray<ExpressionNode> ParamArrayArguments, RuntimeSemanticsEvaluationResult? Error);

    // the object a value is, or a Variant holds - however many Variants deep.
    private static VBObjectValue? HeldObject(VBTypedValue? value) => value switch
    {
        VBObjectValue reference => reference,
        VBVariantValue variant => HeldObject(variant.TypedValue),
        _ => null,
    };

    // MS-VBAL §5.3.1.11 argument mapping. A trailing ParamArray parameter collects every positional
    // argument from its own position onward instead of mapping 1:1 - never targetable by name, and
    // always considered satisfied even with nothing collected (an empty array, not error 449).
    private static ArgumentMapResult MapArguments(ImmutableArray<VBParameterSymbol> parameters, ImmutableArray<ExpressionNode> argumentNodes)
    {
        // `Area()` is a call that supplies nothing; the parser reads the empty parentheses as one omitted argument, which a
        // procedure that takes none has no parameter for.
        if (parameters.IsEmpty && argumentNodes is [MissingArgumentNode])
        {
            argumentNodes = [];
        }

        var paramArrayIndex = parameters.Length > 0 && parameters[^1] is ParamArrayParameterSymbol ? parameters.Length - 1 : -1;
        var mapped = new ExpressionNode?[parameters.Length];
        var paramArrayArguments = ImmutableArray.CreateBuilder<ExpressionNode>();
        var positionalIndex = 0;

        foreach (var argument in argumentNodes)
        {
            if (argument is NamedArgumentNode named)
            {
                var index = IndexOfParameter(parameters, named.Name);
                if (index < 0 || index == paramArrayIndex || mapped[index] is not null)
                {
                    return new ArgumentMapResult(null, [], RuntimeSemanticsEvaluationResult.Error(
                        VBRuntimeErrorInfo.For(VBRuntimeErrorId.NamedArgumentNotFound, argument.Location, Exceptions.VBNamedArgumentNotFound_UnknownOrDuplicate_Verbose)));
                }

                mapped[index] = named.Value;
                continue;
            }

            if (paramArrayIndex >= 0 && positionalIndex >= paramArrayIndex)
            {
                paramArrayArguments.Add(argument);
                positionalIndex++;
                continue;
            }

            if (positionalIndex >= parameters.Length)
            {
                return new ArgumentMapResult(null, [], RuntimeSemanticsEvaluationResult.Error(
                    VBRuntimeErrorInfo.For(VBRuntimeErrorId.WrongNumberOfArgumentsOrInvalidPropertyAssignment, argument.Location, Exceptions.VBWrongNumberOfArguments_Verbose)));
            }

            if (argument is MissingArgumentNode && !parameters[positionalIndex].IsOptional)
            {
                // Error 448, not 449 - MS-VBAL calls this out as checked here, not in the sweep below.
                return new ArgumentMapResult(null, [], RuntimeSemanticsEvaluationResult.Error(
                    VBRuntimeErrorInfo.For(VBRuntimeErrorId.NamedArgumentNotFound, argument.Location, Exceptions.VBNamedArgumentNotFound_MissingRequiredPositional_Verbose)));
            }

            mapped[positionalIndex] = argument;
            positionalIndex++;
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            if (i == paramArrayIndex)
            {
                continue; // always satisfied, even with nothing collected - an empty array, not error 449.
            }

            if (mapped[i] is null && !parameters[i].IsOptional)
            {
                return new ArgumentMapResult(null, [], RuntimeSemanticsEvaluationResult.Error(
                    VBRuntimeErrorInfo.For(VBRuntimeErrorId.ArgumentNotOptional, default, Exceptions.VBArgumentNotOptional_Verbose)));
            }
        }

        return new ArgumentMapResult(mapped, paramArrayArguments.ToImmutable(), null);
    }

    private static int IndexOfParameter(ImmutableArray<VBParameterSymbol> parameters, string name)
    {
        for (var i = 0; i < parameters.Length; i++)
        {
            if (string.Equals(parameters[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    // MS-VBAL §5.3.1.11: a ByRef parameter whose mapped argument's expression is "classified as a
    // variable" gets a real reference-parameter binding instead of a copy - narrowly recognized here as
    // a bare SimpleName resolving to an addressable, writable variable/parameter whose declared type
    // either is the parameter's own declared type or is Variant (the two shapes the spec allows a plain
    // reference binding for without a class/Object copy-back dance, which isn't modeled yet - an
    // Object-typed ByRef parameter falls through to a ByVal-style copy today, a documented, narrower-
    // than-spec gap rather than a wrong result). Anything else (an expression, a literal, a mismatched-
    // type argument, a read-only target) falls through to the same Let-coerced copy every ByVal argument
    // already gets - MS-VBAL's own "otherwise" case, never an error.
    private bool TryResolveByRefArgument(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode argument, VBParameterSymbol parameter, List<MemoryAddress> borrowed,
        out VBRuntimeReference reference, out RuntimeSemanticsEvaluationResult? evaluated, bool external = false)
    {
        reference = VBRuntimeReference.NullRef;
        evaluated = null;

        // an element of an array is a variable (MS-VBAL §5.6.13.1), passed by reference as one: MS-VBA writes what the callee assigns to the element, and the
        // array is locked for as long as the call runs (ICallStackFrame.LocksArray). A Declare is given the element where it is in the array: the reference
        // says which one, and the marshaller passes the array's data from there (a String a Declare takes ByVal is a buffer, not this).
        if (argument is IndexExpressionNode element && (!external || RuntimeProcedureInvoker.IsByRef(parameter.ParameterKind)))
        {
            return TryResolveElementByRefArgument(session, context, element, parameter, borrowed, out reference, out evaluated, external);
        }

        // `As Any` is a parameter of a Declare that takes whatever it is given, a variable of any type by reference - and passes it as the type it is.
        if (external && parameter.ResolvedType is VBUnknownType && argument is SimpleNameExpressionNode anyName
            && session.Symbols.Resolver.ResolveValue(anyName, ScopeKind.Local, context.Scope).Symbol is { } anySymbol and ITypedSymbol anyTyped
            && session.Symbols.Resolver.TryGetAddress(anySymbol, out var anyAddress))
        {
            reference = new VBRuntimeReference(anyAddress, DeclaredTypeOf(session, anySymbol, anyTyped.ResolvedType));
            return true;
        }

        // `Module1.Items` is the variable its bare name is, named through its module (MS-VBAL §5.6.12): bound as the name is, not as a member of a value.
        var qualified = argument is MemberAccessExpressionNode qualifiedAccess ? ResolveNamespaceQualified(session, context, qualifiedAccess) : null;
        if (qualified is null && argument is MemberAccessExpressionNode memberAccess)
        {
            return TryResolveFieldByRefArgument(session, context, memberAccess, parameter, out reference);
        }

        if (qualified is null && argument is not SimpleNameExpressionNode)
        {
            return false;
        }

        var named = qualified ?? session.Symbols.Resolver.ResolveValue((SimpleNameExpressionNode)argument, ScopeKind.Local, context.Scope).Symbol;
        // a ParamArray is never passed by reference: MS-VBA refuses it as an argument of a ByRef parameter ("Invalid ParamArray use"), and passes it to the
        // library as a value - IsMissing of one that collected nothing is True (recorded in Excel).
        if (named is not { } argumentSymbol || argumentSymbol is ParamArrayParameterSymbol || argumentSymbol is not ITypedSymbol typed
            || (!IsOfDeclaredType(parameter, typed.ResolvedType) && parameter.ResolvedType is not VBVariantType))
        {
            return false;
        }

        if (!session.Symbols.Resolver.TryGetAddress(argumentSymbol, out var address)
            || !session.Symbols.Resolver.GetValue(argumentSymbol).BindingCapabilities.HasFlag(BindingCapabilities.SetValue))
        {
            return false;
        }

        reference = new VBRuntimeReference(address, ReferencedType(parameter, DeclaredTypeOf(session, argumentSymbol, typed.ResolvedType)));
        return true;
    }

    // the element's own cell, given an address of its own for as long as the call that is made with it runs - which the caller gives back (borrowed). An
    // element whose cell cannot be written yet (the inert one an element of an object or a Variant array starts with) is given one that can, holding its
    // default, as an assignment to it would. A callee that is not a variable - a function, a property - is not reached into: its element is a value.
    // Once the subscripts are evaluated, an element that is not bound is passed as what they select - an error, or a value - and they are not evaluated again
    // (MS-VBA evaluates them once: a subscript outside the bounds is error 9, raised by the call's argument).
    private bool TryResolveElementByRefArgument(
        IRuntimeSession session, RuntimeEvaluationContext context, IndexExpressionNode element, VBParameterSymbol parameter, List<MemoryAddress> borrowed,
        out VBRuntimeReference reference, out RuntimeSemanticsEvaluationResult? evaluated, bool external = false)
    {
        reference = VBRuntimeReference.NullRef;
        evaluated = null;
        if (element.Arguments.IsDefaultOrEmpty || !DenotesAVariable(session, context, element.Callee))
        {
            return false;
        }

        var callee = Evaluate(session, element.Callee, context);
        var held = callee.IsSuccess ? callee.Result : null;
        while (held is VBVariantValue { TypedValue: var wrapped })
        {
            held = wrapped;
        }

        // `As Any` takes an element of any type, as it takes a variable of any type.
        if (held is not VBArrayValue { IsIndeterminate: false } array
            || (!IsOfDeclaredType(parameter, array.ItemType) && parameter.ResolvedType is not VBVariantType && !(external && parameter.ResolvedType is VBUnknownType)))
        {
            return false;
        }

        if (EvaluateSubscripts(session, context, element.Arguments, out var indices, out var indicesKnown) is { } failed)
        {
            evaluated = failed;
            return false;
        }

        // a subscript that is not known selects an element that could be any of them: what is passed is not known either.
        if (!indicesKnown)
        {
            evaluated = RuntimeSemanticsEvaluationResult.Success(array.ItemType.CreateIndeterminateValue());
            return false;
        }

        if (array.GetElementHandle(indices) is not { } cell)
        {
            evaluated = RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.SubscriptOutOfRange, element.Location,
                string.Join(", ", indices)));
            return false;
        }

        if (!cell.BindingCapabilities.HasFlag(BindingCapabilities.SetValue))
        {
            array.TrySetElement(BindingKnowledge.NewCell(array.ItemType.DefaultValue.RuntimeValue, isKnown: true), indices);
            cell = array.GetElementHandle(indices)!;
        }

        if (!session.Storage.TryAllocate(array.ItemType.DefaultValue.Size, cell, out var address))
        {
            evaluated = array[indices] is { } value ? RuntimeSemanticsEvaluationResult.Success(value) : null;
            return false;
        }

        borrowed.Add(address);
        reference = external
            ? new VBRuntimeReference(address, parameter.ResolvedType is VBUnknownType ? array.ItemType : ReferencedType(parameter, array.ItemType),
                new VBArrayElementLocation(array, indices))
            : new VBRuntimeReference(address, ReferencedType(parameter, array.ItemType));
        return true;
    }

    // what the elements passed by reference were given to be passed as: the call they were passed to is over.
    private static void GiveBack(IRuntimeSession session, List<MemoryAddress> borrowed)
    {
        foreach (var address in borrowed)
        {
            session.Storage.TryDeallocate(address);
        }

        borrowed.Clear();
    }

    // whether a variable of this declared type is one a ByRef parameter can be bound to without a copy: one of the parameter's own declared type, an
    // enumeration being the declared type Long (MS-VBAL §5.2.3.4), so a variable of one and a Long parameter, or two enumerations, are the same type.
    private static bool IsOfDeclaredType(VBParameterSymbol parameter, VBType variableType)
        => VBEnumType.AsDeclaredType(parameter.ResolvedType).Equals(VBEnumType.AsDeclaredType(variableType));

    // MS-VBAL §5.3.1.11: a Variant parameter given a variable of another type is bound to it, and the binding knows what the variable is declared to be - the
    // binding reads as a Variant, and is assigned as that type. A parameter of the variable's own type needs nothing more than the address.
    private static VBType? ReferencedType(VBParameterSymbol parameter, VBType variableType)
        => parameter.ResolvedType is VBVariantType && !variableType.Equals(parameter.ResolvedType) ? variableType : null;

    // the declared type of the variable a name is: its own, unless the name is a reference parameter of the current activation bound to a variable of another
    // type - which is the variable it hands on when it is passed by reference again.
    private static VBType DeclaredTypeOf(IRuntimeSession session, Symbol symbol, VBType declared)
        => session.CallStack.Current is { } current && current.TryGetReferencedType(symbol, out var referenced) ? referenced : declared;

    // MS-VBAL §5.3.1.11: a public variable of an object is a variable too, with an address of its own, and a ByRef parameter is a second name for it
    // when the types agree, as for a variable of the code's own - which is also what lets a callee lock it (§5.4.3.3). A property, and the field of a
    // user-defined type, which has no address of its own, fall through to the copy.
    private bool TryResolveFieldByRefArgument(
        IRuntimeSession session, RuntimeEvaluationContext context, MemberAccessExpressionNode memberAccess, VBParameterSymbol parameter, out VBRuntimeReference reference)
    {
        reference = VBRuntimeReference.NullRef;

        var evaluated = EvaluateOwner(session, context, memberAccess);
        if (!evaluated.IsSuccess
            || UnwrappedOwner(evaluated.Result!) is not VBObjectValue owner
            || ResolveAssignableMember(session, context, memberAccess, owner, isSet: false)
                is not { Field: ITypedSymbol { ResolvedType: { } fieldType } field, Instance: { } instance })
        {
            return false;
        }

        if (!IsOfDeclaredType(parameter, fieldType) && parameter.ResolvedType is not VBVariantType)
        {
            return false;
        }

        if (!instance.TryGetAddress((Symbol)field, out var address)
            || !instance.GetValue((Symbol)field).BindingCapabilities.HasFlag(BindingCapabilities.SetValue))
        {
            return false;
        }

        reference = new VBRuntimeReference(address, ReferencedType(parameter, fieldType));
        return true;
    }

    // MissingArgumentNode is a placeholder, not a value - element access needs every subscript, so it
    // reports as unresolved rather than being silently skipped the way the static evaluator's own
    // type-checking pass can afford to.
    private RuntimeSemanticsEvaluationResult? EvaluateIndexArgument(IRuntimeSession session, ExpressionNode argument, RuntimeEvaluationContext context)
        => argument switch
        {
            MissingArgumentNode => null,
            NamedArgumentNode named => Evaluate(session, named.Value, context),
            // AddressOf's Target names a procedure to reference, never one to call or read a value
            // from, so this never delegates to Evaluate the way every other argument shape does.
            AddressOfExpressionNode => RuntimeSemanticsEvaluationResult.InternalError(),
            _ => Evaluate(session, argument, context),
        };

    private static bool TryGetIntegralSubscript(VBTypedValue? value, out int subscript)
    {
        switch (value)
        {
            case VBIntegerValue integer: subscript = integer.Value; return true;
            case VBLongValue @long: subscript = @long.Value; return true;
            default: subscript = 0; return false;
        }
    }

    private RuntimeSemanticsEvaluationResult EvaluateDictionaryAccess(IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode expression, DictionaryAccessExpressionNode dictionaryAccess)
    {
        // the object the member is looked up in: the expression written before the bang, or the enclosing With block's target.
        var ownerResult = dictionaryAccess.Owner is { } ownerExpression
            ? Evaluate(session, ownerExpression, context)
            : context.EnclosingWithTarget is { } withTarget
                ? RuntimeSemanticsEvaluationResult.Success(withTarget)
                : RuntimeSemanticsEvaluationResult.InternalError();
        return ownerResult.IsSuccess ? AccessThroughDictionary(session, context, dictionaryAccess, ownerResult.Result!) : ownerResult;
    }

    // the default member of the owner, called with the name after the bang, and the access stated as one.
    private RuntimeSemanticsEvaluationResult AccessThroughDictionary(
        IRuntimeSession session, RuntimeEvaluationContext context, DictionaryAccessExpressionNode dictionaryAccess, VBTypedValue owner)
    {
        var assumedBefore = AssumedSoFar;
        var result = CallThroughDictionary(session, context, dictionaryAccess, dictionaryAccess, owner);
        ObserveMemberAccess(session, context, dictionaryAccess, Tokens.DictionaryAccess, dictionaryAccess.Owner, owner, null,
            MemberAccessOperationSemanticFlags.Implicit | MemberAccessOperationSemanticFlags.DefaultMember | MemberAccessOperationSemanticFlags.DictionaryAccess,
            assumedBefore, result);
        return result;
    }

    private RuntimeSemanticsEvaluationResult CallThroughDictionary(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode expression, DictionaryAccessExpressionNode dictionaryAccess, VBTypedValue owner)
    {
        while (owner is VBVariantValue { TypedValue: { } wrapped })
        {
            owner = wrapped;
        }

        if (owner is not VBObjectValue objectValue)
        {
            return RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.ObjectRequired, expression.Location, "A dictionary access is a call of the default member of an object."));
        }

        if (objectValue.IsNothing())
        {
            return RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet, expression.Location, Exceptions.VBMemberAccess_ObjectVariableNotSet_Verbose));
        }

        // owner!member is always sugar for a call through owner's default member (MS-VBAL §5.6.14), with the name of the member as its argument -
        // never a plain read. An object that has no default member to bind the call to does not support it: error 438.
        if (TryResolveDefaultMember(session, objectValue) is not { } defaultMember)
        {
            return RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, expression.Location, "The object has no default member to call with the name of the member."));
        }

        var name = new LiteralExpressionNode(
            dictionaryAccess.Member.Identity, dictionaryAccess.Member.Location, new VBStringValue(dictionaryAccess.Member.IdentifierName));
        return InvokeProcedure(session, context, defaultMember.Member, [name], defaultMember.Receiver);
    }

    private RuntimeSemanticsEvaluationResult EvaluateTypeOfIs(IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode expression, TypeOfIsExpressionNode typeOfIs)
    {
        var operandResult = Evaluate(session, typeOfIs.Operand, context);
        if (!operandResult.IsSuccess)
        {
            return operandResult;
        }

        if (operandResult.Result is not VBObjectValue objectValue || objectValue.IsNothing())
        {
            return RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(false));
        }

        if (TryGetQualifiedTypeName(typeOfIs.TypeExpression) is not var (qualifier, typeName) || typeName is null)
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        var targetResult = VBProjectSymbol.ResolveQualifiedType(session.Symbols.Resolver, qualifier, typeName, context.Scope);
        return targetResult.Symbol is VBClassModuleSymbol targetClass
            ? IsOfClass(session, operandResult, targetClass)
            : RuntimeSemanticsEvaluationResult.InternalError();
    }

    // whether the object an operand evaluated to is an instance of a class that is, or implements, the class tested against: Nothing and a value that is no
    // object are not.
    private static RuntimeSemanticsEvaluationResult IsOfClass(IRuntimeSession session, RuntimeSemanticsEvaluationResult operandResult, VBClassModuleSymbol targetClass)
    {
        if (operandResult.Result is not VBObjectValue objectValue || objectValue.IsNothing())
        {
            return RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(false));
        }

        return session.Symbols.TryGetInstance(objectValue.Value, out var instance)
            ? RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(IsOrImplements(instance.ClassModule, targetClass, [])))
            : RuntimeSemanticsEvaluationResult.InternalError();
    }

    // Uri.Equals/GetHashCode ignore the fragment, where a Symbol's own identity lives - AbsoluteUri
    // string comparison is the safe accessor here, the same as everywhere else in the codebase that
    // compares two symbols' Uris for identity (see RuntimeSession.CreateInstance's own field filter).
    private static bool IsOrImplements(VBClassModuleSymbol classModule, VBClassModuleSymbol target, HashSet<string> visited)
    {
        if (classModule.Uri.AbsoluteUri == target.Uri.AbsoluteUri)
        {
            return true;
        }

        if (!visited.Add(classModule.Uri.AbsoluteUri))
        {
            return false;
        }

        foreach (var implemented in classModule.ImplementedInterfaces)
        {
            if (IsOrImplements(implemented, target, visited))
            {
                return true;
            }
        }

        return false;
    }

    // New Project.ClassName (MS-VBAL 5.6.4's type binding context) arrives as a MemberAccessExpressionNode
    // - Owner is the project qualifier, Member the class name - the same shape TypeOf...Is's TypeExpression
    // uses. A deeper/other shape isn't modeled - defer rather than misreport, same as the static evaluator.
    private static (string? Qualifier, string? TypeName)? TryGetQualifiedTypeName(ExpressionNode typeExpression)
        => typeExpression switch
        {
            SimpleNameExpressionNode simple => (null, simple.IdentifierName),
            MemberAccessExpressionNode { Owner: SimpleNameExpressionNode owner, Member: { } member } => (owner.IdentifierName, member.IdentifierName),
            _ => (null, null),
        };

    private RuntimeSemanticsEvaluationResult EvaluateBinaryOperator(IRuntimeSession session, RuntimeEvaluationContext context, VBBinaryOperatorExpressionNode binaryOperator)
    {
        var leftResult = Evaluate(session, binaryOperator.Left, context);
        if (!leftResult.IsSuccess)
        {
            return leftResult;
        }
        var rightResult = Evaluate(session, binaryOperator.Right, context);
        if (!rightResult.IsSuccess)
        {
            return rightResult;
        }

        return OperatorProvider.EvaluateBinaryOperator(session, binaryOperator, leftResult.Result!, rightResult.Result!);
    }

    private RuntimeSemanticsEvaluationResult EvaluateUnaryOperator(IRuntimeSession session, RuntimeEvaluationContext context, VBUnaryOperatorExpressionNode unaryOperator)
    {
        var operandResult = Evaluate(session, unaryOperator.Operand, context);
        if (!operandResult.IsSuccess)
        {
            return operandResult;
        }

        return OperatorProvider.EvaluateUnaryOperator(session, unaryOperator, operandResult.Result!);
    }
}
