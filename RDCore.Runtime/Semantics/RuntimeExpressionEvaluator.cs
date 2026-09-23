using RDCore.Runtime.Semantics.Expressions;
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
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Static;

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
/// <c>Function</c>/<c>Property Get</c>/<c>Sub</c>, a call through <c>Index</c>/<c>MemberAccess</c> whose
/// target isn't a plain array/field, <c>DictionaryAccess</c> (<strong>MS-VBAL §5.6.14</strong>'s sugar
/// for a call through the owner's default member), and <c>AddressOf</c> (which must never be evaluated
/// as a value read at all — its whole point is to reference a procedure, not call it) all return
/// <see cref="RuntimeSemanticsEvaluationResult.InternalError"/> here rather than being misread as values.
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

    private static RuntimeSemanticsEvaluationResult EvaluateSimpleName(IRuntimeSession session, RuntimeEvaluationContext context, SimpleNameExpressionNode simpleName)
    {
        var result = session.Symbols.Resolver.ResolveValue(simpleName.IdentifierName, ScopeKind.Local, context.Scope);

        if (result.Symbol is VBReturningMemberSymbol or VBProcedureMemberSymbol)
        {
            // a bare reference to a Function/Property Get is an implicit call (MS-VBAL §5.6.10); a bare
            // Sub/Function name is also what AddressOf's Target names. Neither is a value to read, and
            // no callable symbol ever has storage allocated for it, so GetValue below would throw
            // KeyNotFoundException instead of failing cleanly.
            return RuntimeSemanticsEvaluationResult.InternalError();
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
