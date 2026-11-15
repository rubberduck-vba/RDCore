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
using RDCore.Runtime.LanguageCore.Static.Abstract;
using System.Collections.Immutable;
using RDCore.SDK;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.6.13.1, §5.3.1.11, §5.6.13
internal static class ArgumentListStaticSemantics
{
    public static VBCompileErrorInfo? Error(
        StaticEvaluationContext context, ExpressionNode call, VBTypeMemberSymbol procedure, ImmutableArray<VBParameterSymbol> parameters,
        ImmutableArray<ExpressionNode> arguments)
    {
        var own = parameters.Where(parameter => !(parameter.Name == "Me" && parameter.ParameterKind == ParameterKind.ImplicitByRef)).ToImmutableArray();

        // `F()` is written with an empty argument list, which is not an argument: it is the call itself.
        var written = arguments.IsDefault || arguments is [MissingArgumentNode] ? [] : arguments;
        if (own.IsEmpty && !written.IsEmpty && procedure is VBReturningMemberSymbol { ResolvedType: VBVariantType or VBObjectType or VBClassType or VBUnknownType })
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

                if ((AnyError(context, procedure, parameter, named.Value) ?? RecordError(context, procedure, parameter, named.Value)
                    ?? ArrayError(context, procedure, parameter, named.Value) ?? ByRefError(context, procedure, parameter, named.Value)
                    ?? ValueError(context, procedure, parameter, named.Value)) is { } namedByRef)
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

                if (RecordError(context, procedure, own[^1], argument) is { } collected)
                {
                    return collected;
                }

                continue;
            }

            // an argument left out (`F(, 2)`) gives its parameter nothing.
            if (argument is not MissingArgumentNode)
            {
                given.Add(own[position].Name);
                if ((AnyError(context, procedure, own[position], argument) ?? RecordError(context, procedure, own[position], argument)
                    ?? ArrayError(context, procedure, own[position], argument) ?? ByRefError(context, procedure, own[position], argument)
                    ?? ValueError(context, procedure, own[position], argument)) is { } byRef)
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

    // an `As Any` parameter - of a Declare, or of a library member (VarPtr) - takes a variable of any type but an array, which MS-VBA refuses there by its
    // name (observed in Excel: "Type mismatch"); an element of one it takes. A parameter of another procedure that has no declared
    // type is one whose type is not resolved, and is not this.
    private static VBCompileErrorInfo? AnyError(StaticEvaluationContext context, VBTypeMemberSymbol procedure, VBParameterSymbol parameter, ExpressionNode argument)
    {
        if (parameter.ResolvedType is not VBUnknownType || parameter is ParamArrayParameterSymbol
            || procedure is not (VBExternalFunctionMemberSymbol or VBExternalSubMemberSymbol) && procedure.GetProperty(SymbolProperties.ExternalTarget) is null
            || argument is not SimpleNameExpressionNode name
            || context.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope.Uri).Symbol is not ITypedSymbol { ResolvedType: VBArrayType } variable
            || variable is VBReturningMemberSymbol or VBProcedureMemberSymbol)
        {
            return null;
        }

        return VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, argument.Location,
            string.Format(Exceptions.VBArgument_ArrayToAsAny_Verbose, name.IdentifierName));
    }

    // a record is passed by reference, to a parameter of its type. MS-VBA refuses one given to a Variant - ByVal, ByRef, untyped, a ParamArray, a library's
    // (`c.Add t1` of a Collection) - and one given in parentheses, which is a value and no variable to pass, `F (t1)` (observed in Excel).
    // A record of a referenced library's public type is one a Variant holds (VBExternalUserDefinedType); a Declare's parameters are its own;
    // and Len and LenB take "any valid variable name" (MS-VBAL §6.1.2.11.1.22), a record's for its length.
    private static VBCompileErrorInfo? RecordError(StaticEvaluationContext context, VBTypeMemberSymbol procedure, VBParameterSymbol parameter, ExpressionNode argument)
    {
        var toVariant = parameter is ParamArrayParameterSymbol || parameter.ResolvedType is VBVariantType;
        if (procedure is VBExternalFunctionMemberSymbol or VBExternalSubMemberSymbol || argument is MissingArgumentNode or AddressOfExpressionNode
            || IsLength(procedure)
            || !toVariant && parameter.ResolvedType is not VBUserDefinedType
            || ExpressionStaticSemanticsEvaluator.Evaluate(context with { Facts = null, Bindings = null }, argument)
                is not { IsSuccess: true, Result: { } given } || LetCoercionStaticSemantics.RecordOf(given) is not { } record)
        {
            return null;
        }

        // an array of records is refused a Variant too; given to a record parameter it is the ByRef mismatch ByRefError reports.
        return toVariant
            ? VBCompileErrorInfo.For(VBCompileErrorId.UserDefinedTypeNotCoercibleToVariant, argument.Location,
                string.Format(Exceptions.VBCompileError_UserDefinedTypeNotCoercibleToVariant_Verbose, record))
            : given is VBUserDefinedType && argument is VBUnaryOperatorExpressionNode { Token: OperatorSymbolNames.UnaryLetCoerceOp }
                ? VBCompileErrorInfo.For(VBCompileErrorId.VariableRequired, argument.Location, record)
                : null;
    }

    private static bool IsLength(VBTypeMemberSymbol procedure)
        => procedure is VBFunctionMemberSymbol { Name: var name } && RDCore.SDK.Runtime.StdLib.StandardLibrary.IsLibraryMember(procedure)
            && (string.Equals(name, "Len", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "LenB", StringComparison.OrdinalIgnoreCase));

    // MS-VBAL §5.3.1.11: a variable given to a ByRef parameter is aliased to it, and is of its declared type. MS-VBA refuses one of another type
    // (Excel-verified): "ByRef argument type mismatch". An array parameter is ArrayError's; a Variant parameter takes any; a parenthesized argument is a
    // value, which is copied (§5.6.6); and a parameter of a class or Object is not checked here.
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
            (VBArrayType, _) => null,
            (var expected, var given) when IsIntrinsic(expected) && (IsIntrinsic(given) || given is VBVariantType) && !ParameterLists.SameType(expected, given)
                => VBCompileErrorInfo.For(VBCompileErrorId.ByRefArgumentTypeMismatch, argument.Location, name.IdentifierName),
            // a Variant is no variable of a class, whatever it holds; an Object variable is, and a class variable is an Object (Excel-verified).
            (VBClassType, VBVariantType)
                => VBCompileErrorInfo.For(VBCompileErrorId.ByRefArgumentTypeMismatch, argument.Location, name.IdentifierName),
            _ => null,
        };
    }

    // MS-VBAL §5.3.1.11: an array parameter is a second name for an array of its element type, which it is given. MS-VBA refuses another at compile time,
    // "Type mismatch: array or user-defined type expected" (observed in Excel): an array of another element type -
    // an Integer() to a Long(), a Variant() to a Long(), a Collection() to an Object() - a Variant, whatever it holds (a variable, a parameter, an element
    // of a Variant array, what Array or Split returns, to a Variant() or a String() too), a scalar, and an array in parentheses, which is a value and no
    // array variable, of its element type or not, in a statement's argument list too (`Takes (c)`). What a function or a Property Get returns
    // that is an array of its element type it takes, as an array field of a record; a member of a late-bound object is a Variant.
    // The element types are matched as they are declared, except that every Enum is the same element type: an array of an Enum is no array of Long, nor
    // an array of Long one of an Enum, but it is an array of another Enum; an array of String * N is no array of String.
    // The standard library's array parameters are checked too: IRR given a Variant, or an array in parentheses.
    // ArrayError checks those; ByRefError leaves an array parameter to it. A ParamArray and a Declare's parameter are not checked here.
    private static VBCompileErrorInfo? ArrayError(StaticEvaluationContext context, VBTypeMemberSymbol procedure, VBParameterSymbol parameter, ExpressionNode argument)
    {
        if (parameter.ResolvedType is not VBArrayType { ItemType: var expected } || parameter is ParamArrayParameterSymbol
            || procedure is VBExternalFunctionMemberSymbol or VBExternalSubMemberSymbol
            || argument is MissingArgumentNode or ByValArgumentExpressionNode
            || ExpressionStaticSemanticsEvaluator.Evaluate(context with { Facts = null, Bindings = null }, argument) is not { IsSuccess: true, Result: { } given })
        {
            return null;
        }

        var mismatched = given switch
        {
            VBArrayType when argument is VBUnaryOperatorExpressionNode { Token: OperatorSymbolNames.UnaryLetCoerceOp } => true,
            VBArrayType { ItemType: var item } => !VBArrayType.SameElementType(expected, item),
            VBVariantType => true,
            _ => IsIntrinsic(given),
        };
        return mismatched ? VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatchArrayOrUserDefinedTypeExpected, argument.Location, NameOf(argument)) : null;
    }


    private static string NameOf(ExpressionNode argument) => argument switch
    {
        SimpleNameExpressionNode name => name.IdentifierName,
        MemberAccessExpressionNode access => access.Member.IdentifierName,
        _ => string.Empty,
    };

    // MS-VBAL §5.3.1.11: a parameter given a value - ByVal, or ByRef and given what is not a variable - takes it by Let-coercion, which can be statically
    // invalid for its declared type (§5.5.1.1; MS-VBA: "Type mismatch" for a LongLong given to a ByVal Long, Excel-verified). An object is given its
    // default member's value, which ObjectValueStaticSemantics checks; a variable given ByRef is ByRefError's. An address (AddressOf) is a LongPtr, given
    // to any parameter - of a Declare too, which is what it is for - and refused by one of a type it cannot be (MS-VBA: a Long, an Integer, an Object; an
    // `As Any` parameter has no declared type, and takes it).
    private static VBCompileErrorInfo? ValueError(StaticEvaluationContext context, VBTypeMemberSymbol procedure, VBParameterSymbol parameter, ExpressionNode argument)
    {
        if (argument is AddressOfExpressionNode)
        {
            if (parameter is ParamArrayParameterSymbol || parameter.ResolvedType is VBUnknownType or VBVariantType)
            {
                return null;
            }
        }
        else if (parameter is ParamArrayParameterSymbol || procedure is VBExternalFunctionMemberSymbol or VBExternalSubMemberSymbol
            || parameter.ResolvedType is VBUnknownType or VBVariantType or VBClassType or VBObjectType
            || argument is MissingArgumentNode or ByValArgumentExpressionNode
            || parameter.ParameterKind is ParameterKind.ImplicitByRef or ParameterKind.ExplicitByRef && argument is SimpleNameExpressionNode)
        {
            return null;
        }

        if (ExpressionStaticSemanticsEvaluator.Evaluate(context with { Facts = null, Bindings = null }, argument) is not { IsSuccess: true, Result: { } given }
            || given is VBUnknownType or VBVoidType or VBClassType or VBObjectType)
        {
            return null;
        }

        var coerced = LetCoercionStaticSemantics.Instance.DetermineDeclaredType(context, argument, given, parameter.ResolvedType);
        return coerced.IsError ? coerced.ErrorInfo as VBCompileErrorInfo : null;
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
