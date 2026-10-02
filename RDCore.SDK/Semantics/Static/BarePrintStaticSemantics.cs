using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Workspace;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// Where a <c>Print</c> statement with no file number is a statement at all.
/// </summary>
/// <remarks>
/// A bare <c>Print</c> is the <c>Print</c> member of a form or a report in VB6, which the platform has none of. In VBA it is a reserved identifier with
/// no semantics and no statement inside a procedure (only the Immediate window, which is not procedure scope, accepts it as <c>Debug.Print</c>), so in
/// a language without one (<see cref="SupportedLanguage.HasBarePrint"/>) it is as undefined as any other name the language does not declare.
/// </remarks>
public static class BarePrintStaticSemantics
{
    /// <summary>
    /// Evaluates a <c>Print</c> statement against the language it is written in.
    /// </summary>
    /// <param name="print">The statement.</param>
    /// <param name="language">The language the body is written in, or <see langword="null"/> when it is not stated: no language's rules apply then.</param>
    /// <returns>The error, or <see langword="null"/> when the statement exists in the language.</returns>
    public static VBCompileErrorInfo? Evaluate(PrintStatementNode print, SupportedLanguage? language)
        => print is { FileNumber: null } && language is { HasBarePrint: false }
            ? VBCompileErrorInfo.For(VBCompileErrorId.SubOrFunctionNotDefined, print.SourceLocation,
                $"'{print.Token}' is not a statement of {language.Name}: a Print with no file number is the member of a form or a report in VB6, which the platform has none of, and in VBA it is a reserved identifier that is no statement inside a procedure.")
            : null;
}
