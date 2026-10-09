using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00304</c>: an <c>Error</c> statement, in a language where it is a relic of classic BASIC (<strong>MS-VBAL §5.4.4.3</strong>).
/// </summary>
/// <remarks>
/// <c>Err.Raise</c> raises the same error. Whether the language keeps the statement as the way to raise one (<see cref="SDK.Workspace.SupportedLanguage.UsesClassicBasicSyntax"/>)
/// is a fact the host that loaded the module vouches for: an analyzer that is not given it has nothing to say.
/// </remarks>
internal sealed class ObsoleteErrorStatementAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => ModuleLanguage.UsesClassicBasicSyntax(context) is false && context.ParseResult.SyntaxTree is { } module
            ? module.Descendants()
                .OfType<ErrorStatementNode>()
                .Select(statement => new AnalyzerFinding(
                    RDCoreDiagnosticId.ObsoleteErrorSyntax,
                    statement.SourceLocation.Range,
                    DiagnosticSeverity.Information,
                    RDCoreDiagnosticsResources.ObsoleteErrorSyntax_Message))
            : [];
}
