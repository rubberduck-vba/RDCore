using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00103</c>: a <c>Def&lt;Type&gt;</c> directive, which gives a type to every name it covers that is declared without one (<strong>MS-VBAL §5.2.2</strong>).
/// </summary>
internal sealed class TypeDefDirectiveAnalyzer : SyntaxTreeAnalyzer
{
    public override IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ImplicitTypeDeclarationsEnabled.ToCode()];

    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
        => module.Children
            .OfType<TypeDefDirectiveNode>()
            .Select(directive => new AnalyzerFinding(
                RDCoreDiagnosticId.ImplicitTypeDeclarationsEnabled.ToCode(),
                directive.Location.Range,
                DiagnosticSeverity.Hint,
                Say(RDCoreDiagnosticsResources.ImplicitTypeDeclarationsEnabled_Message, directive.Token)));
}
