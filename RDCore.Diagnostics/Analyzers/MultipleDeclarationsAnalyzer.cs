using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00204</c>: a <c>Dim</c>, <c>Static</c> or <c>Const</c> statement that declares several names (<strong>MS-VBAL §5.2.3.1.1</strong>, <strong>§5.4.3.1</strong>).
/// </summary>
/// <remarks>
/// Each name has a type of its own, and a clause that is written once at the end of the statement is the type of the last name only. One finding is made for each statement.
/// </remarks>
internal sealed class MultipleDeclarationsAnalyzer : SyntaxTreeAnalyzer
{
    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
        => module.Descendants()
            .Prepend(module)
            .SelectMany(node => node.SiblingLists())
            .SelectMany(siblings => DeclarationStatements.In(siblings))
            .Where(statement => statement.Count > 1)
            .Select(statement => new AnalyzerFinding(
                RDCoreDiagnosticId.MultipleDeclarations,
                DeclarationStatements.RangeOf(statement),
                DiagnosticSeverity.Information,
                RDCoreDiagnosticsResources.MultipleDeclarations_Message));
}
