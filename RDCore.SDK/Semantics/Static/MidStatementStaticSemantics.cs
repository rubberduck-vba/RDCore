using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Semantics.Static.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// <strong>MS-VBAL §5.4.3.5</strong> the <c>Mid</c>, <c>MidB</c>, <c>Mid$</c> and <c>MidB$</c> statements (static semantics).
/// </summary>
/// <remarks>
/// "The declared type of &lt;string-argument&gt; MUST be String or Variant", and the target is a <c>bound-variable-expression</c>, so it
/// is a variable. The statement's other operands are not given a rule of their own, but they are used as the statement's runtime semantics
/// uses them: the position and the length as <c>Long</c> (the declared type of the parameters of the <c>Mid</c> function), and the value as
/// a <c>String</c>, so each has to be Let-coercible to that (<strong>MS-VBAL §5.5.1.1</strong>).
/// </remarks>
public static class MidStatementStaticSemantics
{
    /// <summary>
    /// Evaluates the operands of a <c>Mid</c> statement and checks them.
    /// </summary>
    /// <param name="context">The compile-time context the statement is evaluated in.</param>
    /// <param name="mid">The statement.</param>
    /// <returns>Every error found, in operand order. Empty when the statement is valid.</returns>
    public static ImmutableArray<VBCompileErrorInfo> Evaluate(StaticEvaluationContext context, MidStatementNode mid)
    {
        var checks = new StatementOperandChecks(context);

        var targetType = checks.TypeOf(mid.Target);
        checks.RequireVariable(mid.Target, "The target of a Mid statement");
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
