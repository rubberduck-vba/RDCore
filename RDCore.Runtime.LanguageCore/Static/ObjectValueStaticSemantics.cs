using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Flags;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using System.Collections.Immutable;
using RDCore.SDK.Semantics;
using RDCore.SDK;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.5.1.2.13, §5.5.1.1; RD-VBAL §5.5.1.1
public static class ObjectValueStaticSemantics
{
    public static StaticSemanticsEvaluationResult ValueOf(StaticEvaluationContext context, ExpressionNode expression, VBType declaredType, ValueDestination destination)
    {
        if (destination is ValueDestination.TypedValue)
        {
            MarkLetCoerced(context.Facts, expression);
        }

        if (IsNothingLiteral(expression))
        {
            return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.InvalidUseOfObject, expression.Location,
                Exceptions.VBCompileError_NothingIsNotAValue_Verbose));
        }

        // MS-VBAL §5.6.2.1: a subroutine has no value to evaluate to (MS-VBA: "Expected Function or variable", Excel-verified).
        if (declaredType is VBVoidType)
        {
            return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.ExpectedFunctionOrVariable, expression.Location, NameOf(expression) ?? string.Empty));
        }

        if (declaredType is not VBClassType declaredClass)
        {
            return StaticSemanticsEvaluationResult.Success(declaredType);
        }

        var classType = CurrentTypes.Of(context.Resolver, declaredClass);
        if (classType.DefaultMember is not { } defaultMember)
        {
            // MS-VBAL 5.5.1.1 makes it statically invalid; MS-VBA compiles it, and raises 438 when it runs - 91 when the object is Nothing (RD-VBAL 5.5.1.1).
            // The object is read for a value when it runs, as a late-bound one is.
            return StaticSemanticsEvaluationResult.Success(VBObjectType.TypeInfo);
        }

        if (OwnParameters(ParametersOf(defaultMember)).Any(parameter => !parameter.IsOptional && parameter is not ParamArrayParameterSymbol))
        {
            return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.ArgumentNotOptional, expression.Location,
                string.Format(Exceptions.VBCompileError_DefaultMemberTakesArguments_Verbose, classType.Name, defaultMember.Name)));
        }

        if (defaultMember.ResolvedType is VBClassType or VBObjectType)
        {
            return destination switch
            {
                ValueDestination.TypedValue => StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, expression.Location,
                    string.Format(Exceptions.VBCompileError_DefaultMemberReturnsAnObject_Verbose, classType.Name, defaultMember.Name))),
                // parentheses read the default member once: what it returns is the object it is declared as.
                ValueDestination.SimpleDataValue => StaticSemanticsEvaluationResult.Success(defaultMember.ResolvedType),
                _ => StaticSemanticsEvaluationResult.Success(VBObjectType.TypeInfo),
            };
        }

        return StaticSemanticsEvaluationResult.Success(defaultMember.ResolvedType);
    }

    // MS-VBAL §5.5.1.1: the expression's value is taken and Let-coerced to a declared type.
    public static void MarkLetCoerced(IExpressionFactSink? facts, ExpressionNode expression)
    {
        if (facts is not null && facts.TryGet(expression.Identity, out var fact) && !fact.IsLetCoerced)
        {
            facts.Record(fact with { IsLetCoerced = true });
        }
    }

    public static ValueDestination? DestinationOf(VBParameterSymbol parameter)
    {
        if (parameter is ParamArrayParameterSymbol)
        {
            return null;
        }

        if (parameter.TryGetProperty(SymbolProperties.ValueDestination, out var destination))
        {
            return destination;
        }

        return IsValueType(parameter.ResolvedType) ? ValueDestination.TypedValue : null;
    }

    public static bool IsValueType(VBType type) => type is INumericType or VBStringType or VBBooleanType or VBDateType or VBEnumType
        || ExpressionStaticSemanticsEvaluator.AsOperand(type) is INumericType;

    // MS-VBAL §5.3.1.11
    public static VBCompileErrorInfo? ArgumentsError(StaticEvaluationContext context, ImmutableArray<VBParameterSymbol> parameters, IEnumerable<ExpressionNode> arguments)
    {
        var own = OwnParameters(parameters).ToImmutableArray();
        var position = 0;
        foreach (var argument in arguments)
        {
            VBParameterSymbol? parameter;
            ExpressionNode value;
            if (argument is NamedArgumentNode named)
            {
                parameter = own.FirstOrDefault(candidate => string.Equals(candidate.Name, named.Name, StringComparison.OrdinalIgnoreCase));
                value = named.Value;
            }
            else
            {
                parameter = position < own.Length ? own[position] : null;
                value = argument;
                if (parameter is not ParamArrayParameterSymbol)
                {
                    position++;
                }
            }

            if (parameter is null || value is MissingArgumentNode || DestinationOf(parameter) is not { } destination)
            {
                continue;
            }

            // ByVal says how the argument is passed; the argument is the expression it is written before. The call evaluated it already, and recorded what
            // it binds to: this only asks its type again, and records nothing over it.
            var passed = value is ByValArgumentExpressionNode byVal ? byVal.Operand : value;
            var argumentType = ExpressionStaticSemanticsEvaluator.Evaluate(Unrecorded(context), passed);
            if (argumentType is not { IsSuccess: true, Result: { } type })
            {
                continue;
            }

            if (destination is ValueDestination.StringOrVariable)
            {
                if (StringOrVariableError(context, passed, type) is { } notAStringOrAVariable)
                {
                    return notAStringOrAVariable;
                }

                continue;
            }

            // MS-VBAL §5.3.1.11: a variable of the parameter's own type, given to a ByRef parameter, is passed by reference; its value is not taken.
            var byReference = value is not ByValArgumentExpressionNode && parameter.ParameterKind is ParameterKind.ImplicitByRef or ParameterKind.ExplicitByRef
                && passed is not VBUnaryOperatorExpressionNode { Token: OperatorSymbolNames.UnaryLetCoerceOp } && DenotesAVariable(context, passed)
                && string.Equals(type.Name, parameter.ResolvedType.Name, StringComparison.OrdinalIgnoreCase);
            if (ValueOf(byReference ? context with { Facts = null } : context, passed, type, destination) is { IsError: true } refused)
            {
                return refused.ErrorInfo as VBCompileErrorInfo;
            }
        }

        return null;
    }

    public static VBCompileErrorInfo? NotAValueArgumentError(
        StaticEvaluationContext context, ExpressionNode callee, ImmutableArray<ExpressionNode> arguments, ExpressionNode argument, VBCompileErrorInfo? error)
    {
        var value = argument switch
        {
            NamedArgumentNode named => named.Value,
            ByValArgumentExpressionNode byVal => byVal.Operand,
            _ => argument,
        };

        if (!NameClassificationStaticSemantics.IsNotAValue(value, error) || ParametersOfCallee(context, callee) is not { } parameters)
        {
            return null;
        }

        var own = OwnParameters(parameters).ToImmutableArray();
        var position = arguments.IndexOf(argument);
        var parameter = argument is NamedArgumentNode { Name: var name }
            ? own.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            : position >= 0 && position < own.Length ? own[position] : null;

        return parameter is not null && DestinationOf(parameter) is ValueDestination.StringOrVariable
            ? VBCompileErrorInfo.For(VBCompileErrorId.VariableRequired, value.Location,
                string.Format(Exceptions.VBCompileError_NameIsNoVariable_Verbose, NameClassificationStaticSemantics.NameOf(value), error!.Verbose))
            : null;
    }

    // Len and LenB: "any valid string expression, or any valid variable name" (MS-VBAL §6.1.2.11.1.22). A value that is a String or a Variant is a string
    // expression, and so is one MS-VBA reads as one when it runs (an object, a record); a value of any other declared type must be a variable, whose size is
    // measured. An object stands for its default member's value, which is no variable: MS-VBA refuses it unless that value is a String or a Variant.
    private static VBCompileErrorInfo? StringOrVariableError(StaticEvaluationContext context, ExpressionNode expression, VBType type)
    {
        if (ValueOf(context, expression, type, ValueDestination.AnyValue) is { IsError: true } refused)
        {
            return refused.ErrorInfo as VBCompileErrorInfo;
        }

        if (type is VBClassType declaredClass && CurrentTypes.Of(context.Resolver, declaredClass) is { DefaultMember: { } defaultMember } classType)
        {
            return IsReadAsAString(defaultMember.ResolvedType) && defaultMember.ResolvedType is not (VBClassType or VBObjectType)
                ? null
                : VBCompileErrorInfo.For(VBCompileErrorId.VariableRequired, expression.Location,
                    string.Format(Exceptions.VBCompileError_LenOfADefaultMemberValue_Verbose, classType.Name, defaultMember.Name, defaultMember.ResolvedType.Name));
        }

        return type is VBClassType || IsReadAsAString(type) || DenotesAVariable(context, expression)
            ? null
            : VBCompileErrorInfo.For(VBCompileErrorId.VariableRequired, expression.Location,
                string.Format(Exceptions.VBCompileError_LenOfAValue_Verbose, type.Name));
    }

    // what Len reads as a string expression when it runs, as MS-VBA has it (recorded in Excel): a String, a Variant, a late-bound object, a record.
    private static bool IsReadAsAString(VBType type)
        => type is VBStringType or VBFixedStringType or VBVariantType or VBObjectType or VBUserDefinedType or VBUnknownType or VBNullType or VBEmptyType;

    // MS-VBAL §5.6: an expression classified as a variable - a variable or a parameter by its name, qualified by its module or not, a field of a record or a
    // public variable of a class, an element of an array. A constant, a call, a property, a literal and an operator's result are values. MS-VBA takes a
    // variable in parentheses as the variable, for Len (recorded in Excel).
    private static bool DenotesAVariable(StaticEvaluationContext context, ExpressionNode expression) => expression switch
    {
        VBUnaryOperatorExpressionNode { Token: OperatorSymbolNames.UnaryLetCoerceOp } parenthesized => DenotesAVariable(context, parenthesized.Operand),
        SimpleNameExpressionNode name => IsAVariable(context.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope.Uri).Symbol),
        MemberAccessExpressionNode { Owner: { } owner } access when context.Resolver.NamespaceOf(owner, context.Scope.Uri) is { } qualifier
            => IsAVariable(context.Resolver.ResolveMember(qualifier, access.Member, context.Scope.Uri).Symbol),
        MemberAccessExpressionNode { Owner: { } owner } access => ExpressionStaticSemanticsEvaluator.Evaluate(Unrecorded(context), owner) switch
        {
            { IsSuccess: true, Result: VBUserDefinedType } => true,
            { IsSuccess: true, Result: VBClassType declared } => CurrentTypes.Of(context.Resolver, declared).Members.FirstOrDefault(member
                => member.Name.Equals(access.Member.IdentifierName, StringComparison.OrdinalIgnoreCase)) is { Kind: SymbolKindExt.Variable or SymbolKindExt.Field },
            _ => false,
        },
        IndexExpressionNode { Callee: var callee } => ExpressionStaticSemanticsEvaluator.Evaluate(Unrecorded(context), callee) is { IsSuccess: true, Result: VBArrayType },
        _ => false,
    };

    private static bool IsAVariable(Symbol? symbol) => symbol is { Kind: SymbolKindExt.Variable or SymbolKindExt.Field } or VBParameterSymbol;

    public static ImmutableArray<VBParameterSymbol>? ParametersOfCallee(StaticEvaluationContext context, ExpressionNode callee)
        => MemberOfCallee(context, callee) is { } member ? ParametersOf(member) : null;

    public static VBTypeMemberSymbol? MemberOfCallee(StaticEvaluationContext context, ExpressionNode callee)
    {
        var symbol = callee switch
        {
            SimpleNameExpressionNode name => context.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope.Uri).Symbol,
            MemberAccessExpressionNode { Owner: { } owner } access when context.Resolver.NamespaceOf(owner, context.Scope.Uri) is { } qualifier
                => context.Resolver.ResolveMember(qualifier, access.Member, context.Scope.Uri).Symbol,
            MemberAccessExpressionNode { Owner: { } owner } access when ExpressionStaticSemanticsEvaluator.Evaluate(Unrecorded(context), owner) is { IsSuccess: true, Result: VBClassType classType }
                => CalledMemberOf(context, classType, access),
            // a member of the target of the enclosing With block (MS-VBAL §5.6.15).
            MemberAccessExpressionNode { Owner: null } access when context.EnclosingWithTargetType is VBClassType classType => CalledMemberOf(context, classType, access),
            _ => null,
        };

        return symbol is VBReturningMemberSymbol or VBProcedureMemberSymbol ? (VBTypeMemberSymbol)symbol : null;
    }

    public static VBTypeMemberSymbol? DesignerControlOf(StaticEvaluationContext context, ExpressionNode target)
    {
        var symbol = target switch
        {
            SimpleNameExpressionNode name => context.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope.Uri).Symbol,
            MemberAccessExpressionNode { Owner: { } owner } access when ExpressionStaticSemanticsEvaluator.Evaluate(Unrecorded(context), owner) is { IsSuccess: true, Result: VBClassType classType }
                => (VBProjectSymbol.ResolveClass(context.Resolver, classType.Symbol) ?? classType.Symbol).DefaultInterfaceMembers.FirstOrDefault(member
                    => string.Equals(member.Name, access.Member.IdentifierName, StringComparison.OrdinalIgnoreCase)),
            _ => null,
        };

        return symbol is VBTypeMemberSymbol member && member.GetProperty(SymbolProperties.DesignerControl) ? member : null;
    }

    // the member of a class a call or a read of the name invokes, as the class is declared now: a Sub, a Function or a Property Get - not a Property Let or
    // Set of the same name, which an assignment invokes.
    private static VBTypeMemberSymbol? CalledMemberOf(StaticEvaluationContext context, VBClassType classType, MemberAccessExpressionNode access)
        => (VBProjectSymbol.ResolveClass(context.Resolver, classType.Symbol) ?? classType.Symbol).DefaultInterfaceMembers.FirstOrDefault(member
            => string.Equals(member.Name, access.Member.IdentifierName, StringComparison.OrdinalIgnoreCase)
                && member is VBReturningMemberSymbol or VBProcedureMemberSymbol && member is not (VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol));

    // the parameters of a procedure, whichever kind of member declares it.
    private static ImmutableArray<VBParameterSymbol> ParametersOf(VBTypeMemberSymbol member) => member switch
    {
        VBReturningMemberSymbol returning => returning.Parameters,
        VBProcedureMemberSymbol procedure => procedure.Parameters,
        _ => [],
    };

    // MS-VBAL §5.6.13: a Function or Property Get that declares no parameters, which an argument list does not call: it is called with none, and the
    // arguments apply to what it returns.
    public static bool ReturnsWhatArgumentsApplyTo(VBTypeMemberSymbol member)
        => member is VBFunctionMemberSymbol or VBPropertyGetMemberSymbol && !OwnParameters(ParametersOf(member)).Any();

    // a member of a class has an implicit Me at parameter 0, which is the object the call is on and no argument of it.
    private static IEnumerable<VBParameterSymbol> OwnParameters(ImmutableArray<VBParameterSymbol> parameters)
        => parameters.Where(parameter => !(parameter.Name == "Me" && parameter.ParameterKind == ParameterKind.ImplicitByRef));

    // the name a subroutine is written by, for the message of the error.
    private static string? NameOf(ExpressionNode expression) => expression switch
    {
        SimpleNameExpressionNode name => name.IdentifierName,
        MemberAccessExpressionNode access => access.Member.IdentifierName,
        IndexExpressionNode index => NameOf(index.Callee),
        _ => null,
    };

    // the same context, with nothing recording what an expression evaluated again binds to.
    private static StaticEvaluationContext Unrecorded(StaticEvaluationContext context) => context with { Facts = null, Bindings = null };

    private static bool IsNothingLiteral(ExpressionNode expression)
        => expression is LiteralExpressionNode { StaticValue: VBNothingValue };
}
