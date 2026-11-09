using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Workspace;

namespace RDCore.Runtime.LanguageCore.Static;

public static class BarePrintStaticSemantics
{
    public static VBCompileErrorInfo? Evaluate(PrintStatementNode print, SupportedLanguage? language)
        => print is { FileNumber: null } && language is { HasBarePrint: false }
            ? VBCompileErrorInfo.For(VBCompileErrorId.SubOrFunctionNotDefined, print.SourceLocation,
                $"'{print.Token}' is not a statement of {language.Name}: a Print with no file number is the member of a form or a report in VB6, which the platform has none of, and in VBA it is a reserved identifier that is no statement inside a procedure.")
            : null;
}
