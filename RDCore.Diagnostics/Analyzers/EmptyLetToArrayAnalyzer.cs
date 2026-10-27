using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11016</c>: the <c>Empty</c> or the <c>Null</c> literal is Let-assigned to an array - a variable, or the result of a function, declared as one -
/// which raises run-time error 13 (Type mismatch) whenever it runs.
/// </summary>
/// <remarks>
/// The <c>Empty</c> and <c>Null</c> literals have a declared type of <c>Variant</c> (<strong>MS-VBAL §3.3.5.2</strong>), and a <c>Variant</c> is valid to
/// Let-coerce to an array (<strong>MS-VBAL §5.5.1.1</strong>): the code compiles, in MS-VBA as in RD-VBA. The value each holds is no array, and the assignment
/// raises 13 when it runs (Excel-measured, <c>oracle\s46</c> F17a-b, <c>oracle\s48</c> J01-J02). The host states it from the literal and the declared type
/// alone (<see cref="ValueExpressionSemanticFlags.EmptyLetToArray"/>), so it holds of every path, whether or not the run-time facts reach the statement.
/// </remarks>
internal sealed class EmptyLetToArrayAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Procedures ?? []).SelectMany(procedure => procedure.Expressions
            .Where(fact => fact.Flags.HasFlag(ValueExpressionSemanticFlags.EmptyLetToArray))
            .Select(fact => new AnalyzerFinding(
                RDCoreDiagnosticId.EmptyLetToArray,
                fact.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.EmptyLetToArray_Message, fact.DeclaredType ?? "Empty"))));
}
