using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.Runtime.LanguageCore.Instructions;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.4.2.5, §5.4.2.7, §5.4.2.17, §5.4.2.18, §5.4.2.19
// stated once: StatementStaticSemanticsEvaluator and InstructionListLowering must agree on it.
// Exit Function is accepted in a Property Get, where MS-VBAL §5.4.2.18 refuses it: MS-VBA accepts it, and programs rely on that.
public static class ExitStatementStaticSemantics
{
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
