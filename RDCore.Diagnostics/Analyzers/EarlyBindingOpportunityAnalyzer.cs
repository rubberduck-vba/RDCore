using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Semantics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00311</c>: a <c>CreateObject</c> call that creates an object of a class a referenced library declares, which could be declared as that class and
/// created with <c>New</c>.
/// </summary>
/// <remarks>
/// The class is a fact of the call (<see cref="ExpressionFact.Creates"/>), which is what this reads: it does not look at the syntax tree. A class the platform
/// implements itself is not one: in MS-VBA it is a class of a library that the project would have to reference.
/// </remarks>
internal sealed class EarlyBindingOpportunityAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Procedures ?? [])
            .SelectMany(procedure => procedure.Expressions)
            .Where(fact => fact.Creates is { Source: ObjectClassSource.ReferencedLibrary })
            .Select(fact => new AnalyzerFinding(
                RDCoreDiagnosticId.EarlyBindingOpportunity, fact.Location.Range, DiagnosticSeverity.Information,
                string.Format(RDCoreDiagnosticsResources.EarlyBindingOpportunity_Message, fact.Creates!.ProgId, fact.Creates.Library, fact.Creates.ClassName)));
}
