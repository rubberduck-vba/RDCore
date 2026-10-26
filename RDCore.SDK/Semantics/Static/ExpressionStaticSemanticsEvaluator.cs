using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Runtime.Abstract.Execution;
﻿using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Static.Abstract;
using RDCore.SDK.Semantics.Static.Expressions;
using RDCore.SDK.Semantics.Static.Operators;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// Recursively determines the declared type of a real, arbitrarily-nested expression tree.
/// </summary>
/// <remarks>
/// Every individual <see cref="IStaticSemantics"/> rule (<see cref="SimpleNameExpressionStaticSemantics"/>,
/// <see cref="MemberAccessExpressionStaticSemantics"/>, operators, ...) only knows how to combine
/// already-resolved operand types for the one node it handles — nothing recurses into an expression's
/// own children first to produce them. This is that missing piece: given any <see cref="ExpressionNode"/>,
/// it dispatches by the node's own C# type (and, for operator nodes, by <c>Token</c>) to the matching
/// rule, evaluating children first and short-circuiting on the first error, so a nested expression like
/// <c>Foo.Bar.Baz</c> or <c>x + 1</c> resolves end to end instead of only ever being unit-tested with
/// hand-fed operand types.
/// </remarks>
public static class ExpressionStaticSemanticsEvaluator
{
    /// <summary>
    /// Determines the declared type of <paramref name="expression"/>, recursively evaluating its
    /// children first wherever a rule needs their declared types as operands.
    /// </summary>
    /// <param name="context">The compile-time context this expression is evaluated against.</param>
    /// <param name="expression">The expression to evaluate — may be arbitrarily nested.</param>
    /// <returns>
    /// The result of the first rule (own or a child's) that fails, or the final resolved type when the
    /// whole tree evaluates successfully. A node kind with no rule yet (or an operator token with no
    /// mapped rule, e.g. <c>Mod</c>) defers to <see cref="VBUnknownType"/> rather than erroring — the
    /// same "not modeled yet, not wrong" convention every existing rule already uses for its own gaps.
    /// </returns>
    public static StaticSemanticsEvaluationResult Evaluate(StaticEvaluationContext context, ExpressionNode expression)
    {
        var result = EvaluateCore(context, expression);
        if (context.Facts is { } facts)
        {
            // children are evaluated first, so what is known of an operand is known when the expression that has it is described.
            facts.Record(ExpressionFactDescriber.Describe(context, facts, expression, result));
        }

        // so are its bindings, so that the plan of an operation is made for the declared types of its operands.
        context.Bindings?.Record(context, expression, result);

        return result;
    }

    private static StaticSemanticsEvaluationResult EvaluateCore(StaticEvaluationContext context, ExpressionNode expression)
        => expression switch
        {
            LiteralExpressionNode => LiteralExpressionStaticSemantics.Instance.DetermineDeclaredType(context, expression),
            SimpleNameExpressionNode => SimpleNameExpressionStaticSemantics.Instance.DetermineDeclaredType(context, expression),
            InstanceExpressionNode => InstanceExpressionStaticSemantics.Instance.DetermineDeclaredType(context, expression),
            // TypeExpression names a type, not a value - nothing to recurse into as an expression.
            NewExpressionNode => NewExpressionStaticSemantics.Instance.DetermineDeclaredType(context, expression),
            MemberAccessExpressionNode memberAccess => EvaluateMemberAccess(context, expression, memberAccess),
            IndexExpressionNode indexExpression => EvaluateIndexExpression(context, expression, indexExpression),
            DictionaryAccessExpressionNode dictionaryAccess => EvaluateDictionaryAccess(context, expression, dictionaryAccess),
            // TypeExpression names a type, not a value - nothing to recurse into as an expression.
            TypeOfIsExpressionNode typeOfIs => EvaluateTypeOfIs(context, expression, typeOfIs),
            ArrayBoundExpressionNode arrayBound => EvaluateArrayBound(context, arrayBound),
            ArrayExpressionNode array => EvaluateArray(context, array),
            VBBinaryOperatorExpressionNode binaryOperator => EvaluateBinaryOperator(context, expression, binaryOperator),
            VBUnaryOperatorExpressionNode unaryOperator => EvaluateUnaryOperator(context, expression, unaryOperator),
            // ByVal flags how an argument is passed; the argument is the expression it is written before.
            ByValArgumentExpressionNode byVal => Evaluate(context, byVal.Operand),
            AddressOfExpressionNode addressOf => EvaluateAddressOf(context, addressOf),
            // what is printed is an expression like any other, which can be wrong in its own right; an item of an output list has no type of its own.
            PrintOutputItemNode item => EvaluatePrintItem(context, item),
            PrintSpcClauseNode spc => EvaluateOperands(context, spc.Count),
            PrintTabClauseNode tab => EvaluateOperands(context, tab.Column),
            ObjectPrintExpressionNode print => EvaluateOperands(context, [print.Owner, .. print.Items]),
            // MS-VBAL §3.3.5.3: an intrinsic type is no value, wherever an expression is written with one (MS-VBA: "Syntax error", Excel-verified).
            BuiltInTypeExpressionNode builtInType => StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.SyntaxError, builtInType.Location,
                string.Format(Exceptions.VBCompileError_BuiltInTypeIsNoValue_Verbose, builtInType.TypeName))),
            _ => StaticSemanticsEvaluationResult.Success(VBUnknownType.TypeInfo),
        };

    // an item of an output list is printed, which a subroutine has no value to be (MS-VBAL §5.6.2.1; MS-VBA: "Expected Function or variable",
    // Excel-verified).
    private static StaticSemanticsEvaluationResult EvaluatePrintItem(StaticEvaluationContext context, PrintOutputItemNode item)
    {
        if (item.Value is { } value && Evaluate(context, value) is { IsSuccess: true, Result: VBVoidType })
        {
            return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.ExpectedFunctionOrVariable, value.Location,
                value switch { SimpleNameExpressionNode name => name.IdentifierName, MemberAccessExpressionNode access => access.Member.IdentifierName, _ => string.Empty }));
        }

        return EvaluateOperands(context, item.Value);
    }

    // the operands of an expression that has no declared type of its own: the first of them that is an error, or nothing known of the expression itself.
    private static StaticSemanticsEvaluationResult EvaluateOperands(StaticEvaluationContext context, params ExpressionNode?[] operands)
    {
        foreach (var operand in operands.OfType<ExpressionNode>())
        {
            var result = Evaluate(context, operand);
            if (result.IsError)
            {
                return result;
            }
        }

        return StaticSemanticsEvaluationResult.Success(VBUnknownType.TypeInfo);
    }

    private static StaticSemanticsEvaluationResult EvaluateMemberAccess(
        StaticEvaluationContext context, ExpressionNode expression, MemberAccessExpressionNode memberAccess)
    {
        // MS-VBAL §5.6.12: an owner that names a project or a procedural module (`Strings.LenB`, `VBA.LenB`) is a
        // namespace, not a value: it has no declared type to look the member up in, and the member is resolved in
        // the namespace instead.
        if (memberAccess.Owner is { } namespaceExpression
            && context.Resolver.NamespaceOf(namespaceExpression, context.Scope.Uri) is { } qualifier)
        {
            return EvaluateNamespaceMember(context, expression, qualifier, memberAccess);
        }

        VBType ownerType;
        if (memberAccess.Owner is { } enumeration && NameClassificationStaticSemantics.EnumerationOf(context, enumeration) is { } enumerationType)
        {
            // an enumeration qualifies the names of its members (`Color.Red`, `Globals.Color.Red`): as the owner of one it is the type, which by itself
            // it is no value of.
            ownerType = enumerationType;
        }
        else if (memberAccess.Owner is { } owner)
        {
            var ownerResult = Evaluate(context, owner);
            if (ownerResult.IsError)
            {
                return ownerResult;
            }
            ownerType = ownerResult.Result!;
        }
        else if (context.EnclosingWithTargetType is { } withTargetType)
        {
            // a With-relative access (.Member) resolves against the innermost enclosing With block's
            // target type (MS-VBAL 5.6.15) - only known here when a statement walker threaded it in.
            ownerType = withTargetType;
        }
        else
        {
            // MS-VBAL 5.6.15: "If there is no enclosing With block, the with-expression is invalid."
            return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.WithExpressionOutsideWithBlock,
                expression.Location, $"'.{memberAccess.Member.IdentifierName}' has no enclosing With block."));
        }

        return MemberAccessExpressionStaticSemantics.Instance.DetermineDeclaredType(context, expression, ownerType);
    }

    // MS-VBAL §5.6.12, an <l-expression> classified as a project or a procedural module: the member access is the
    // member it resolves to, with that member's own declared type - a variable, property or function's, a value's, or
    // none for a subroutine. A name the namespace does not have is as invalid as a member a value's type does not.
    private static StaticSemanticsEvaluationResult EvaluateNamespaceMember(
        StaticEvaluationContext context, ExpressionNode expression, Symbol qualifier, MemberAccessExpressionNode memberAccess)
    {
        var memberName = memberAccess.Member.IdentifierName;
        var resolved = context.Resolver.ResolveMember(qualifier, memberName, context.Scope.Uri);

        if (resolved.IsError)
        {
            return StaticSemanticsEvaluationResult.Error(
                SimpleNameExpressionStaticSemantics.GetResolutionErrorInfo(expression, memberName, resolved.ErrorId!.Value, resolved.Candidates));
        }

        if (resolved.Symbol is not { } member)
        {
            return StaticSemanticsEvaluationResult.Error(
                VBCompileErrorInfo.For(VBCompileErrorId.MethodOrDataMemberNotFound, expression.Location, memberName));
        }

        // a module, a project or an enumeration a namespace declares is no value, qualified as it is not bare (`VBA.Strings`, `Globals.Color`). One that is
        // only the owner of another member access never gets here.
        if (NameClassificationStaticSemantics.NotAValueError(member, expression.Location, memberName) is { } notAValue)
        {
            return StaticSemanticsEvaluationResult.Error(notAValue);
        }

        return StaticSemanticsEvaluationResult.Success(
            member is ITypedSymbol typed && !NamespaceExpressions.IsNamespace(member) ? typed.ResolvedType : VBUnknownType.TypeInfo);
    }

    private static StaticSemanticsEvaluationResult EvaluateIndexExpression(
        StaticEvaluationContext context, ExpressionNode expression, IndexExpressionNode indexExpression)
    {
        var calleeResult = Evaluate(context, indexExpression.Callee);
        if (calleeResult.IsError)
        {
            return calleeResult;
        }

        // a function whose name is reserved is written with the arguments it takes, or not at all (MS-VBA: "Syntax error", Excel-verified).
        if (ReservedNameStaticSemantics.CallError(indexExpression) is { } reservedNameError)
        {
            return StaticSemanticsEvaluationResult.Error(reservedNameError);
        }

        // an argument written ByVal is valid where MS-VBA compiles it, and not only before an argument of an external procedure as MS-VBAL §5.6.13.1 says
        // (RD-VBAL §5.6.13.1): only the parameter it is written for says which.
        if (ByValArgumentStaticSemantics.Error(context, indexExpression, calleeResult.Result) is { } byValError)
        {
            return StaticSemanticsEvaluationResult.Error(byValError);
        }

        foreach (var argument in indexExpression.Arguments)
        {
            var argumentResult = EvaluateIndexArgument(context, argument);
            if (argumentResult is { IsError: true })
            {
                return ObjectValueStaticSemantics.NotAValueArgumentError(context, indexExpression.Callee, indexExpression.Arguments, argument,
                    argumentResult.Value.ErrorInfo as VBCompileErrorInfo) is { } variableRequired
                    ? StaticSemanticsEvaluationResult.Error(variableRequired)
                    : argumentResult.Value;
            }

            // a subscript of an element is let-coerced to a Long (RD-VBAL §5.6.13), which MS-VBA refuses a LongLong to at compile time, whatever its value
            // (Excel-verified): not for a call, whose parameters say what they take.
            if (argumentResult is { Result: VBLongLongType } && calleeResult.Result is VBArrayType && ProcedureNamedBy(context, indexExpression.Callee) is null)
            {
                return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, argument.Location,
                    Exceptions.VBIndex_LongLongSubscript_Verbose));
            }
        }

        // an index of an array is no argument: any other callee may take what is written in its argument list by reference.
        if (calleeResult.Result is not VBArrayType)
        {
            MarkPassedAsArguments(context, indexExpression.Arguments);
        }

        ByValArgumentStaticSemantics.MarkRedundant(context, indexExpression, calleeResult.Result);

        // a ParamArray is no variable a ByRef parameter can be bound to (MS-VBAL §5.3.1.11), and an object given to a parameter that expects a value of it
        // must have one (MS-VBAL §5.5.1.1).
        if (calleeResult.Result is not VBArrayType && ObjectValueStaticSemantics.MemberOfCallee(context, indexExpression.Callee) is { } calledMember
            && ObjectValueStaticSemantics.ParametersOfCallee(context, indexExpression.Callee) is { } parameters
            && (ArgumentListStaticSemantics.Error(context, indexExpression, calledMember, parameters, indexExpression.Arguments)
                ?? ParamArrayStaticSemantics.ArgumentsError(context, calledMember, parameters, indexExpression.Arguments)
                ?? ObjectValueStaticSemantics.ArgumentsError(context, parameters, indexExpression.Arguments)) is { } argumentError)
        {
            return StaticSemanticsEvaluationResult.Error(argumentError);
        }

        // MS-VBAL §5.6.13: the arguments are those of a call when the callee is a procedure, and the result of the call has the type the procedure
        // returns - however that type is shaped: `Whole()` of a function that returns Long() is no element of an array. A procedure that declares no
        // parameters is not given the arguments written after it: it is called, and they index what it returns.
        if (ProcedureNamedBy(context, indexExpression.Callee) is { } procedure)
        {
            var indexesResult = procedure.Parameters.All(parameter => parameter.Name == "Me")
                && indexExpression.Arguments is not ([] or [MissingArgumentNode]);
            return indexesResult
                ? IndexExpressionStaticSemantics.Instance.DetermineDeclaredType(context, expression, calleeResult.Result!)
                : calleeResult;
        }

        // a variable of an intrinsic type other than Variant has no element (MS-VBA: "Expected array"), and an array declared with fixed dimensions has an
        // element for as many subscripts as it has dimensions (MS-VBA: "Wrong number of dimensions") - Excel-verified.
        if (indexExpression.Callee is SimpleNameExpressionNode name
            && context.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope.Uri).Symbol is { } variable and not (VBReturningMemberSymbol or VBProcedureMemberSymbol))
        {
            if (calleeResult.Result is VBNumericType or VBStringType or VBBooleanType or VBDateType)
            {
                return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.ExpectedArray, indexExpression.Callee.Location, name.IdentifierName));
            }

            if (variable.TryGetProperty(SymbolProperties.ArrayBounds, out var bounds) && !bounds.IsDefaultOrEmpty
                && indexExpression.Arguments is not ([] or [MissingArgumentNode]) && indexExpression.Arguments.Length != bounds.Length)
            {
                return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.WrongNumberOfDimensions, indexExpression.Location, name.IdentifierName));
            }
        }

        return IndexExpressionStaticSemantics.Instance.DetermineDeclaredType(context, expression, calleeResult.Result!);
    }

    /// <summary>
    /// Flags the arguments of a call that may be taken by reference (<see cref="ValueExpressionSemanticFlags.PassedAsArgument"/>): not one written with
    /// <c>ByVal</c>, which is a value bound to nothing, nor one that is not a value (the address of a procedure).
    /// </summary>
    internal static void MarkPassedAsArguments(StaticEvaluationContext context, IEnumerable<ExpressionNode> arguments)
    {
        if (context.Facts is not { } facts)
        {
            return;
        }

        foreach (var argument in arguments)
        {
            var passed = argument is NamedArgumentNode named ? named.Value : argument;
            if (passed is ByValArgumentExpressionNode or AddressOfExpressionNode or MissingArgumentNode)
            {
                continue;
            }

            if (facts.TryGet(passed.Identity, out var fact))
            {
                facts.Record(fact with { Flags = fact.Flags | ValueExpressionSemanticFlags.PassedAsArgument });
            }
        }
    }

    // the Function or Property Get a callee names, by its bare name, qualified by the project or module that declares it, or as a member of an object
    // of a class; null for anything that is not one (an array, a variable that holds an object, a Sub).
    internal static VBReturningMemberSymbol? ProcedureNamedBy(StaticEvaluationContext context, ExpressionNode callee)
    {
        Symbol? symbol = callee switch
        {
            SimpleNameExpressionNode name => context.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope.Uri).Symbol,
            MemberAccessExpressionNode { Owner: { } owner } access when context.Resolver.NamespaceOf(owner, context.Scope.Uri) is { } qualifier
                => context.Resolver.ResolveMember(qualifier, access.Member, context.Scope.Uri).Symbol,
            // the class as the composition has it now: a type a class's own member returns (`Function Append() As SB` of SB) was built before the class had
            // its members, and a call on what such a member returns (`SB.Append("a").Append("b")`) is a call all the same.
            MemberAccessExpressionNode { Owner: { } owner } access when Evaluate(context, owner) is { IsSuccess: true, Result: VBClassType classType }
                => ReturningMemberOf(context, classType, access),
            // a member of the innermost With block's target (MS-VBAL §5.6.15): `With .LabelFor("x")` calls LabelFor, and does not index what it returns.
            MemberAccessExpressionNode { Owner: null } access when context.EnclosingWithTargetType is VBClassType withTarget
                => ReturningMemberOf(context, withTarget, access),
            _ => null,
        };

        return symbol as VBReturningMemberSymbol;
    }

    private static Symbol? ReturningMemberOf(StaticEvaluationContext context, VBClassType owner, MemberAccessExpressionNode access)
        => Expressions.MemberAccessExpressionStaticSemantics.CurrentMembersOf(context, owner)
            .FirstOrDefault(member => string.Equals(member.Name, access.Member.IdentifierName, StringComparison.OrdinalIgnoreCase)
                && member is VBFunctionMemberSymbol or VBPropertyGetMemberSymbol);

    // MissingArgumentNode is a placeholder, not a value - nothing to evaluate. NamedArgumentNode and
    // AddressOfExpressionNode wrap the expression actually worth checking for errors; neither has a
    // declared type IndexExpressionStaticSemantics needs, so only the callee's type feeds it.
    private static StaticSemanticsEvaluationResult? EvaluateIndexArgument(StaticEvaluationContext context, ExpressionNode argument)
        => argument switch
        {
            MissingArgumentNode => null,
            NamedArgumentNode named => EvaluateIndexArgument(context, named.Value),
            ByValArgumentExpressionNode byVal => Evaluate(context, byVal.Operand),
            _ => Evaluate(context, argument),
        };

    // MS-VBAL §5.6.16.8: what AddressOf names is a subroutine, a function or a property with a Property Get (MS-VBA: "Expected Sub, Function, or Property"),
    // and not one of a class module (MS-VBA: "Invalid use of AddressOf operator", of an object's member too) - both Excel-verified. Its declared type is
    // implementation-defined: MS-VBA's is the LongPtr of the environment, as a LongLong is refused by a Long parameter in 64-bit (Excel-verified).
    private static StaticSemanticsEvaluationResult EvaluateAddressOf(StaticEvaluationContext context, AddressOfExpressionNode addressOf)
    {
        var target = addressOf.Target;
        var referenced = target switch
        {
            SimpleNameExpressionNode name => context.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope.Uri).Symbol,
            MemberAccessExpressionNode { Owner: { } owner } access when context.Resolver.NamespaceOf(owner, context.Scope.Uri) is { } qualifier
                => context.Resolver.ResolveMember(qualifier, access.Member, context.Scope.Uri).Symbol,
            _ => null,
        };

        // a member of what is no namespace - a class module, an object - has no address; an owner that names nothing is that name's error.
        if (referenced is null && target is MemberAccessExpressionNode { Owner: { } other } member && context.Resolver.NamespaceOf(other, context.Scope.Uri) is null
            && (other is not SimpleNameExpressionNode ownerName || context.Resolver.ResolveValue(ownerName, ScopeKind.Local, context.Scope.Uri).Symbol is not null
                || context.Resolver.ResolveQualifier(ownerName.IdentifierName, ScopeKind.Global, context.Scope.Uri).Symbol is VBClassModuleSymbol))
        {
            return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.InvalidUseOfAddressOf, target.Location, member.Member.IdentifierName));
        }

        return referenced switch
        {
            // what MS-VBA makes of the address of a Declare is not known.
            VBExternalFunctionMemberSymbol or VBExternalSubMemberSymbol => Evaluate(context, target),
            VBProcedureMemberSymbol or VBFunctionMemberSymbol or VBPropertyGetMemberSymbol
                => StaticSemanticsEvaluationResult.Success(context.Environment?.PointerType ?? VBUnknownType.TypeInfo),
            ITypedSymbol
                =>StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.ExpectedSubFunctionOrProperty, target.Location,
                    target switch { SimpleNameExpressionNode simple => simple.IdentifierName, MemberAccessExpressionNode access => access.Member.IdentifierName, _ => string.Empty })),
            _ => Evaluate(context, target),
        };
    }

    private static StaticSemanticsEvaluationResult EvaluateDictionaryAccess(
        StaticEvaluationContext context, ExpressionNode expression, DictionaryAccessExpressionNode dictionaryAccess)
    {
        VBType ownerType;
        if (dictionaryAccess.Owner is { } owner)
        {
            var ownerResult = Evaluate(context, owner);
            if (ownerResult.IsError)
            {
                return ownerResult;
            }
            ownerType = ownerResult.Result!;
        }
        else if (context.EnclosingWithTargetType is { } withTargetType)
        {
            // a With-relative access (!member) resolves against the innermost enclosing With block's
            // target type (MS-VBAL 5.6.15) - only known here when a statement walker threaded it in.
            ownerType = withTargetType;
        }
        else
        {
            // MS-VBAL 5.6.15: "If there is no enclosing With block, the with-expression is invalid."
            return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.WithExpressionOutsideWithBlock,
                expression.Location, $"'!{dictionaryAccess.Member.IdentifierName}' has no enclosing With block."));
        }

        return DictionaryAccessExpressionStaticSemantics.Instance.DetermineDeclaredType(context, expression, ownerType);
    }

    // MS-VBAL 3.3.5.2: LBound and UBound yield a Long whatever the array, so the result type needs nothing of the
    // operands - but an operand is an expression like any other and can be wrong in its own right, which is the
    // first rule to fail here.
    // 🚧 TODO the array operand's declared type must be an array, or a Variant or Object that may hold one: MS-VBA
    // refuses anything else when it compiles the expression. Nothing says that yet, so a non-array operand is only
    // found out when the expression runs.
    private static StaticSemanticsEvaluationResult EvaluateArrayBound(StaticEvaluationContext context, ArrayBoundExpressionNode arrayBound)
    {
        foreach (var operand in arrayBound.Inputs.OfType<ExpressionNode>())
        {
            // LBound and UBound are parsed with their operands, which are never written ByVal (MS-VBA: "Syntax error", recorded in Excel).
            if (operand is ByValArgumentExpressionNode byVal)
            {
                return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.SyntaxError, byVal.Location,
                    string.Format(Exceptions.VBArgument_ByValInlineFunction_Verbose, arrayBound.Kind is ArrayBoundKind.Lower ? Tokens.LBound : Tokens.UBound)));
            }

            var isArray = ReferenceEquals(operand, arrayBound.Array);

            // what the bounds are asked of is written as a name, a member or a call: a literal, an operator's result and anything in parentheses are a
            // syntax error (MS-VBA, Excel-verified, oracle\s29 U05, U20-U24, U32).
            if (isArray && operand is LiteralExpressionNode or VBUnaryOperatorExpressionNode or VBBinaryOperatorExpressionNode or TypeOfIsExpressionNode)
            {
                return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.SyntaxError, operand.Location,
                    arrayBound.Kind is ArrayBoundKind.Lower ? Tokens.LBound : Tokens.UBound));
            }

            var result = Evaluate(context, operand);
            if (isArray && NameClassificationStaticSemantics.IsNotAValue(operand, result.ErrorInfo as VBCompileErrorInfo))
            {
                // a project, a module or an enumeration is no array either (MS-VBA: "Expected array", oracle\s29 U26-U28).
                return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.ExpectedArray, operand.Location, result.ErrorInfo!.Verbose));
            }

            if (result.IsError)
            {
                return result;
            }

            // MS-VBAL §3.3.5.2: what the bounds are asked of is an array; one declared as an intrinsic type other than Variant, an object, a class or a
            // user-defined type never holds one (MS-VBA: "Expected array", Excel-verified, oracle\s29 U01-U12, U29-U31). A Variant may, when it runs.
            if (isArray && result.Result is VBNumericType or VBStringType or VBBooleanType or VBDateType or VBObjectType or VBClassType or VBUserDefinedType)
            {
                return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.ExpectedArray, operand.Location,
                    string.Format(Exceptions.VBCompileError_ExpectedArray_Verbose, result.Result.Name)));
            }
        }

        return StaticSemanticsEvaluationResult.Success(VBLongType.TypeInfo);
    }

    // the Array keyword yields a Variant whatever its elements, which are expressions like any other and can be wrong in their own right. Its elements are
    // a ParamArray's, which takes none written ByVal (MS-VBA: "Type mismatch", recorded in Excel; RD-VBAL §5.6.13.1).
    private static StaticSemanticsEvaluationResult EvaluateArray(StaticEvaluationContext context, ArrayExpressionNode array)
    {
        foreach (var element in array.Elements)
        {
            if (element is ByValArgumentExpressionNode byVal)
            {
                return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, byVal.Location,
                    string.Format(Exceptions.VBArgument_ByValParamArray_Verbose, "Array")));
            }

            var result = Evaluate(context, element);
            if (result.IsError)
            {
                return result;
            }
        }

        return StaticSemanticsEvaluationResult.Success(VBVariantType.TypeInfo);
    }

    private static StaticSemanticsEvaluationResult EvaluateTypeOfIs(
        StaticEvaluationContext context, ExpressionNode expression, TypeOfIsExpressionNode typeOfIs)
    {
        // what TypeOf tests is an object, written as it is: MS-VBA refuses one in parentheses, which would be its default member's value (MS-VBA: "Syntax
        // error", Excel-verified).
        if (typeOfIs.Operand is VBUnaryOperatorExpressionNode { Token: OperatorSymbolNames.UnaryArithmeticPrecedenceOp or OperatorSymbolNames.UnaryLetCoerceOp } parenthesized)
        {
            return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.SyntaxError, parenthesized.Location,
                string.Format(Exceptions.VBCompileError_TypeOfParenthesizedOperand_Verbose, Tokens.TypeOf)));
        }

        var operandResult = Evaluate(context, typeOfIs.Operand);
        if (operandResult.IsError)
        {
            return operandResult;
        }

        return TypeOfIsExpressionStaticSemantics.Instance.DetermineDeclaredType(context, expression, operandResult.Result!);
    }

    // a project, a module or an enumeration as the operand of a binary operator is a type mismatch, as MS-VBA has it (Excel-verified, oracle\s29 O01-O18), and
    // not its classification: that is what the operand of a unary operator is.
    private static StaticSemanticsEvaluationResult NoOperand(ExpressionNode operand, StaticSemanticsEvaluationResult result)
        => NameClassificationStaticSemantics.IsNotAValue(operand, result.ErrorInfo as VBCompileErrorInfo)
            ? StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, operand.Location,
                string.Format(Exceptions.VBCompileError_NameIsNoOperand_Verbose, NameClassificationStaticSemantics.NameOf(operand), result.ErrorInfo!.Verbose)))
            : result;

    private static StaticSemanticsEvaluationResult EvaluateBinaryOperator(
        StaticEvaluationContext context, ExpressionNode expression, VBBinaryOperatorExpressionNode binaryOperator)
    {
        var leftResult = Evaluate(context, binaryOperator.Left);
        if (leftResult.IsError)
        {
            return NoOperand(binaryOperator.Left, leftResult);
        }
        var rightResult = Evaluate(context, binaryOperator.Right);
        if (rightResult.IsError)
        {
            return NoOperand(binaryOperator.Right, rightResult);
        }

        // an operand whose type is not known - a member of a library's class that names a class not read yet, a member an extensible class has without declaring
        // it - is not an operand the operator is wrong for: it is late-bound, and the operator is applied to what it turns out to be.
        if (leftResult.Result is VBUnknownType || rightResult.Result is VBUnknownType)
        {
            return StaticSemanticsEvaluationResult.Success(VBUnknownType.TypeInfo);
        }

        // an operand of any operator but Is is a value: an object stands for its default member's (MS-VBAL §5.5.1.1, as MS-VBA compiles it).
        var left = leftResult.Result!;
        var right = rightResult.Result!;
        if (binaryOperator.Token != Tokens.CompareIsOp)
        {
            var leftValue = ObjectValueStaticSemantics.ValueOf(context, binaryOperator.Left, left, ValueDestination.TypedValue);
            if (leftValue.IsError)
            {
                return leftValue;
            }

            var rightValue = ObjectValueStaticSemantics.ValueOf(context, binaryOperator.Right, right, ValueDestination.TypedValue);
            if (rightValue.IsError)
            {
                return rightValue;
            }

            (left, right) = (leftValue.Result!, rightValue.Result!);
        }

        return ResolveBinaryOperatorRule(binaryOperator.Token) is { } rule
            ? rule.DetermineDeclaredType(context, expression, AsOperand(left), AsOperand(right))
            : StaticSemanticsEvaluationResult.Success(VBUnknownType.TypeInfo);
    }

    // MS-VBAL §5.2.3.4: the members of an enumeration are Longs, and an operand declared as the enumeration is a Long to the operator; MS-VBAL §2.2:
    // LongPtr is an alias, of LongLong in a 64-bit environment and of Long in a 32-bit one, and an operand declared LongPtr is what it is an alias of.
    internal static VBType AsOperand(VBType declared) => declared switch
    {
        VBEnumType => VBLongType.TypeInfo,
        VBLongPtrType_x64 => VBLongLongType.TypeInfo,
        VBLongPtrType_x86 => VBLongType.TypeInfo,
        _ => declared,
    };

    private static StaticSemanticsEvaluationResult EvaluateUnaryOperator(
        StaticEvaluationContext context, ExpressionNode expression, VBUnaryOperatorExpressionNode unaryOperator)
    {
        var operandResult = Evaluate(context, unaryOperator.Operand);
        if (operandResult.IsError)
        {
            return operandResult;
        }

        // see EvaluateBinaryOperator: an operand that is not known is late-bound, not wrong.
        if (operandResult.Result is VBUnknownType)
        {
            return StaticSemanticsEvaluationResult.Success(VBUnknownType.TypeInfo);
        }

        // see EvaluateBinaryOperator: an operand is a value. The parentheses around an argument (`Foo (x)`) are no operator of the program's: what they
        // coerce to is the parameter's to say. The parentheses around an operand of an operator are its simple data value (MS-VBAL §5.6.6) - an object's
        // default member, even where the operator takes an object: `(o) Is Nothing` tests what o's default member returns, and `((o))` reads it twice
        // (MS-VBA, Excel-verified).
        var operand = operandResult.Result!;
        if (unaryOperator.Token != OperatorSymbolNames.UnaryLetCoerceOp)
        {
            // the simple data value of what parentheses enclose is of whatever type it is - an object, when the default member returns one; an operand of
            // any other unary operator is a value of a declared type.
            var destination = unaryOperator.Token == OperatorSymbolNames.UnaryArithmeticPrecedenceOp ? ValueDestination.SimpleDataValue : ValueDestination.TypedValue;
            var value = ObjectValueStaticSemantics.ValueOf(context, unaryOperator.Operand, operand, destination);
            if (value.IsError)
            {
                return value;
            }

            operand = value.Result!;
        }

        return ResolveUnaryOperatorRule(unaryOperator.Token) is { } rule
            ? rule.DetermineDeclaredType(context, expression, AsOperand(operand))
            : StaticSemanticsEvaluationResult.Success(VBUnknownType.TypeInfo);
    }

    // One rule instance per call; every operator rule is a small record or a shared Lazy singleton, so this never allocates anything meaningful. An
    // operator with no rule falls through to null, deferred by the caller like any other unmapped case.
    internal static IStaticSemantics? ResolveBinaryOperatorRule(string token) => token switch
    {
        Tokens.AdditionOp => new BinaryAdditionOperatorStaticSemantics(),
        Tokens.SubtractionOp => new BinarySubtractionOperatorStaticSemantics(),
        Tokens.MultiplicationOp => new BinaryMultiplicationOperatorStaticSemantics(),
        Tokens.DivisionOp => new BinaryDivisionOperatorStaticSemantics(),
        Tokens.IntegerDivisionOp => new BinaryIntegerDivisionOperatorStaticSematics(),
        Tokens.ModuloOp => new BinaryModuloOperatorStaticSemantics(),
        Tokens.PowerOp => new BinaryExponentOperatorStaticSemantics(),
        Tokens.ConcatOp => new BinaryConcatOperatorStaticSemantics(),
        Tokens.CompareIsOp => new BinaryIsRefEqOperatorStaticSemantics(),
        Tokens.CompareEqualOp or Tokens.CompareNotEqualOp or Tokens.CompareGreaterThanOp
            or Tokens.CompareGreaterThanOrEqualOp or Tokens.CompareLessThanOp
            or Tokens.CompareLessThanOrEqualOp or Tokens.CompareLikeOp => new BinaryRelationalOperatorStaticSemantics(),
        Tokens.LogicalAndOp or Tokens.LogicalOrOp or Tokens.LogicalXOrOp
            or Tokens.LogicalEqvOp or Tokens.LogicalImpOp => new BinaryLogicalOperatorStaticSemantics(token),
        _ => null,
    };

    internal static IStaticSemantics? ResolveUnaryOperatorRule(string token) => token switch
    {
        Tokens.NegationOp => new UnaryNegationOperatorStaticSemantics(),
        Tokens.LogicalNotOp => new UnaryLogicalOperatorStaticSemantics(),
        // MS-VBAL 5.6.6: a pair of parentheses is an operator - around an operand of an operator, the precedence one; anywhere else, the let-coercion one
        // (RD-VBAL §5.6.6) - and either leaves the declared type alone.
        // Without a rule it falls through to the VBUnknownType above, which would lose the type of
        // everything anyone ever wrote parentheses around.
        OperatorSymbolNames.UnaryLetCoerceOp => new UnaryLetCoerceOperatorStaticSemantics(),
        OperatorSymbolNames.UnaryArithmeticPrecedenceOp => new UnaryPrecedenceOperatorStaticSemantics(),
        _ => null,
    };
}
