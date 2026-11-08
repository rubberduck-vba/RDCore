using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using System.Collections.Immutable;
using RDCore.SDK;

namespace RDCore.Runtime.LanguageCore.Static;

/// <summary>
/// What a <c>ParamArray</c> parameter may be given to: <c>Invalid ParamArray use</c> (<see cref="VBCompileErrorId.InvalidParamArrayUse"/>) where it is passed
/// as the argument of a <c>ByRef</c> parameter of a procedure of the code's own (<strong>MS-VBAL §5.3.1.11</strong>).
/// </summary>
/// <remarks>
/// A <c>ParamArray</c> is an array of values the call collected, not a variable of the caller's, so there is nothing a reference parameter can be a second name
/// for. MS-VBA refuses it as a compile error, written bare (<c>Inner p</c>) or in parentheses (<c>Inner (p)</c>), whether the parameter is a <c>Variant</c>, an
/// array or implicitly <c>ByRef</c>, of a procedure, a function or a member of a class. It takes it as the argument of a <c>ByVal</c> parameter, of another
/// <c>ParamArray</c>, and of the standard library (<c>IsMissing(p)</c>); an element of it (<c>p(0)</c>) is a variable (all recorded in Excel).
/// </remarks>
public static class ParamArrayStaticSemantics
{
    /// <summary>
    /// The first <c>Invalid ParamArray use</c> of the arguments of a call to <paramref name="member"/>, or <see langword="null"/> when there is none.
    /// </summary>
    /// <param name="context">The compile-time context the arguments are resolved in.</param>
    /// <param name="member">The procedure called.</param>
    /// <param name="parameters">Its parameters; an implicit <c>Me</c> is not one a call gives an argument to.</param>
    /// <param name="arguments">The arguments of the call, positional and named, as they are written.</param>
    public static VBCompileErrorInfo? ArgumentsError(
        StaticEvaluationContext context, VBTypeMemberSymbol member, ImmutableArray<VBParameterSymbol> parameters, IEnumerable<ExpressionNode> arguments)
    {
        // a library's members, and a Declare's, are not the code's own: they take a ParamArray as what it is.
        if (member is VBExternalFunctionMemberSymbol or VBExternalSubMemberSymbol
            || member.GetProperty(SymbolProperties.ExternalTarget) is not null || member.GetProperty(SymbolProperties.Library) is not null)
        {
            return null;
        }

        var own = parameters.Where(parameter => !(parameter.Name == "Me" && parameter.ParameterKind == ParameterKind.ImplicitByRef)).ToImmutableArray();
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

            if (parameter is null or ParamArrayParameterSymbol
                || parameter.ParameterKind is not (ParameterKind.ImplicitByRef or ParameterKind.ExplicitByRef)
                || Unparenthesized(value) is not SimpleNameExpressionNode name
                || context.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope.Uri).Symbol is not ParamArrayParameterSymbol paramArray)
            {
                continue;
            }

            return VBCompileErrorInfo.For(VBCompileErrorId.InvalidParamArrayUse, value.Location,
                string.Format(Exceptions.VBCompileError_InvalidParamArrayUse_ByRef_Verbose, paramArray.Name, parameter.Name));
        }

        return null;
    }

    // `(p)` is `p` let-coerced (MS-VBAL §5.6.6), which MS-VBA refuses all the same.
    private static ExpressionNode Unparenthesized(ExpressionNode expression)
    {
        while (expression is VBUnaryOperatorExpressionNode { Token: OperatorSymbolNames.UnaryLetCoerceOp } parenthesized)
        {
            expression = parenthesized.Operand;
        }

        return expression;
    }
}
