using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Symbols;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00114</c>: a <c>ReDim</c> statement that declares the array it re-dimensions, its name matching nothing (<strong>MS-VBAL §5.4.3.3</strong>).
/// </summary>
/// <remarks>
/// That the <c>ReDim</c> declares its array is a fact of the declaration the host vouches for (<see cref="SDK.Semantics.DeclarationFact.DeclaredBy"/>): only
/// the host knows the name matched no variable or constant of any module or library. A <c>ReDim</c> of a misspelled name declares a new array, under
/// <c>Option Explicit</c> too, where a <c>Dim</c> would have said so. That the name hides a procedure, a module or a library function of the same name is
/// another analyzer's to say.
/// </remarks>
internal sealed class ImplicitArrayDeclarationAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Declarations ?? [])
            .Where(declaration => declaration.DeclaredBy is LocalDeclarationKind.ReDim)
            .Select(declaration => new AnalyzerFinding(
                RDCoreDiagnosticId.ImplicitArrayDeclaration, declaration.Location.Range, DiagnosticSeverity.Information,
                string.Format(RDCoreDiagnosticsResources.ImplicitArrayDeclaration_Message, declaration.Name)));
}
