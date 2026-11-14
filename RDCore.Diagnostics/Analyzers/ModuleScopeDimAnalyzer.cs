using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00202</c>: a <c>Dim</c> statement at the module level, which declares <c>Private</c> variables (<strong>MS-VBAL §5.2.3</strong>).
/// </summary>
/// <remarks>
/// A module-level variable that states no access modifier can only have been declared with <c>Dim</c>. One finding is made for each statement, however many variables it declares.
/// </remarks>
internal sealed class ModuleScopeDimAnalyzer : SyntaxTreeAnalyzer
{
    public override IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ModuleScopeDimDeclaration.ToCode()];

    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
        => DeclarationStatements.In(module.Children)
            .Where(statement => statement[0] is VariableDeclarationNode { AccessModifier: AccessModifier.Implicit })
            .Select(statement => new AnalyzerFinding(
                RDCoreDiagnosticId.ModuleScopeDimDeclaration.ToCode(),
                DeclarationStatements.RangeOf(statement),
                DiagnosticSeverity.Information,
                RDCoreDiagnosticsResources.ModuleScopeDimDeclaration_Message));
}
