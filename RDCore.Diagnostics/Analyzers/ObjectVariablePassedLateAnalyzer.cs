using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11029</c>: a public variable of an object, named as a member of it (<c>b.Count</c>), or an element of one (<c>b.V(0)</c>), given to a procedure
/// called late (<c>o.Twice b.Count</c>, <c>o</c> an <c>Object</c> or a <c>Variant</c>), which MS-VBA passes as a copy.
/// </summary>
/// <remarks>
/// That the argument is one is a fact the language core states about it (<see cref="ValueExpressionSemanticFlags.ObjectVariablePassedLateAsACopy"/>). What the
/// procedure does with it is not known until it runs: if it takes the argument <c>ByRef</c>, what it assigns to the parameter is not written back to the
/// object's variable, as MS-VBA has it (oracle\s58 F04, RD-VBAL §5.3.1.11). A hint: a fact about how the argument is passed, not a problem to fix.
/// </remarks>
internal sealed class ObjectVariablePassedLateAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Procedures ?? [])
            .SelectMany(procedure => procedure.Expressions)
            .Where(fact => fact.Flags.HasFlag(ValueExpressionSemanticFlags.ObjectVariablePassedLateAsACopy))
            .Select(fact => new AnalyzerFinding(
                RDCoreDiagnosticId.ObjectVariablePassedLateIsACopy, fact.Location.Range, DiagnosticSeverity.Hint,
                RDCoreDiagnosticsResources.ObjectVariablePassedLateIsACopy_Message));
}
