using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00315</c>: <c>Option Base 0</c> or <c>Option Compare Binary</c>, the options a module has when it states none (<strong>MS-VBAL §5.2.1.1</strong>,
/// <strong>§5.2.1.2</strong>).
/// </summary>
internal sealed class RedundantModuleOptionAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.ParseResult.SyntaxTree?.Children ?? [])
            .OfType<ModuleOptionDirectiveNode>()
            .Where(option => option.ModuleOption is ModuleOptions.OptionBase0 or ModuleOptions.OptionCompareBinary)
            .Select(option => (Option: option, Name: option.ModuleOption is ModuleOptions.OptionBase0 ? "Option Base 0" : "Option Compare Binary"))
            .Select(found => new AnalyzerFinding(
                RDCoreDiagnosticId.RedundantModuleOption.ToCode(),
                found.Option.SourceLocation.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.RedundantModuleOption_Message, found.Name))
            {
                // the module has the same option without it.
                Fixes = LineEdits.WholeLinesOf(context.ParseResult, found.Option) is { } lines
                    ? new DiagnosticFixes
                    {
                        Edit = SyntaxEdits.Fix(AnalyzerMessages.Format(RDCoreDiagnosticsResources.RedundantModuleOption_Fix, found.Name), SyntaxEdits.Remove(lines)),
                    }
                    : null,
            });
}
