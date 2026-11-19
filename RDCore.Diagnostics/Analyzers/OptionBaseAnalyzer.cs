using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00102</c>: a module that states <c>Option Base 1</c> (<strong>MS-VBAL §5.2.1.2</strong>).
/// </summary>
/// <remarks>
/// <c>Option Base 0</c> states the default, and is not reported.
/// </remarks>
internal sealed class OptionBaseAnalyzer : SyntaxTreeAnalyzer
{
    public override IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ImplicitNonDefaultArrayBase.ToCode()];

    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
        => module.Children
            .OfType<ModuleOptionDirectiveNode>()
            .Where(directive => directive.ModuleOption == ModuleOptions.OptionBase1)
            .Select(directive => new AnalyzerFinding(
                RDCoreDiagnosticId.ImplicitNonDefaultArrayBase.ToCode(), directive.Location.Range, DiagnosticSeverity.Hint, RDCoreDiagnosticsResources.ImplicitNonDefaultArrayBase_Message));
}
