using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics.Expressions;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Literals;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Static;
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
public sealed class RuntimeExpressionEvaluator(IOperatorRuntimeSemanticsProvider OperatorProvider)
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
    /// Let-coerces each argument of a bare <c>Sub</c> call to its own parameter's declared type
    /// (<strong>MS-VBAL §5.5.1.2</strong>) before passing it to <see cref="ProcedureInvoker"/> - ByVal
    /// parameter passing is itself a Let-target, the same as any other. Settable for the same
    /// construction-order reason as <see cref="ProcedureInvoker"/>.
    /// </summary>
    public ILetCoercionRuntimeSemanticsProvider? LetCoercionProvider { get; set; }

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
        => expression switch
        {
            LiteralExpressionNode => LiteralExpressionRuntimeSemantics.Instance.Evaluate(session, new(), expression),
            SimpleNameExpressionNode simpleName => EvaluateSimpleName(session, context, simpleName),
            PrecompilerNameExpressionNode precompilerName => EvaluatePrecompilerConstant(session, precompilerName),
            InstanceExpressionNode => EvaluateInstance(session, context, expression),
            NewExpressionNode newExpression => EvaluateNew(session, context, expression, newExpression),
            MemberAccessExpressionNode memberAccess => EvaluateMemberAccess(session, context, expression, memberAccess),
            IndexExpressionNode indexExpression => EvaluateIndex(session, context, expression, indexExpression),
            DictionaryAccessExpressionNode dictionaryAccess => EvaluateDictionaryAccess(session, context, expression, dictionaryAccess),
            TypeOfIsExpressionNode typeOfIs => EvaluateTypeOfIs(session, context, expression, typeOfIs),
            VBBinaryOperatorExpressionNode binaryOperator => EvaluateBinaryOperator(session, context, binaryOperator),
            VBUnaryOperatorExpressionNode unaryOperator => EvaluateUnaryOperator(session, context, unaryOperator),
            _ => RuntimeSemanticsEvaluationResult.InternalError(),
        };

    private RuntimeSemanticsEvaluationResult EvaluateSimpleName(IRuntimeSession session, RuntimeEvaluationContext context, SimpleNameExpressionNode simpleName)
    {
        var result = session.Symbols.Resolver.ResolveValue(simpleName.IdentifierName, ScopeKind.Local, context.Scope);

        if (result.Symbol is VBProcedureMemberSymbol sub)
        {
            // a bare reference to a Sub, with no enclosing Index to supply arguments, is a call with
            // none (MS-VBAL §5.6.10) - "Foo" alone, or Call Foo's own Callee.
            return InvokeProcedure(session, context, sub, []);
        }

        if (result.Symbol is VBFunctionMemberSymbol or VBPropertyGetMemberSymbol)
        {
            var returningMember = (VBTypeMemberSymbol)result.Symbol;
            // Deliberately NOT VBReturningMemberSymbol (its own base type): that also covers
            // Const/EnumConst/module-and-instance fields/UDT fields, every one of them a plain value to
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
            // none of its own arguments to supply.
            return returningMember.Uri.AbsoluteUri == context.Scope.AbsoluteUri && session.CallStack.Current is { } enclosing
                ? RuntimeSemanticsEvaluationResult.Success(enclosing.ReturnValue!)
                : InvokeProcedure(session, context, returningMember, []);
        }

        // static semantics should already have rejected an unresolved, ambiguous, or duplicate name;
        // reaching here means that check was skipped.
        return result.Symbol is ITypedSymbol typed
            ? RuntimeSemanticsEvaluationResult.Success(typed.ResolvedType.CreateValue(session.Symbols.Resolver.GetValue(result.Symbol)))
            : RuntimeSemanticsEvaluationResult.InternalError();
    }

    // MS-VBAL §5.6.16.2: a conditional-compilation constant that names nothing is the value 0 - not a
    // compile error, and Option Explicit (a variable-declaration concern) has no bearing on it.
    private static RuntimeSemanticsEvaluationResult EvaluatePrecompilerConstant(IRuntimeSession session, PrecompilerNameExpressionNode name)
    {
        var result = session.Symbols.Resolver.ResolveValue(name.Name, ScopeKind.Global, StaticSymbol.GlobalUri);
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
            ? NewExpressionRuntimeSemantics.Instance.Evaluate(session, new(), expression, new VBSymbolDescValue(classModule))
            : RuntimeSemanticsEvaluationResult.InternalError();
    }

    private RuntimeSemanticsEvaluationResult EvaluateMemberAccess(IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode expression, MemberAccessExpressionNode memberAccess)
    {
        VBTypedValue owner;
        if (memberAccess.Owner is { } ownerExpression)
        {
            var ownerResult = Evaluate(session, ownerExpression, context);
            if (!ownerResult.IsSuccess)
            {
                return ownerResult;
            }
            owner = ownerResult.Result!;
        }
        else if (context.EnclosingWithTarget is { } withTarget)
        {
            owner = withTarget;
        }
        else
        {
            // MS-VBAL §5.6.15: invalid with no enclosing With block - static semantics should already
            // have rejected this.
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        return EvaluateInstanceField(session, owner, memberAccess.Member.IdentifierName);
    }

    // A late-bound Variant/Object member, and a call through a Property/Function/Sub member, are both
    // invocations, not reads - only a Field/Variable-kind member is readable this way.
    private static RuntimeSemanticsEvaluationResult EvaluateInstanceField(IRuntimeSession session, VBTypedValue owner, string memberName)
    {
        if (owner is not VBObjectValue objectValue || objectValue.IsNothing()
            || !session.Symbols.TryGetInstance(objectValue.Value, out var instance))
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        var member = instance.ClassModule.DefaultInterfaceMembers
            .FirstOrDefault(candidate => string.Equals(candidate.Name, memberName, StringComparison.OrdinalIgnoreCase));

        return member is { Kind: SymbolKindExt.Field or SymbolKindExt.Variable }
            ? RuntimeSemanticsEvaluationResult.Success(member.ResolvedType.CreateValue(instance.GetValue(member)))
            : RuntimeSemanticsEvaluationResult.InternalError();
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
        if (TryResolveCallableSub(session, context, indexExpression.Callee) is { } sub)
        {
            return InvokeProcedure(session, context, sub, indexExpression.Arguments);
        }

        var calleeResult = Evaluate(session, indexExpression.Callee, context);
        if (!calleeResult.IsSuccess)
        {
            return calleeResult;
        }

        if (calleeResult.Result is not VBArrayValue array)
        {
            // any other Callee shape is a function/property call, not an element read.
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        var subscripts = new int[indexExpression.Arguments.Length];
        for (var i = 0; i < indexExpression.Arguments.Length; i++)
        {
            var argumentResult = EvaluateIndexArgument(session, indexExpression.Arguments[i], context);
            if (argumentResult is { } evaluated && !evaluated.IsSuccess)
            {
                return evaluated;
            }
            if (argumentResult is null || !TryGetIntegralSubscript(argumentResult.Value.Result, out var subscript))
            {
                return RuntimeSemanticsEvaluationResult.InternalError();
            }
            subscripts[i] = subscript;
        }

        var element = array[subscripts];
        return element is not null
            ? RuntimeSemanticsEvaluationResult.Success(element)
            : RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.SubscriptOutOfRange, expression.Location,
                string.Join(", ", subscripts)));
    }

    private static VBTypeMemberSymbol? TryResolveCallableSub(IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode callee)
    {
        if (callee is not SimpleNameExpressionNode simpleName)
        {
            return null;
        }

        var result = session.Symbols.Resolver.ResolveValue(simpleName.IdentifierName, ScopeKind.Local, context.Scope);
        return result.Symbol is VBProcedureMemberSymbol or VBFunctionMemberSymbol or VBPropertyGetMemberSymbol
            ? (VBTypeMemberSymbol)result.Symbol
            : null;
    }

    // Same module, no Me, no ParamArray (needs array-typed argument passing, a separate, pre-existing
    // gap RuntimeProcedureInvoker's own ByVal path has for ANY array-typed parameter - VBArrayType.CreateValue
    // requires a VBRuntimeArrayValue-boxed handle a plain ValueBindingHandle never supplies, so this
    // isn't ParamArray-specific to fix), no depth-guard-aware error attribution beyond what
    // RuntimeProcedureInvoker itself reports. ProcedureInvoker/LetCoercionProvider never having been
    // wired in is InternalError - a real composition gap this slice doesn't newly introduce.
    private RuntimeSemanticsEvaluationResult InvokeProcedure(IRuntimeSession session, RuntimeEvaluationContext context, VBTypeMemberSymbol procedure, ImmutableArray<ExpressionNode> argumentNodes)
    {
        var parameters = RuntimeProcedureInvoker.GetParameters(procedure);
        if (ProcedureInvoker is null || LetCoercionProvider is null)
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        var mapResult = MapArguments(parameters, argumentNodes);
        if (mapResult.Error is { } mappingError)
        {
            return mappingError;
        }

        var mapped = mapResult.Mapped!;
        var arguments = new IRuntimeValue[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            var argumentNode = mapped[i];

            if (argumentNode is null or MissingArgumentNode)
            {
                // MS-VBAL §5.3.1.11: no argument mapped to this parameter - MapArguments already
                // rejected the case where that's true of a non-Optional one, so this is always a fresh
                // local Let-assigned from the parameter's own declared default (or its declared type's
                // own default, when none was specified) - never a reference binding, ByRef or not:
                // there is no caller expression to alias. Nothing to Let-coerce either: a default value
                // is already of the parameter's own declared type by construction.
                arguments[i] = (parameter.DefaultValue ?? parameter.ResolvedType.DefaultValue).RuntimeValue;
                continue;
            }

            if (RuntimeProcedureInvoker.IsByRef(parameter.ParameterKind)
                && TryResolveByRefArgument(session, context, argumentNode, parameter, out var reference))
            {
                arguments[i] = reference;
                continue;
            }

            var argumentResult = EvaluateIndexArgument(session, argumentNode, context);
            if (argumentResult is { } evaluated && !evaluated.IsSuccess)
            {
                return evaluated;
            }
            if (argumentResult is null)
            {
                return RuntimeSemanticsEvaluationResult.InternalError();
            }

            // ByVal parameter passing Let-coerces the argument to the parameter's own declared type
            // (MS-VBAL §5.5.1.2) before it's ever wrapped into a fresh binding - the same rule any other
            // Let-target follows, a literal "5" (Integer) passed to a Long parameter included. A ByRef
            // parameter whose argument wasn't recognized as an aliasable variable above falls through to
            // this exact same path (MS-VBAL §5.3.1.11's own "otherwise" case) - a fresh local, never a
            // reported error.
            var coercionFrame = new LetCoercionStackFrame(argumentNode.Identity, InputIndex.CoercionSourceValue,
                argumentResult.Value.Result!, new VBTypeDescValue(parameter.ResolvedType));
            var coercionResult = LetCoercionProvider.EvaluateLetCoercionSemantics(session.Symbols.Resolver, argumentNode, coercionFrame);
            if (!coercionResult.IsApplicable)
            {
                return RuntimeSemanticsEvaluationResult.InternalError();
            }
            if (!coercionResult.IsSuccess)
            {
                return RuntimeSemanticsEvaluationResult.Error(coercionResult.ErrorInfo!);
            }

            arguments[i] = coercionResult.Result!.RuntimeValue;
        }

        return ProcedureInvoker.Invoke(procedure, session.Symbols.Resolver, arguments);
    }

    private readonly record struct ArgumentMapResult(ExpressionNode?[]? Mapped, RuntimeSemanticsEvaluationResult? Error);

    // MS-VBAL §5.3.1.11's own mapping pass: each positional argument maps left-to-right to its
    // positional parameter; each named argument maps to the same-named parameter, whichever order they
    // arrive in relative to each other (a named argument always follows every positional one in real
    // source, so this never needs to reconcile the two against each other beyond "does this slot
    // already have a mapping"). ParamArray collection is deliberately not implemented here - see
    // InvokeProcedure's own doc for why - so an extra positional argument beyond the parameter count is
    // always error 450, even when the last parameter happens to be a ParamArray; the same gap means a
    // ParamArray's own "unmapped defaults to an empty array, never an error" rule isn't applied either
    // - a call that omits it errors 449 like any other missing non-Optional argument would, since
    // ParamArrayParameterSymbol.IsOptional is always false.
    private static ArgumentMapResult MapArguments(ImmutableArray<VBParameterSymbol> parameters, ImmutableArray<ExpressionNode> argumentNodes)
    {
        var mapped = new ExpressionNode?[parameters.Length];
        var positionalIndex = 0;

        foreach (var argument in argumentNodes)
        {
            if (argument is NamedArgumentNode named)
            {
                var index = IndexOfParameter(parameters, named.Name);
                if (index < 0 || mapped[index] is not null)
                {
                    return new ArgumentMapResult(null, RuntimeSemanticsEvaluationResult.Error(
                        VBRuntimeErrorInfo.For(VBRuntimeErrorId.NamedArgumentNotFound, argument.Location, Exceptions.VBNamedArgumentNotFound_UnknownOrDuplicate_Verbose)));
                }

                mapped[index] = named.Value;
                continue;
            }

            if (positionalIndex >= parameters.Length)
            {
                return new ArgumentMapResult(null, RuntimeSemanticsEvaluationResult.Error(
                    VBRuntimeErrorInfo.For(VBRuntimeErrorId.WrongNumberOfArgumentsOrInvalidPropertyAssignment, argument.Location, Exceptions.VBWrongNumberOfArguments_Verbose)));
            }

            if (argument is MissingArgumentNode && !parameters[positionalIndex].IsOptional)
            {
                // MS-VBAL §5.3.1.11: "If a positional argument is specified with its value omitted and
                // its mapped parameter is not optional, runtime error 448... is raised, EVEN IF a named
                // argument is later mapped to this parameter" - checked here, during mapping, rather
                // than folded into the general unmapped-parameter sweep below, which is error 449 for
                // every other "nothing landed in this slot at all" case.
                return new ArgumentMapResult(null, RuntimeSemanticsEvaluationResult.Error(
                    VBRuntimeErrorInfo.For(VBRuntimeErrorId.NamedArgumentNotFound, argument.Location, Exceptions.VBNamedArgumentNotFound_MissingRequiredPositional_Verbose)));
            }

            mapped[positionalIndex] = argument;
            positionalIndex++;
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            if (mapped[i] is null && !parameters[i].IsOptional)
            {
                return new ArgumentMapResult(null, RuntimeSemanticsEvaluationResult.Error(
                    VBRuntimeErrorInfo.For(VBRuntimeErrorId.ArgumentNotOptional, default, Exceptions.VBArgumentNotOptional_Verbose)));
            }
        }

        return new ArgumentMapResult(mapped, null);
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
    // either matches the parameter's own exactly or is Variant (the two shapes the spec allows a plain
    // reference binding for without a class/Object copy-back dance, which isn't modeled yet - an
    // Object-typed ByRef parameter falls through to a ByVal-style copy today, a documented, narrower-
    // than-spec gap rather than a wrong result). Anything else (an expression, a literal, a mismatched-
    // type argument, a read-only target) falls through to the same Let-coerced copy every ByVal argument
    // already gets - MS-VBAL's own "otherwise" case, never an error.
    private static bool TryResolveByRefArgument(IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode argument, VBParameterSymbol parameter, out VBRuntimeReference reference)
    {
        reference = VBRuntimeReference.NullRef;
        if (argument is not SimpleNameExpressionNode simpleName)
        {
            return false;
        }

        var result = session.Symbols.Resolver.ResolveValue(simpleName.IdentifierName, ScopeKind.Local, context.Scope);
        if (result.Symbol is not { } argumentSymbol || argumentSymbol is not ITypedSymbol typed
            || (!parameter.ResolvedType.Equals(typed.ResolvedType) && parameter.ResolvedType is not VBVariantType))
        {
            return false;
        }

        if (!session.Symbols.Resolver.TryGetAddress(argumentSymbol, out var address)
            || !session.Symbols.Resolver.GetValue(argumentSymbol).BindingCapabilities.HasFlag(BindingCapabilities.SetValue))
        {
            return false;
        }

        reference = new VBRuntimeReference(address);
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
        if (dictionaryAccess.Owner is { } ownerExpression)
        {
            var ownerResult = Evaluate(session, ownerExpression, context);
            if (!ownerResult.IsSuccess)
            {
                return ownerResult;
            }
        }
        else if (context.EnclosingWithTarget is null)
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        // owner!member is always sugar for a call through owner's default member (MS-VBAL §5.6.14) -
        // never a plain read.
        return RuntimeSemanticsEvaluationResult.InternalError();
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

        if (TryGetQualifiedTypeName(typeOfIs.TypeExpression) is not var (qualifier, typeName) || typeName is null
            || !session.Symbols.TryGetInstance(objectValue.Value, out var instance))
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        var targetResult = VBProjectSymbol.ResolveQualifiedType(session.Symbols.Resolver, qualifier, typeName, context.Scope);
        return targetResult.Symbol is VBClassModuleSymbol targetClass
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
