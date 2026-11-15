using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Workspace;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Runtime.LanguageCore.Static;

// a Print with no file number is the Print method of the form or report it is written in: a Visual Basic 6.0 form's code prints on the form (drawsOnMe).
public static class BarePrintStaticSemantics
{
    public static VBCompileErrorInfo? Evaluate(PrintStatementNode print, SupportedLanguage? language, bool drawsOnMe = false)
        => print is { FileNumber: null } && language is { HasBarePrint: false } && !drawsOnMe
            ? VBCompileErrorInfo.For(VBCompileErrorId.SubOrFunctionNotDefined, print.SourceLocation,
                $"'{print.Token}' is not a statement of {language.Name}: a Print with no file number is the member of a form or a report, which prints only in the code of a Visual Basic 6.0 form, and in VBA it is a reserved identifier that is no statement inside a procedure.")
            : null;
}
