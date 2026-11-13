using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11044</c>: <c>LSet</c> between two records when either one holds a variable-length <c>String</c>, which <strong>MS-VBAL §5.4.3.6</strong> allows and
/// MS-VBA refuses ("Compile error: Type mismatch").
/// </summary>
/// <remarks>
/// That a side of the byte copy holds one is a fact the language core states about the statement
/// (<see cref="FixedAssignmentSemanticFlags.SourceHoldsVariableLengthString"/>, <see cref="FixedAssignmentSemanticFlags.DestinationHoldsVariableLengthString"/>).
/// RD-VBA runs the statement, as the specification allows: a warning, because the code does not compile in MS-VBA. Nothing is reported for a procedure that
/// was not evaluated.
/// </remarks>
internal sealed class LSetOfARecordWithAStringAnalyzer : IModuleAnalyzer
{
    private const FixedAssignmentSemanticFlags EitherSide =
        FixedAssignmentSemanticFlags.SourceHoldsVariableLengthString | FixedAssignmentSemanticFlags.DestinationHoldsVariableLengthString;

    public AnalysisPhase Phase => AnalysisPhase.Runtime;

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Procedures ?? [])
            .SelectMany(procedure => procedure.Runtime?.Statements ?? [])
            .Where(statement => statement.FixedAssignmentFlags is { } flags
                && flags.HasFlag(FixedAssignmentSemanticFlags.UserDefinedTypeCopy) && (flags & EitherSide) != 0)
            .DistinctBy(statement => statement.Node)
            .Select(statement => new AnalyzerFinding(
                RDCoreDiagnosticId.LSetOfARecordWithAString.ToCode(),
                statement.Location.Range,
                DiagnosticSeverity.Warning,
                RDCoreDiagnosticsResources.LSetOfARecordWithAString_Message));
}
