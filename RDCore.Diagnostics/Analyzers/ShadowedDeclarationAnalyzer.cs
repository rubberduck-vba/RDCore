using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00208</c>: a local or a parameter named like a member of its module, a <c>Public</c> member of another module, a member of a library or a module, which
/// its procedure no longer sees by that name (<strong>MS-VBAL §5.6.10</strong>).
/// </summary>
/// <remarks>
/// A simple name is first matched in its procedure: <c>Dim Left As Long</c> makes <c>Left(s, 1)</c> an index of the variable in that procedure, and
/// <c>Count</c> a parameter there, where it is the module's variable everywhere else. What the name means in the rest of the module is a fact of the declaration
/// the host vouches for (<see cref="SDK.Semantics.DeclarationFact.Hides"/>): only the host resolves names across modules and libraries.
/// </remarks>
internal sealed class ShadowedDeclarationAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Declarations ?? [])
            .Where(declaration => declaration.Hides is not null)
            .Select(declaration => new AnalyzerFinding(
                RDCoreDiagnosticId.ShadowedDeclaration, declaration.Location.Range, DiagnosticSeverity.Information,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.ShadowedDeclaration_Message, declaration.Name, declaration.Hides!.QualifiedName)));
}
