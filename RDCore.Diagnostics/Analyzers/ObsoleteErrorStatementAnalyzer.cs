using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00304</c>: an <c>Error</c> statement, in a language where it is a relic of classic BASIC (<strong>MS-VBAL §5.4.4.3</strong>).
/// </summary>
/// <remarks>
/// <para>
/// <c>Err.Raise</c> raises the same error. Whether the language keeps the statement as the way to raise one (<see cref="SDK.Workspace.SupportedLanguage.UsesClassicBasicSyntax"/>)
/// is a fact the host that loaded the module vouches for: an analyzer that is not given it has nothing to say.
/// </para>
/// <para>
/// The fix writes <c>Err.Clear: Err.Raise</c> in place of the keyword. <c>Err.Raise</c> keeps the description, the source and the help context of an error
/// that <c>Err</c> still holds, where the <c>Error</c> statement takes those of the number it raises, so <c>Err</c> is cleared first. The statement's number is
/// an <c>Integer</c>, and <c>Err.Raise</c> takes a <c>Long</c>: a number that is not an <c>Integer</c> literal is passed through <c>CInt</c>, which overflows
/// where the statement did.
/// </para>
/// </remarks>
internal sealed class ObsoleteErrorStatementAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ObsoleteErrorSyntax.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => ModuleLanguage.UsesClassicBasicSyntax(context) is false && context.ParseResult.SyntaxTree is { } module
            ? module.Descendants()
                .OfType<ErrorStatementNode>()
                .Select(statement => new AnalyzerFinding(
                    RDCoreDiagnosticId.ObsoleteErrorSyntax.ToCode(),
                    statement.SourceLocation.Range,
                    DiagnosticSeverity.Information,
                    RDCoreDiagnosticsResources.ObsoleteErrorSyntax_Message) { Fixes = FixOf(statement) })
            : [];

    private static DiagnosticFixes FixOf(ErrorStatementNode statement)
    {
        var number = statement.NumberExpression.SourceLocation.Range;
        var keyword = ModuleLanguage.KeywordAt(statement.SourceLocation.Range, "Error".Length);
        var edits = statement.NumberExpression is LiteralExpressionNode { StaticValue: VBIntegerValue }
            ? [SyntaxEdits.Replace(keyword, "Err.Clear: Err.Raise")]
            : new[] { SyntaxEdits.Replace(new SourceRange(statement.SourceLocation.Range.Start, number.Start), "Err.Clear: Err.Raise CInt("), SyntaxEdits.Insert(number.End, ")") };
        return new DiagnosticFixes { Edit = SyntaxEdits.Fix(RDCoreDiagnosticsResources.ObsoleteErrorSyntax_Fix, edits) };
    }
}
