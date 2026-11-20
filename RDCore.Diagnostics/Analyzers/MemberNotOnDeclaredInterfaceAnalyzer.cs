using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11051</c>: a member access on an object declared as a class of a referenced library that the class does not declare (<strong>MS-VBAL
/// §5.6.12</strong>): the library's class is extensible, so the code compiles, and the member is looked up on the object when the code runs.
/// </summary>
/// <remarks>
/// The host states the access <c>MemberAccessOperationSemanticFlags.NotOnDeclaredInterface</c>. A class of the VBA library, whose objects have no other
/// members, is <c>RDC11010</c>'s: its access has no candidates. An access whose run-time error the host states (an object that is
/// <c>Nothing</c>) is the run-time error's. The class is the declared type of the object's expression, or of the <c>With</c> block's.
/// </remarks>
internal sealed class MemberNotOnDeclaredInterfaceAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.MemberNotOnDeclaredInterface.ToCode()];

    public AnalysisPhase Phase => AnalysisPhase.Runtime;

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context is not { ParseResult.SyntaxTree: { } module, Semantics: { } semantics })
        {
            yield break;
        }

        const MemberAccessOperationSemanticFlags notDeclared
            = MemberAccessOperationSemanticFlags.LateBound | MemberAccessOperationSemanticFlags.NotOnDeclaredInterface;
        var accesses = semantics.Procedures
            .SelectMany(procedure => (procedure.Runtime?.Operations ?? []).Select(access => (procedure, access)))
            .Where(pair => pair.access is { Error: null, Member: not null, MemberAccessOpSemanticFlags: { } flags } && (flags & notDeclared) == notDeclared
                && pair.access.LateBoundCandidates is not { IsEmpty: true })
            .DistinctBy(pair => pair.access.Location.Range)
            .ToArray();
        if (accesses.Length == 0)
        {
            yield break;
        }

        var nodes = module.Descendants().OfType<ExpressionNode>().GroupBy(node => node.Identity).ToDictionary(group => group.Key, group => group.First());
        foreach (var (procedure, access) in accesses)
        {
            if (ClassOf(module, procedure, nodes, access) is not { } declared)
            {
                continue;
            }

            yield return new AnalyzerFinding(
                RDCoreDiagnosticId.MemberNotOnDeclaredInterface.ToCode(),
                access.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.MemberNotOnDeclaredInterface_Message, access.Member!, declared));
        }
    }

    // the declared type of the object the member is accessed on: before the '.', or the object of the enclosing With block.
    private static string? ClassOf(ModuleNode module, ProcedureSemanticsDto procedure, Dictionary<SyntaxNodeId, ExpressionNode> nodes, OperatorFactDto access)
    {
        var node = nodes.TryGetValue(access.Node, out var found) ? found : null;
        if ((node is IndexExpressionNode { Callee: var callee } ? callee : node) is not MemberAccessExpressionNode memberAccess)
        {
            return null;
        }

        // the innermost With block whose body the access is in, for an access the host states is on the With block's object.
        var range = memberAccess.Location.Range;
        var owner = access.MemberAccessOpSemanticFlags is { } flags && flags.HasFlag(MemberAccessOperationSemanticFlags.WithBlockVariable)
            ? module.Descendants().OfType<WithStatementNode>()
                .Where(with => with.WithExpression.Location.Range.End <= range.Start && range.End <= with.SourceLocation.Range.End)
                .OrderByDescending(with => with.SourceLocation.Range.Start)
                .Select(with => with.WithExpression)
                .FirstOrDefault()
            : memberAccess.Owner;
        return owner is null ? null : procedure.Expressions.FirstOrDefault(fact => fact.Node == owner.Identity)?.DeclaredType;
    }
}
