using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00306</c>: a <c>Let</c> statement written with the <c>Let</c> keyword (<strong>MS-VBAL §5.4.3.8</strong>).
/// </summary>
/// <remarks>
/// The assignment is the same without the keyword. The finding is the keyword, and the fix takes it out, with what is between it and the target.
/// </remarks>
internal sealed class ObsoleteLetStatementAnalyzer : SyntaxTreeAnalyzer
{
    public override IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ObsoleteLetStatement.ToCode()];

    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
        => module.Descendants()
            .OfType<AssignmentStatementNode>()
            .Where(assignment => assignment.Kind is AssignmentKind.ExplicitLet)
            .Select(assignment => new AnalyzerFinding(
                RDCoreDiagnosticId.ObsoleteLetStatement.ToCode(),
                ModuleLanguage.KeywordAt(assignment.SourceLocation.Range, "Let".Length),
                DiagnosticSeverity.Information,
                RDCoreDiagnosticsResources.ObsoleteLetStatement_Message)
            {
                Fixes = new DiagnosticFixes
                {
                    Edits = [SyntaxEdits.Fix(RDCoreDiagnosticsResources.ObsoleteLetStatement_Fix,
                        SyntaxEdits.Remove(new SourceRange(assignment.SourceLocation.Range.Start, assignment.Target.SourceLocation.Range.Start)))],
                },
            });
}
