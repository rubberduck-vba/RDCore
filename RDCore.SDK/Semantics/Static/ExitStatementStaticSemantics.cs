using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// Where an <c>Exit</c> statement may be written: <strong>MS-VBAL §5.4.2.5</strong> (<c>Exit For</c>), <strong>§5.4.2.7</strong> (<c>Exit Do</c>),
/// <strong>§5.4.2.17</strong> (<c>Exit Sub</c>), <strong>§5.4.2.18</strong> (<c>Exit Function</c>) and <strong>§5.4.2.19</strong>
/// (<c>Exit Property</c>).
/// </summary>
/// <remarks>
/// The rule is stated once, here, because two walkers of a procedure body each have the position it depends on - the loops around the statement, and the
/// kind of procedure it is in - and both have to agree on it: <see cref="StatementStaticSemanticsEvaluator"/>, and
/// <see cref="Instructions.InstructionListLowering"/>, the pass that actually reports the errors of a module as it is loaded.
/// <para>
/// An <c>Exit For</c> or <c>Exit Do</c> must be lexically inside a loop of its kind, at any depth: an <c>Exit For</c> in a <c>Do</c> loop that is itself in a
/// <c>For</c> loop is the <c>For</c> loop's.
/// </para>
/// <para>
/// 👉 <c>Exit Function</c> is also accepted in a <c>Property Get</c>. <strong>MS-VBAL §5.4.2.18</strong> says it must be in a function, but MS-VBA accepts it in a
/// property getter (see <see cref="VBCompileErrorId.ExitFunctionNotAllowedInSubOrProperty"/>), and a program written against MS-VBA relies on that.
/// </para>
/// </remarks>
public static class ExitStatementStaticSemantics
{
    /// <summary>
    /// Checks one <c>Exit</c> statement against the position it is written in.
    /// </summary>
    /// <param name="exit">The statement. Anything that is not an <c>Exit</c> statement is valid.</param>
    /// <param name="withinFor">Whether the statement is lexically inside a <c>For</c> or <c>For Each</c> loop.</param>
    /// <param name="withinDo">Whether the statement is lexically inside a <c>Do</c> loop.</param>
    /// <param name="procedure">The kind of the procedure the statement is written in, or <see langword="null"/> when it is not known - the kind of
    /// procedure is not checked then.</param>
    /// <returns>The error, or <see langword="null"/> when the statement is where it may be.</returns>
    public static VBCompileErrorInfo? Evaluate(KeywordStatementNode exit, bool withinFor, bool withinDo, MemberKind? procedure)
        => exit.Token switch
        {
            Tokens.ExitFor when !withinFor => Error(VBCompileErrorId.ExitForNotWithinForNext, exit,
                "An Exit For statement must be inside a For or a For Each statement (MS-VBAL §5.4.2.5)."),
            Tokens.ExitDo when !withinDo => Error(VBCompileErrorId.ExitDoNotWithinDoLoop, exit,
                "An Exit Do statement must be inside a Do statement (MS-VBAL §5.4.2.7)."),
            Tokens.ExitSub when procedure is { } kind && kind is not MemberKind.Procedure => Error(VBCompileErrorId.ExitSubNotAllowedInFunctionOrProperty, exit,
                $"An Exit Sub statement must be inside a Sub, and this is {Describe(kind)} (MS-VBAL §5.4.2.17)."),
            Tokens.ExitFunction when procedure is { } kind && kind is not (MemberKind.Function or MemberKind.PropertyGet) => Error(VBCompileErrorId.ExitFunctionNotAllowedInSubOrProperty, exit,
                $"An Exit Function statement must be inside a Function, and this is {Describe(kind)} (MS-VBAL §5.4.2.18)."),
            Tokens.ExitProperty when procedure is { } kind && kind is not (MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet) => Error(VBCompileErrorId.ExitPropertyNotAllowedInSubOrFunction, exit,
                $"An Exit Property statement must be inside a Property, and this is {Describe(kind)} (MS-VBAL §5.4.2.19)."),
            _ => null,
        };

    private static string Describe(MemberKind kind) => kind switch
    {
        MemberKind.Procedure => "a Sub",
        MemberKind.Function => "a Function",
        MemberKind.PropertyGet => "a Property Get",
        MemberKind.PropertyLet => "a Property Let",
        MemberKind.PropertySet => "a Property Set",
        _ => $"a {kind}",
    };

    private static VBCompileErrorInfo Error(VBCompileErrorId id, KeywordStatementNode exit, string verbose)
        => VBCompileErrorInfo.For(id, exit.SourceLocation, verbose);
}
