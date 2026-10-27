using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11021</c>: a <c>Const</c> of a procedure valued with <c>Len</c> of a constant expression (<c>Const K3 = Len(K)</c>), which MS-VBA compiles there and
/// refuses in a module.
/// </summary>
/// <remarks>
/// <strong>MS-VBAL §5.6.16.1</strong> lists <c>Len</c> among constant expressions. MS-VBA refuses it in a <c>Const</c> of a module, and compiles it in one of a
/// procedure - whose run crashes the host (recorded in Excel: oracle\s44 C01-C04). RD-VBA compiles it to the length MS-VBAL gives it, and warns, by the
/// author's ruling: the same declaration is an error one level up. A module's <c>Const</c> of <c>Len</c> is a compile error, and a <c>Len</c> that is no
/// function of the library makes the value no constant expression, so a module that loads has only the library's <c>Len</c> here.
/// </remarks>
internal sealed class ProcedureConstantLengthAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => context.ParseResult.SyntaxTree is { } module
            ? module.Descendants()
                .OfType<ConstantDeclarationNode>()
                .Where(constant => constant.ConstKind == ConstKind.Local && constant.Descendants().OfType<IndexExpressionNode>().Any(IsLen))
                .Select(constant => new AnalyzerFinding(
                    RDCoreDiagnosticId.ProcedureConstantLength,
                    constant.Location.Range,
                    DiagnosticSeverity.Warning,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.ProcedureConstantLength_Message, constant.Name)))
            : [];

    // a call of Len, by its name or through its module (VBA.Len).
    private static bool IsLen(IndexExpressionNode call) => call.Callee switch
    {
        SimpleNameExpressionNode name => string.Equals(name.IdentifierName, "Len", StringComparison.OrdinalIgnoreCase),
        MemberAccessExpressionNode access => string.Equals(access.Member.IdentifierName, "Len", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };
}
