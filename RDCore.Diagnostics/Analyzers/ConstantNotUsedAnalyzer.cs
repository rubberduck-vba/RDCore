using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Semantics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01015</c>: a constant nothing refers to.
/// </summary>
/// <remarks>
/// The host counts the references of a constant of a procedure (<see cref="SDK.Platform.Protocol.DeclarationFactDto.References"/>): its statements, the values
/// of its other constants and the bounds of its arrays. A constant of the module may be read by the declarations of the module, which the pass does not
/// evaluate, and is not reported.
/// </remarks>
internal sealed class ConstantNotUsedAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.Semantics is not { } semantics)
        {
            yield break;
        }

        foreach (var (fact, _) in UnusedDeclarations.Counted(semantics, DeclarationKind.Constant).Where(counted => counted.References is { Reads: 0, PassedAsArguments: 0 }))
        {
            yield return new AnalyzerFinding(
                RDCoreDiagnosticId.ConstantNotUsed.ToCode(),
                fact.Location.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.ConstantNotUsed_Message, fact.Name));
        }
    }
}
