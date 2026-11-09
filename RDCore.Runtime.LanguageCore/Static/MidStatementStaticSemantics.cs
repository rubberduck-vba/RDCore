using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using System.Collections.Immutable;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.4.3.5, §5.5.1.1
public static class MidStatementStaticSemantics
{
    public static ImmutableArray<VBCompileErrorInfo> Evaluate(StaticEvaluationContext context, MidStatementNode mid)
    {
        var checks = new StatementOperandChecks(context);

        var targetType = checks.TypeOf(mid.Target);
        checks.RequireVariable(mid.Target, "The target of a Mid statement", "5.4.3.5");
        checks.RequireStringOrVariant(mid.Target, targetType, "The target of a Mid statement");

        checks.RequireLetCoercible(mid.Start, checks.TypeOf(mid.Start), VBLongType.TypeInfo);
        if (mid.Length is { } length)
        {
            checks.RequireLetCoercible(length, checks.TypeOf(length), VBLongType.TypeInfo);
        }

        checks.RequireLetCoercible(mid.Value, checks.TypeOf(mid.Value), VBStringType.TypeInfo);
        return checks.Errors;
    }
}
