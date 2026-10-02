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
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;
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

            // an element is a Variant holding what the argument came to; an object in it is referenced by the cell, as by any variable.
            var value = element.Result is VBVariantValue variant ? variant : new VBVariantValue(element.Result!);
            array.TrySetElement(new ValueBindingHandle(value.RuntimeValue), lower + index);
            if (UnwrappedOwner(value) is VBObjectValue { } held && !held.IsNothing())
            {
                ObjectReferences.Rebind(session, array.GetElementHandle(lower + index)!, null, held);
            }
        }

        return RuntimeSemanticsEvaluationResult.Success(new VBVariantValue(array));
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

        return ArrayBoundRuntimeSemantics.Evaluate(arrayBound, array.Result!, dimension);
    }

    private RuntimeSemanticsEvaluationResult EvaluateSimpleName(IRuntimeSession session, RuntimeEvaluationContext context, SimpleNameExpressionNode simpleName)
        => ReadSymbol(
            session, context, session.Symbols.Resolver.ResolveValue(simpleName.IdentifierName, ScopeKind.Local, context.Scope).Symbol,
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
            // none of its own arguments to supply. Only a BARE name is the function's result variable: `Module1.Foo`
            // names the function, from anywhere, and is a call.
            return nameOfEnclosingFunctionIsItsResult
                && returningMember.Uri.AbsoluteUri == context.Scope.AbsoluteUri && session.CallStack.Current is { } enclosing
                    ? RuntimeSemanticsEvaluationResult.Success(enclosing.ReturnValue!)
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

        // static semantics should already have rejected an unresolved, ambiguous, or duplicate name;
        // reaching here means that check was skipped.
        if (symbol is not ITypedSymbol typed)
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        var handle = session.Symbols.Resolver.GetValue(symbol);
        var value = typed.ResolvedType.CreateValue(handle);

        // MS-VBAL §5.2.3.1.4 / §2.5.1: a variable declared As New - a class module's default instance among them - is
        // never Nothing when it is referred to: the reference creates the object it was waiting for, which is also
        // why `Is Nothing` of it can never be true.
        return symbol.GetProperty(SymbolProperties.AutoInstantiated) && value is VBObjectValue { } held && held.IsNothing()
            && typed.ResolvedType is VBClassType { Symbol: { } classModule }
                ? AutoInstantiate(session, handle, classModule)
                : RuntimeSemanticsEvaluationResult.Success(value);
    }

    private static RuntimeSemanticsEvaluationResult AutoInstantiate(IRuntimeSession session, IBindingHandle handle, VBClassModuleSymbol classModule)
    {
        // a declared type carries the class as it was when the type was built, and the class is what the instance is
        // made from: its members at the moment of the reference.
        if (session.Symbols.Resolver.ResolveType(classModule.Name, ScopeKind.Global, StaticSymbol.GlobalUri).Symbol is VBClassModuleSymbol current)
        {
            classModule = current;
        }

        var objectId = session.Objects.CreateObject();
        session.Symbols.CreateInstance(objectId, classModule);
        var created = new VBObjectValue(objectId);

        // the variable holds the object before Initialize runs, as it holds one a Set stored: the handler can already
        // reach it through the variable, and the reference must not be lost if it does.
        handle.SetValue(session.Symbols.Resolver, created.RuntimeValue);
        ObjectReferences.Rebind(session, handle, null, created);

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

    // the member of a namespace a qualified name refers to, or null when there is none to refer to - which static
    // semantics should have rejected, as it should an unresolved bare name.
    private static Symbol? ResolveNamespaceMember(
        IRuntimeSession session, RuntimeEvaluationContext context, Symbol qualifier, MemberAccessExpressionNode access)
        => session.Symbols.Resolver.ResolveMember(qualifier, access.Member.IdentifierName, context.Scope).Symbol is { } member
            && !NamespaceExpressions.IsNamespace(member) ? member : null;

    // null for anything that is not a workspace Const, and for a Const whose declaration carried no
    // expression - a library constant has a real binding to read instead, so it takes the path below.
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
    private static RuntimeSemanticsEvaluationResult EvaluatePrecompilerConstant(IRuntimeSession session, PrecompilerNameExpressionNode name)
    {
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
        if (result.Symbol is not VBClassModuleSymbol classModule)
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        var created = NewExpressionRuntimeSemantics.Instance.Evaluate(session, new(), expression, new VBSymbolDescValue(classModule));

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
        // no object to evaluate. `Strings.vbCrLf`, `Information.Erl`, `VBA.Strings.LenB`: the name is resolved
        // in the namespace and means what it would unqualified.
        if (memberAccess.Owner is { } namespaceExpression
            && TryClassifyNamespace(session, context, namespaceExpression) is { } qualifier)
        {
            return ReadSymbol(
                session, context, ResolveNamespaceMember(session, context, qualifier, memberAccess), nameOfEnclosingFunctionIsItsResult: false);
        }

        var ownerResult = EvaluateOwner(session, context, memberAccess);
        if (!ownerResult.IsSuccess)
        {
            return ownerResult;
        }

        var owner = ownerResult.Result!;
        var memberName = memberAccess.Member.IdentifierName;

        if (ObjectNotSet(memberAccess, owner) is { } notSet)
        {
            return notSet;
        }

        // a Property Get, Function or Sub of the object is an invocation with no arguments of its own; a field is a read.
        return TryResolveInvocableMember(session, context, memberAccess.Owner, owner, memberName) is { } found
            ? InvokeProcedure(session, context, found.Member, [], found.Receiver)
            : EvaluateInstanceField(session, owner, memberName);
    }

    // a member of an object variable that holds no object - never set, or set to Nothing - is error 91: there is no
    // object to find the member on. An As New variable is never in that state when it is referred to, which is what
    // made it the object the member is on.
    private static RuntimeSemanticsEvaluationResult? ObjectNotSet(MemberAccessExpressionNode memberAccess, VBTypedValue owner)
        => UnwrappedOwner(owner) is VBObjectValue { } unset && unset.IsNothing()
            ? RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet, memberAccess.Location, Exceptions.VBMemberAccess_ObjectVariableNotSet_Verbose))
            : null;

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

        // a Property Let or Set derives from the subroutine's symbol and is not one: reading the property is its Get.
        var member = instance.ClassModule.DefaultInterfaceMembers.FirstOrDefault(candidate =>
            candidate is VBPropertyGetMemberSymbol or VBFunctionMemberSymbol or VBProcedureMemberSymbol and not (VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol)
            && string.Equals(candidate.Name, memberName, StringComparison.OrdinalIgnoreCase));

        return member is null ? null : (objectValue.RuntimeValue, member);
    }

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

    // What the expression an object is reached through is declared as is what decides which of its interfaces a member
    // is a member of: the value carries the object, and nothing of how it was declared. So the declaration is asked of
    // the same rules that type the expression at compile time. It is asked only of an object whose class implements an
    // interface it declares, since for any other the answer would be its own class.
    private static VBTypeMemberSymbol? ImplementationThroughDeclaredInterface(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode? ownerExpression, VBClassModuleSymbol actual, string memberName)
    {
        if (DeclaredInterfaceOf(session, context, ownerExpression, actual) is not { } implementedInterface)
        {
            return null;
        }

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
    /// Finds the member <paramref name="memberName"/> of <paramref name="owner"/> that an assignment writes to
    /// (<strong>MS-VBAL §5.4.3.8</strong>, <strong>§5.4.3.9</strong>).
    /// </summary>
    /// <remarks>
    /// A public variable is the storage it names, and a property is assigned by its <c>Property Let</c>, or by its
    /// <c>Property Set</c> in a <c>Set</c> assignment. Through a declared interface (<strong>§5.3.1.9</strong>) the
    /// accessor is the one the object's class implements it with, and a public variable of the interface, which the
    /// class implements with properties, is assigned through the property that implements it.
    /// </remarks>
    /// <param name="session">The session the objects live in.</param>
    /// <param name="context">The scope of the assignment.</param>
    /// <param name="ownerExpression">The expression the object is reached through, which says how it is declared.</param>
    /// <param name="owner">The object.</param>
    /// <param name="memberName">The member's name.</param>
    /// <param name="isSet">Whether it is a <c>Set</c> assignment.</param>
    /// <returns>The member, or <see langword="null"/> when the object has none that can be assigned.</returns>
    public AssignableMember? ResolveAssignableMember(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode? ownerExpression, VBObjectValue owner, string memberName, bool isSet)
    {
        if (owner.IsNothing() || !session.Symbols.TryGetInstance(owner.Value, out var instance))
        {
            return null;
        }

        var actual = instance.ClassModule;
        var access = isSet ? ImplementationAccess.Set : ImplementationAccess.Let;
        var receiver = owner.RuntimeValue;

        if (DeclaredInterfaceOf(session, context, ownerExpression, actual) is { } implementedInterface)
        {
            var interfaceMembers = implementedInterface.Members
                .Where(candidate => string.Equals(candidate.Name, memberName, StringComparison.OrdinalIgnoreCase)).ToArray();
            var interfaceMember = interfaceMembers.FirstOrDefault(candidate => candidate.Kind == SymbolKindExt.Field)
                ?? interfaceMembers.FirstOrDefault(candidate => isSet ? candidate is VBPropertySetMemberSymbol : candidate is VBPropertyLetMemberSymbol);

            if (interfaceMember is not null && actual.FindImplementation(implementedInterface, interfaceMember, access) is { } implementation)
            {
                return new AssignableMember(null, null, implementation, receiver);
            }
        }

        var candidates = actual.DefaultInterfaceMembers
            .Where(candidate => string.Equals(candidate.Name, memberName, StringComparison.OrdinalIgnoreCase)).ToArray();

        if (candidates.FirstOrDefault(candidate => isSet ? candidate is VBPropertySetMemberSymbol : candidate is VBPropertyLetMemberSymbol) is { } accessor)
        {
            return new AssignableMember(null, null, accessor, receiver);
        }

        return candidates.FirstOrDefault(candidate => candidate.Kind == SymbolKindExt.Field) is { } field
            ? new AssignableMember(field, instance, null, null)
            : null;
    }

    /// <summary>
    /// Invokes the <c>Property Let</c> or <c>Property Set</c> an assignment to a property is, with the index arguments written
    /// after the property's name and the value assigned as the last argument (<strong>MS-VBAL §5.3.1.7</strong>).
    /// </summary>
    /// <param name="session">The session the call runs in.</param>
    /// <param name="context">The scope the arguments are written in.</param>
    /// <param name="accessor">The accessor.</param>
    /// <param name="receiver">The object it is invoked on.</param>
    /// <param name="indexArguments">The arguments written between the property's name and the assignment.</param>
    /// <param name="value">The value assigned, which is Let- or Set-coerced to the type of the accessor's value parameter.</param>
    /// <param name="source">The expression the value came from, for the location of an error.</param>
    /// <param name="isSet">Whether it is a <c>Set</c> assignment.</param>
    public RuntimeSemanticsEvaluationResult InvokeAssignment(
        IRuntimeSession session, RuntimeEvaluationContext context, VBTypeMemberSymbol accessor, IRuntimeValue receiver,
        ImmutableArray<ExpressionNode> indexArguments, VBTypedValue value, ExpressionNode source, bool isSet)
    {
        var parameters = RuntimeProcedureInvoker.GetParameters(accessor);
        parameters = parameters is [{ Name: "Me" }, ..] ? parameters.RemoveAt(0) : parameters;
        if (ProcedureInvoker is null || LetCoercionProvider is null || parameters.IsEmpty)
        {
            // static semantics rejects a Property Let or Set with no value parameter (VBC09321).
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        // the value parameter is the last one, and the ones before it are what the property is indexed by.
        var valueParameter = parameters[^1];
        if (BindArguments(session, context, parameters.RemoveAt(parameters.Length - 1), indexArguments, out var arguments) is { } bindingError)
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
                new LetCoercionStackFrame(source.Identity, InputIndex.CoercionSourceValue, value, new VBTypeDescValue(valueParameter.ResolvedType)));
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

        IRuntimeValue[] callArguments = [receiver, .. arguments, coerced.RuntimeValue];
        return Bindings is { } bindings
            ? bindings.ForMember(accessor).Call(session.Symbols.Resolver, callArguments)
            : ProcedureInvoker.Invoke(accessor, session.Symbols.Resolver, callArguments);
    }

    // A late-bound Variant/Object member, and a call through a Property/Function/Sub member, are both
    // invocations, not reads - only a Field/Variable-kind member is readable this way.
    private static RuntimeSemanticsEvaluationResult EvaluateInstanceField(IRuntimeSession session, VBTypedValue owner, string memberName)
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

        return member is { Kind: SymbolKindExt.Field or SymbolKindExt.Variable }
            ? RuntimeSemanticsEvaluationResult.Success(member.ResolvedType.CreateValue(instance.GetValue(member)))
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
        if (TryResolveCallableSub(session, context, indexExpression.Callee) is { } sub)
        {
            return InvokeProcedure(session, context, sub, indexExpression.Arguments);
        }

        // a call written on an object - Err.Raise 5, obj.Item(1) - invokes the member the object's class has by that
        // name. The owner is evaluated once, here, because evaluating it again to read the member as an array would
        // repeat whatever it does.
        RuntimeSemanticsEvaluationResult calleeResult;
        if (indexExpression.Callee is MemberAccessExpressionNode { Owner: { } namespaceExpression } namespaced
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

            if (ObjectNotSet(qualified, ownerResult.Result!) is { } notSet)
            {
                return notSet;
            }

            if (TryResolveInvocableMember(session, context, qualified.Owner, ownerResult.Result!, qualified.Member.IdentifierName) is { } found)
            {
                return InvokeProcedure(session, context, found.Member, indexExpression.Arguments, found.Receiver);
            }

            calleeResult = EvaluateInstanceField(session, ownerResult.Result!, qualified.Member.IdentifierName);
        }
        else
        {
            calleeResult = Evaluate(session, indexExpression.Callee, context);
        }

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

        // an object that is indexed is a call of its default member: `c(1)` is `c.Item(1)`.
        if (calleeValue is VBObjectValue indexed)
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

        if (calleeValue is not VBArrayValue array)
        {
            // any other Callee shape is a function/property call, not an element read.
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        if (EvaluateSubscripts(session, context, indexExpression.Arguments, out var subscripts) is { } subscriptFailure)
        {
            return subscriptFailure;
        }

        var element = array[subscripts];
        return element is not null
            ? RuntimeSemanticsEvaluationResult.Success(element)
            : RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.SubscriptOutOfRange, expression.Location,
                string.Join(", ", subscripts)));
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
    {
        subscripts = [];
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

        var result = session.Symbols.Resolver.ResolveValue(simpleName.IdentifierName, ScopeKind.Local, context.Scope);
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

        // a call on an object - Err.Raise 5 - is the same call with the same arguments, written without parentheses.
        return callee is MemberAccessExpressionNode
            ? Evaluate(session, new IndexExpressionNode(callee.Identity, callee.Location, callee, argumentNodes), context)
            : RuntimeSemanticsEvaluationResult.InternalError();
    }

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

        if (BindArguments(session, context, parameters, argumentNodes, out var arguments) is { } bindingError)
        {
            return bindingError;
        }

        IRuntimeValue[] callArguments = receiver is null ? arguments : [receiver, .. arguments];

        // through a binding rather than straight to the invoker: whether this member's code is the workspace's
        // is the factory's decision, and a call site has no business knowing.
        return Bindings is { } bindings
            ? bindings.ForMember(procedure).Call(session.Symbols.Resolver, callArguments)
            : ProcedureInvoker.Invoke(procedure, session.Symbols.Resolver, callArguments);
    }

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
    /// 🚧 TODO a <c>ByRef</c> parameter whose argument is not a variable is a fresh local for each handler, so the
    /// value one leaves in it is not the next one's argument.
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

        if (BindArguments(session, context, raised.Parameters, argumentNodes, out var arguments) is { } bindingError)
        {
            return bindingError;
        }

        // MS-VBAL §5.4.2.20: the next invocation's argument for a ByRef parameter is what the parameter last contained.
        // An argument that names a variable is that variable; any other has nowhere to be left a value in, so it is
        // given a location of its own for the handlers of this one event.
        var temporaries = new List<MemoryAddress>();
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
    /// <returns>The error that stopped the binding, or <see langword="null"/> when every argument was bound.</returns>
    private RuntimeSemanticsEvaluationResult? BindArguments(
        IRuntimeSession session, RuntimeEvaluationContext context, ImmutableArray<VBParameterSymbol> parameters,
        ImmutableArray<ExpressionNode> argumentNodes, out IRuntimeValue[] arguments)
    {
        arguments = [];
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
                // expression to Let-coerce or alias, so just the parameter's own default. That default is
                // a constant expression (MS-VBAL 5.3.1.5), reduced by the same fold a Const's own is and
                // therefore reduced once however many times the procedure is called without it.
                if (parameter.DefaultValue is { } declaredDefault)
                {
                    var foldResult = FoldConstant(session, new RuntimeEvaluationContext(parameter.ParentUri), parameter, declaredDefault);
                    if (!foldResult.IsSuccess)
                    {
                        return foldResult;
                    }

                    bound[i] = foldResult.Result!.RuntimeValue;
                    continue;
                }

                bound[i] = parameter.ResolvedType.DefaultValue.RuntimeValue;
                continue;
            }

            if (RuntimeProcedureInvoker.IsByRef(parameter.ParameterKind)
                && TryResolveByRefArgument(session, context, argumentNode, parameter, out var reference))
            {
                bound[i] = reference;
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

            // an object passed to a parameter declared as a class or as Object is Set-assigned to the parameter's new
            // local (MS-VBAL §5.3.1.11): the parameter holds the reference, and nothing asks the object for a value. So is
            // one passed to a Variant, which holds the object itself: "if the value type of the argument is a specific class
            // or Nothing, its data value is Set-assigned" to the local - not what the object's default member returns.
            if (SetCoercion is { } setCoercion
                && argumentResult.Value.Result is VBObjectValue
                && parameter.ResolvedType is VBClassType or VBObjectType or VBVariantType)
            {
                var setResult = setCoercion.EvaluateSetCoercion(session, argumentNode, argumentResult.Value.Result!, parameter.ResolvedType);
                if (!setResult.IsSuccess)
                {
                    return RuntimeSemanticsEvaluationResult.Error(setResult.ErrorInfo!);
                }

                bound[i] = setResult.Result!.RuntimeValue;
                continue;
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
        var array = new VBFixedSizeArrayValue(argumentNodes.IsEmpty ? [] : [(0, argumentNodes.Length - 1)]);
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

            var coercionFrame = new LetCoercionStackFrame(argumentNodes[i].Identity, InputIndex.CoercionSourceValue,
                argumentResult.Value.Result!, new VBTypeDescValue(VBVariantType.TypeInfo));
            var coercionResult = LetCoercionProvider!.EvaluateLetCoercionSemantics(session.Symbols.Resolver, argumentNodes[i], coercionFrame);
            if (!coercionResult.IsApplicable)
            {
                return new ParamArrayCollectResult(null, RuntimeSemanticsEvaluationResult.InternalError());
            }
            if (!coercionResult.IsSuccess)
            {
                return new ParamArrayCollectResult(null, RuntimeSemanticsEvaluationResult.Error(coercionResult.ErrorInfo!));
            }

            array.TrySetElement(new ValueBindingHandle(coercionResult.Result!.RuntimeValue), i);
        }

        return new ParamArrayCollectResult(new VBRuntimeValue<VBRuntimeArrayValue>(new VBRuntimeArrayValue(array)), null);
    }

    private readonly record struct ArgumentMapResult(ExpressionNode?[]? Mapped, ImmutableArray<ExpressionNode> ParamArrayArguments, RuntimeSemanticsEvaluationResult? Error);

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
    // either matches the parameter's own exactly or is Variant (the two shapes the spec allows a plain
    // reference binding for without a class/Object copy-back dance, which isn't modeled yet - an
    // Object-typed ByRef parameter falls through to a ByVal-style copy today, a documented, narrower-
    // than-spec gap rather than a wrong result). Anything else (an expression, a literal, a mismatched-
    // type argument, a read-only target) falls through to the same Let-coerced copy every ByVal argument
    // already gets - MS-VBAL's own "otherwise" case, never an error.
    private bool TryResolveByRefArgument(IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode argument, VBParameterSymbol parameter, out VBRuntimeReference reference)
    {
        reference = VBRuntimeReference.NullRef;
        if (argument is MemberAccessExpressionNode memberAccess)
        {
            return TryResolveFieldByRefArgument(session, context, memberAccess, parameter, out reference);
        }

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
            || ResolveAssignableMember(session, context, memberAccess.Owner, owner, memberAccess.Member.IdentifierName, isSet: false)
                is not { Field: ITypedSymbol { ResolvedType: { } fieldType } field, Instance: { } instance })
        {
            return false;
        }

        if (!parameter.ResolvedType.Equals(fieldType) && parameter.ResolvedType is not VBVariantType)
        {
            return false;
        }

        if (!instance.TryGetAddress((Symbol)field, out var address)
            || !instance.GetValue((Symbol)field).BindingCapabilities.HasFlag(BindingCapabilities.SetValue))
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
