using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11035</c>: a <c>Set</c> to <c>Nothing</c> of the variable a <c>For Each</c> walks, inside that loop (<c>Set c = Nothing</c> in
/// <c>For Each v In c</c>), which RD-VBA leaves as it is while the loop runs.
/// </summary>
/// <remarks>
/// <strong>MS-VBAL §5.4.2.4</strong> leaves the enumeration implementation-defined. The loop holds the object it walks, which its own loop does not destroy:
/// the loop reaches every member, and the variable still refers to the object after it. MS-VBA lets go of a <c>Collection</c> there, which is destroyed with
/// the members the loop has not reached, and the loop ends (observed in Excel). A warning, because a program that relies on MS-VBA's behavior runs
/// differently. The analyzer finds the <c>Set</c> in the syntax tree of the loop, and reports it where the static pass evaluated its target.
/// </remarks>
internal sealed class CollectionSetToNothingInItsOwnLoopAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.ParseResult.SyntaxTree is not { } module || context.Semantics is not { } semantics)
        {
            return [];
        }

        var facts = semantics.Procedures.SelectMany(procedure => procedure.Expressions).DistinctBy(fact => fact.Node).ToDictionary(fact => fact.Node);
        return module.Descendants().OfType<ForEachStatementNode>()
            .SelectMany(ForEachCollectionStaticSemantics.SetsToNothingOfItsCollection)
            .Select(set => (SimpleNameExpressionNode)set.Target)
            .DistinctBy(target => target.Identity)
            .Where(target => facts.ContainsKey(target.Identity))
            .Select(target => new AnalyzerFinding(
                RDCoreDiagnosticId.CollectionSetToNothingInItsOwnLoop.ToCode(),
                facts[target.Identity].Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.CollectionSetToNothingInItsOwnLoop_Message, target.IdentifierName)));
    }
}
