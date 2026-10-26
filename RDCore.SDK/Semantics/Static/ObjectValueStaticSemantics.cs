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
using RDCore.SDK.Semantics.Static.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// What an object is where a value is expected, at compile time: the static half of <strong>MS-VBAL §5.5.1.2.13</strong>'s <em>simple data value</em>
/// (<strong>MS-VBAL §5.5.1.1</strong>).
/// </summary>
/// <remarks>
/// <para>
/// A value is expected of the value of a <c>Let</c> assignment to a variable of a value type, of an operand of any operator but <c>Is</c>, of an argument of a
/// parameter declared with a value type - <c>ByVal</c>, or <c>ByRef</c> and given an expression - and of an argument of the standard library's parameters that
/// read a value (<see cref="SymbolProperties.ValueDestination"/>). There, an object stands for what its default member returns, and what cannot have a value
/// is a compile error, as MS-VBA has it (recorded in Excel):
/// </para>
/// <list type="bullet">
/// <item>The <c>Nothing</c> literal: <see cref="VBCompileErrorId.InvalidUseOfObject"/>. An object variable that holds <c>Nothing</c> compiles, and raises error
/// 91 when it runs.</item>
/// <item>An object of a class whose default member takes an argument, as a <c>Collection</c>'s <c>Item</c> does: <see cref="VBCompileErrorId.ArgumentNotOptional"/>.</item>
/// <item>An object of a class whose default member returns an object, where a value of a declared type is expected:
/// <see cref="VBCompileErrorId.TypeMismatch"/> (<see cref="ValueDestination.TypedValue"/>). <c>Abs</c>, <c>Int</c> and <c>Fix</c> take one, and read it
/// when they run (<see cref="ValueDestination.AnyValue"/>); <c>Len</c> and <c>LenB</c> take a <c>String</c> or a variable, which it is not
/// (<see cref="ValueDestination.StringOrVariable"/>).</item>
/// </list>
/// <para>
/// 👉 An object of a class that has no default member, where a value of a declared type is expected, is <see cref="VBCompileErrorId.TypeMismatch"/> too: MS-VBAL
/// §5.5.1.1 makes it statically invalid. MS-VBA compiles it, and raises error 438 when it runs, which the error says (<see cref="NoDefaultMember"/>).
/// </para>
/// </remarks>
public static class ObjectValueStaticSemantics
{
    /// <summary>
    /// The declared type of the value <paramref name="expression"/> stands for where a value is expected, or the compile error that it has none.
    /// </summary>
    /// <param name="context">The compile-time context: the class of an object is read from it as the composition has it now (see <see cref="CurrentTypes"/>).</param>
    /// <param name="expression">The expression whose value is expected.</param>
    /// <param name="declaredType">The declared type of <paramref name="expression"/>.</param>
    /// <param name="destination">What the place that expects a value expects of an object.</param>
    /// <returns>
    /// <paramref name="declaredType"/> for anything that is not an object of a class; the declared type of the class's default member for one that has a
    /// default member; <see cref="VBObjectType"/> - late-bound - where a value of any type is expected, for one that has none or whose default member returns
    /// an object; or the error.
    /// </returns>
    public static StaticSemanticsEvaluationResult ValueOf(StaticEvaluationContext context, ExpressionNode expression, VBType declaredType, ValueDestination destination)
    {
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

        var classType = CurrentTypes.Of(context, declaredClass);
        if (classType.DefaultMember is not { } defaultMember)
        {
            // MS-VBAL 5.5.1.1: statically invalid. A function that takes a value of any type reads it when it runs, as MS-VBA has it.
            return destination is ValueDestination.TypedValue
                ? StaticSemanticsEvaluationResult.Error(NoDefaultMember(expression, classType))
                : StaticSemanticsEvaluationResult.Success(VBObjectType.TypeInfo);
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

    /// <summary>
    /// The compile error of an object of a class that has no default member, Let-coerced to a value (<strong>MS-VBAL §5.5.1.1</strong>).
    /// </summary>
    /// <param name="expression">The expression that is the object.</param>
    /// <param name="classType">The class of the object.</param>
    /// <remarks>
    /// 👉 MS-VBA compiles it, and raises error 438 (Object doesn't support this property or method) when it runs; RD-VBA validates at compile time what the
    /// specification says is validated at compile time, and says so in the error.
    /// </remarks>
    public static VBCompileErrorInfo NoDefaultMember(ExpressionNode expression, VBClassType classType)
        => VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, expression.Location,
            string.Format(Exceptions.VBCompileError_ClassHasNoDefaultMember_Verbose, classType.Name));

    /// <summary>
    /// What <paramref name="parameter"/> expects of an object given to it; <see langword="null"/> when it takes an object as it is.
    /// </summary>
    /// <param name="parameter">A parameter of a procedure.</param>
    /// <remarks>
    /// A parameter declared with a value type expects a value of that type; a standard-library parameter declared <c>Variant</c> says what it expects, if it
    /// reads a value at all (<see cref="SymbolProperties.ValueDestination"/>); any other parameter - <c>Variant</c>, an object, an array, a user-defined type,
    /// a <c>ParamArray</c> - takes what it is given.
    /// </remarks>
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

    /// <summary>
    /// Whether <paramref name="type"/> is the declared type of a value - a number, a string, a <c>Boolean</c>, a <c>Date</c>, an enumeration - which a
    /// variable or a parameter declared with it expects of what it is given.
    /// </summary>
    /// <param name="type">A declared type.</param>
    public static bool IsValueType(VBType type) => type is INumericType or VBStringType or VBBooleanType or VBDateType or VBEnumType
        || ExpressionStaticSemanticsEvaluator.AsOperand(type) is INumericType;

    /// <summary>
    /// The first compile error of the arguments of a call, each given to the parameter it is the argument of (<strong>MS-VBAL §5.3.1.11</strong>): an object
    /// given to a parameter that expects a value of it must have one.
    /// </summary>
    /// <param name="context">The compile-time context the arguments are evaluated against.</param>
    /// <param name="parameters">The parameters of the procedure called; an implicit <c>Me</c> is not one a call gives an argument to.</param>
    /// <param name="arguments">The arguments of the call, positional and named, as they are written.</param>
    /// <returns>The first error, or <see langword="null"/> when there is none.</returns>
    /// <remarks>
    /// An argument that matches no parameter is not this check's to report: the call's argument list is checked when it runs. A <c>ParamArray</c> takes what
    /// it collects as it is.
    /// </remarks>
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

            if (ValueOf(context, passed, type, destination) is { IsError: true } refused)
            {
                return refused.ErrorInfo as VBCompileErrorInfo;
            }
        }

        return null;
    }

    /// <summary>
    /// The error of an argument that is a project, a module or an enumeration (<paramref name="error"/>, the argument's own), given to a parameter that takes a
    /// <c>String</c> or a variable - what <c>Len</c> and <c>LenB</c> measure: MS-VBA requires a variable there (Excel-verified, oracle\s29 C05, C06, C35, O19).
    /// </summary>
    /// <param name="context">The compile-time context the call is evaluated against.</param>
    /// <param name="callee">What is called.</param>
    /// <param name="arguments">The arguments of the call.</param>
    /// <param name="argument">The argument whose evaluation is <paramref name="error"/>.</param>
    /// <param name="error">The argument's error.</param>
    /// <returns>The error to report instead, or <see langword="null"/> when <paramref name="error"/> is the one.</returns>
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

        if (type is VBClassType declaredClass && CurrentTypes.Of(context, declaredClass) is { DefaultMember: { } defaultMember } classType)
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
            { IsSuccess: true, Result: VBClassType declared } => CurrentTypes.Of(context, declared).Members.FirstOrDefault(member
                => member.Name.Equals(access.Member.IdentifierName, StringComparison.OrdinalIgnoreCase)) is { Kind: SymbolKindExt.Variable or SymbolKindExt.Field },
            _ => false,
        },
        IndexExpressionNode { Callee: var callee } => ExpressionStaticSemanticsEvaluator.Evaluate(Unrecorded(context), callee) is { IsSuccess: true, Result: VBArrayType },
        _ => false,
    };

    private static bool IsAVariable(Symbol? symbol) => symbol is { Kind: SymbolKindExt.Variable or SymbolKindExt.Field } or VBParameterSymbol;

    /// <summary>
    /// The parameters of the procedure a call written on <paramref name="callee"/> calls - by its bare name, qualified by the project or module that declares
    /// it, or as a member of an object of a class; <see langword="null"/> when it calls no procedure that is known (an array, a late-bound member).
    /// </summary>
    /// <param name="context">The compile-time context the callee is evaluated against.</param>
    /// <param name="callee">The expression the call is written on.</param>
    public static ImmutableArray<VBParameterSymbol>? ParametersOfCallee(StaticEvaluationContext context, ExpressionNode callee)
        => MemberOfCallee(context, callee) is { } member ? ParametersOf(member) : null;

    /// <summary>
    /// The procedure a call written on <paramref name="callee"/> calls - by its bare name, qualified by the project or module that declares it, or as a
    /// member of an object of a class; <see langword="null"/> when it calls no procedure that is known (an array, a late-bound member).
    /// </summary>
    /// <param name="context">The compile-time context the callee is evaluated against.</param>
    /// <param name="callee">The expression the call is written on.</param>
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

    /// <summary>
    /// The control of a form that <paramref name="target"/> names - by its bare name in the form's code, or as a member of the form (<c>Me.TextBox1</c>,
    /// <c>UserForm1.TextBox1</c>); <see langword="null"/> when it names something else (<see cref="SymbolProperties.DesignerControl"/>).
    /// </summary>
    /// <param name="context">The compile-time context the target is evaluated against.</param>
    /// <param name="target">The expression an assignment is written on.</param>
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
