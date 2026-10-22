using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Static.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// Whether the argument list of a call matches the parameters of the procedure it calls (<strong>MS-VBAL §5.6.13.1</strong> Argument Lists,
/// <strong>§5.3.1.11</strong> Procedure Invocation Argument Processing), as MS-VBA checks it at compile time (recorded in Excel).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>An argument no parameter takes - one too many, with no <c>ParamArray</c> to collect it, or a named argument given to a procedure that declares no
/// parameter - is <see cref="VBCompileErrorId.WrongNumberOfArgumentsOrInvalidPropertyAssignment"/>.</item>
/// <item>A named argument that names no parameter is <see cref="VBCompileErrorId.NamedArgumentNotFound"/>; one that names a parameter already given an
/// argument is <see cref="VBCompileErrorId.NamedArgumentAlreadySpecified"/>.</item>
/// <item>A parameter that is neither <c>Optional</c> nor a <c>ParamArray</c>, and that the call gives no argument, is
/// <see cref="VBCompileErrorId.ArgumentNotOptional"/>.</item>
/// <item>A subroutine given as an argument has no value to give: <see cref="VBCompileErrorId.ExpectedFunctionOrVariable"/>.</item>
/// </list>
/// A <c>Function</c> or <c>Property Get</c> that declares no parameter, and returns what can be indexed - a <c>Variant</c>, an array, an object - is not given
/// the arguments written after it: they index what it returns, and are not this check's.
/// </remarks>
internal static class ArgumentListStaticSemantics
{
    /// <summary>
    /// The first way the arguments of a call do not match the parameters of the procedure it calls.
    /// </summary>
    /// <param name="context">The compile-time context the arguments are evaluated against.</param>
    /// <param name="call">The call, where an argument that is missing is reported.</param>
    /// <param name="procedure">The procedure called.</param>
    /// <param name="parameters">Its parameters; an implicit <c>Me</c> is not one a call gives an argument to.</param>
    /// <param name="arguments">The arguments, positional and named, as they are written.</param>
    /// <returns>The error, or <see langword="null"/> when they match.</returns>
    public static VBCompileErrorInfo? Error(
        StaticEvaluationContext context, ExpressionNode call, VBTypeMemberSymbol procedure, ImmutableArray<VBParameterSymbol> parameters,
        ImmutableArray<ExpressionNode> arguments)
    {
        var own = parameters.Where(parameter => !(parameter.Name == "Me" && parameter.ParameterKind == ParameterKind.ImplicitByRef)).ToImmutableArray();

        // `F()` is written with an empty argument list, which is not an argument: it is the call itself.
        var written = arguments.IsDefault || arguments is [MissingArgumentNode] ? [] : arguments;
        if (own.IsEmpty && !written.IsEmpty && procedure is VBReturningMemberSymbol { ResolvedType: VBVariantType or VBObjectType or VBClassType or VBArrayType or VBUnknownType })
        {
            return null;
        }

        var hasParamArray = own.Any(parameter => parameter is ParamArrayParameterSymbol);
        var given = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var position = 0;
        foreach (var argument in written)
        {
            if (SubroutineGiven(context, argument is NamedArgumentNode { Value: var value } ? value : argument) is { } subroutine)
            {
                return subroutine;
            }

            if (argument is NamedArgumentNode named)
            {
                if (own.IsEmpty)
                {
                    return VBCompileErrorInfo.For(VBCompileErrorId.WrongNumberOfArgumentsOrInvalidPropertyAssignment, named.Location, procedure.Name);
                }

                var parameter = own.FirstOrDefault(candidate => candidate is not ParamArrayParameterSymbol
                    && string.Equals(candidate.Name, named.Name, StringComparison.OrdinalIgnoreCase));
                if (parameter is null)
                {
                    return VBCompileErrorInfo.For(VBCompileErrorId.NamedArgumentNotFound, named.Location, named.Name);
                }

                if (!given.Add(parameter.Name))
                {
                    return VBCompileErrorInfo.For(VBCompileErrorId.NamedArgumentAlreadySpecified, named.Location, named.Name);
                }

                if (ByRefError(context, procedure, parameter, named.Value) is { } namedByRef)
                {
                    return namedByRef;
                }

                continue;
            }

            if (position >= own.Length || own[position] is ParamArrayParameterSymbol)
            {
                // what a ParamArray collects is any number of arguments.
                if (!hasParamArray)
                {
                    return VBCompileErrorInfo.For(VBCompileErrorId.WrongNumberOfArgumentsOrInvalidPropertyAssignment, argument.Location, procedure.Name);
                }

                continue;
            }

            // an argument left out (`F(, 2)`) gives its parameter nothing.
            if (argument is not MissingArgumentNode)
            {
                given.Add(own[position].Name);
                if (ByRefError(context, procedure, own[position], argument) is { } byRef)
                {
                    return byRef;
                }
            }

            position++;
        }

        return own.FirstOrDefault(parameter => !parameter.IsOptional && parameter is not ParamArrayParameterSymbol && !given.Contains(parameter.Name)) is { } missing
            ? VBCompileErrorInfo.For(VBCompileErrorId.ArgumentNotOptional, call.Location, missing.Name)
            : null;
    }

    // MS-VBAL §5.3.1.11: a variable given to a ByRef parameter is aliased to it, and is of its declared type - an array, of its element type. MS-VBA refuses
    // one of another type (Excel-verified): "ByRef argument type mismatch", or for an array "Type mismatch: array or user-defined type expected". A Variant
    // parameter takes any; a parenthesized argument is a value, which is copied (§5.6.6); and a parameter of a class or Object is not checked here.
    private static VBCompileErrorInfo? ByRefError(StaticEvaluationContext context, VBTypeMemberSymbol procedure, VBParameterSymbol parameter, ExpressionNode argument)
    {
        if (parameter.ParameterKind is not (ParameterKind.ImplicitByRef or ParameterKind.ExplicitByRef) || parameter is ParamArrayParameterSymbol
            || procedure is VBExternalFunctionMemberSymbol or VBExternalSubMemberSymbol
            || argument is not SimpleNameExpressionNode name
            || context.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope.Uri).Symbol is not ITypedSymbol { ResolvedType: var argumentType } variable
            || variable is VBReturningMemberSymbol or VBProcedureMemberSymbol or VBConstantMemberSymbol or VBLocalConstantSymbol or VBEnumConstMemberSymbol)
        {
            return null;
        }

        return (parameter.ResolvedType, argumentType) switch
        {
            (VBArrayType { ItemType: var expected }, VBArrayType { ItemType: var given }) when IsIntrinsic(expected) && IsIntrinsic(given) && !ParameterLists.SameType(expected, given)
                => VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatchArrayOrUserDefinedTypeExpected, argument.Location, name.IdentifierName),
            (var expected, var given) when IsIntrinsic(expected) && (IsIntrinsic(given) || given is VBVariantType) && !ParameterLists.SameType(expected, given)
                => VBCompileErrorInfo.For(VBCompileErrorId.ByRefArgumentTypeMismatch, argument.Location, name.IdentifierName),
            // a Variant is no variable of a class, whatever it holds; an Object variable is, and a class variable is an Object (Excel-verified).
            (VBClassType, VBVariantType)
                => VBCompileErrorInfo.For(VBCompileErrorId.ByRefArgumentTypeMismatch, argument.Location, name.IdentifierName),
            _ => null,
        };
    }

    // a number, a String, a Boolean or a Date: what a ByRef parameter's type is compared by. A Variant parameter takes any variable.
    private static bool IsIntrinsic(VBType type) => type is VBNumericType or VBStringType or VBBooleanType or VBDateType;

    // MS-VBAL §5.6.2.1: a subroutine given as an argument has no value to give, whichever parameter takes it.
    private static VBCompileErrorInfo? SubroutineGiven(StaticEvaluationContext context, ExpressionNode argument)
    {
        var passed = argument is ByValArgumentExpressionNode byVal ? byVal.Operand : argument;
        if (passed is MissingArgumentNode)
        {
            return null;
        }

        return ExpressionStaticSemanticsEvaluator.Evaluate(context with { Facts = null, Bindings = null }, passed) is { IsSuccess: true, Result: VBVoidType }
            ? VBCompileErrorInfo.For(VBCompileErrorId.ExpectedFunctionOrVariable, passed.Location,
                passed switch { SimpleNameExpressionNode name => name.IdentifierName, MemberAccessExpressionNode access => access.Member.IdentifierName, _ => string.Empty })
            : null;
    }
}
