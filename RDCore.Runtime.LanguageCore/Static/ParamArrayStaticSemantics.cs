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

// MS-VBAL §5.3.1.11
// a ParamArray holds values the call collected, not a caller's variable: there is nothing for a reference parameter to be a second name for.
public static class ParamArrayStaticSemantics
{
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
