using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Source;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00101</c>: a module that does not state <c>Option Explicit</c> (<strong>MS-VBAL §5.2.1.3</strong>).
/// </summary>
/// <remarks>
/// Without it a name that is not declared is not an error, and a variable comes into being where it is used, so a misspelled name goes unnoticed. Whether the
/// module states it is a fact the host that loaded the module vouches for; an analyzer that is not given the fact has nothing to say.
/// </remarks>
internal sealed class OptionExplicitAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.Semantics is { OptionExplicit: false })
        {
            // the module as a whole is what is wrong with it: the first line is where a reader looks for the option.
            yield return new AnalyzerFinding(
                RDCoreDiagnosticId.ImplicitDeclarationsEnabled,
                new SourceRange(new SourcePosition(0, 0), new SourcePosition(1, 0)),
                DiagnosticSeverity.Warning,
                RDCoreDiagnosticsResources.ImplicitDeclarationsEnabled_Message);
        }
    }
}
