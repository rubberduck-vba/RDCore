using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11026</c>: a public variable of an object, named as a member of it (<c>b.Count</c>, <c>Me.Count</c>), or an element of one (<c>b.V(0)</c>), given to
/// a <c>ByRef</c> parameter, which <strong>MS-VBAL §5.6.12</strong> and <strong>§5.3.1.11</strong> pass by reference and MS-VBA passes as a copy; or given to
/// a procedure called late, which may take it by reference.
/// </summary>
/// <remarks>
/// That the argument is one is a fact the language core states about it (<see cref="ValueExpressionSemanticFlags.ObjectVariablePassedAsACopy"/>,
/// <see cref="ValueExpressionSemanticFlags.ObjectVariablePassedLateAsACopy"/>). RD-VBA passes the copy, as MS-VBA does (RD-VBAL §5.3.1.11): what the
/// procedure assigns to its parameter is lost, where the specification has it written to the object's variable. A warning, for code that relies on what the
/// specification says.
/// </remarks>
internal sealed class ObjectVariablePassedByRefAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Procedures ?? [])
            .SelectMany(procedure => procedure.Expressions)
            .Where(fact => fact.Flags.HasFlag(ValueExpressionSemanticFlags.ObjectVariablePassedAsACopy)
                || fact.Flags.HasFlag(ValueExpressionSemanticFlags.ObjectVariablePassedLateAsACopy))
            .Select(fact => new AnalyzerFinding(
                RDCoreDiagnosticId.ObjectVariablePassedByRefIsACopy, fact.Location.Range, DiagnosticSeverity.Warning,
                fact.Flags.HasFlag(ValueExpressionSemanticFlags.ObjectVariablePassedAsACopy)
                    ? RDCoreDiagnosticsResources.ObjectVariablePassedByRefIsACopy_Message
                    : RDCoreDiagnosticsResources.ObjectVariablePassedLateIsACopy_Message));
}
