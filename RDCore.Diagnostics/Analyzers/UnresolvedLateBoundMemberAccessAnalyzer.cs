using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11010</c>: a late-bound member access (<strong>MS-VBAL §5.6.12</strong>) that no member can answer when it runs: no class the object can be of has a
/// member of the name that the code of the access can reach (<strong>MS-VBAL §5.6.3</strong>, <strong>§5.3.1.1</strong>), so the access raises run-time
/// error 438.
/// </summary>
/// <remarks>
/// The host states the members the access may reach (<c>OperatorFactDto.LateBoundCandidates</c>): none, when the classes the object can be of are all known
/// and none has such a member, or when a class of the VBA library, whose objects have no other members, does not declare it; not known, for an object given
/// as an argument, returned by a function or read from a collection, which is not reported. An access whose run-time error the host already states is the
/// run-time error's.
/// </remarks>
internal sealed class UnresolvedLateBoundMemberAccessAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.UnresolvedLateBoundMemberAccess.ToCode()];

    public AnalysisPhase Phase => AnalysisPhase.Runtime;

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Procedures ?? [])
            .SelectMany(procedure => procedure.Runtime?.Operations ?? [])
            .Where(access => access is { Error: null, Member: not null, LateBoundCandidates: { IsEmpty: true } }
                && access.MemberAccessOpSemanticFlags is { } flags && flags.HasFlag(MemberAccessOperationSemanticFlags.LateBound))
            .DistinctBy(access => access.Location.Range)
            .Select(access => new AnalyzerFinding(
                RDCoreDiagnosticId.UnresolvedLateBoundMemberAccess.ToCode(),
                access.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.UnresolvedLateBoundMemberAccess_Message, access.Member!)));
}
