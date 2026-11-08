using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11025</c>: an array given a second empty argument list, <c>f()()</c>, which <strong>MS-VBAL §5.6.13.1</strong> makes invalid - an empty argument
/// list was already specified for it - and <strong>MS-VBA</strong> compiles, as the array.
/// </summary>
/// <remarks>
/// RD-VBA does as MS-VBA does (recorded in Excel: oracle\s53 E07, E08, F07, F08): <c>UBound(f()())</c> compiles, and <c>f()()</c> raises run-time error 9
/// (Subscript out of range) whenever it runs. A warning, because the specification refuses it, and it can never run. The host states it from the declarations
/// (<see cref="ValueExpressionSemanticFlags.ArrayGivenASecondEmptyArgumentList"/>).
/// </remarks>
internal sealed class ArrayGivenASecondEmptyArgumentListAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Procedures ?? []).SelectMany(procedure => procedure.Expressions
            .Where(fact => fact.Flags.HasFlag(ValueExpressionSemanticFlags.ArrayGivenASecondEmptyArgumentList))
            .Select(fact => new AnalyzerFinding(
                RDCoreDiagnosticId.ArrayGivenASecondEmptyArgumentList,
                fact.Location.Range,
                DiagnosticSeverity.Warning,
                RDCoreDiagnosticsResources.ArrayGivenASecondEmptyArgumentList_Message)));
}
