using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Semantics.Flags;
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
/// differently. The host states it of the <c>Set</c>'s target (<see cref="ValueExpressionSemanticFlags.CollectionSetToNothingInItsOwnLoop"/>).
/// </remarks>
internal sealed class CollectionSetToNothingInItsOwnLoopAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.ParseResult.SyntaxTree is not { } module || context.Semantics is not { } semantics)
        {
            yield break;
        }

        var names = module.Descendants().OfType<SimpleNameExpressionNode>().ToDictionary(name => name.Identity);
        foreach (var fact in semantics.Procedures.SelectMany(procedure => procedure.Expressions)
            .Where(fact => fact.Flags.HasFlag(ValueExpressionSemanticFlags.CollectionSetToNothingInItsOwnLoop)))
        {
            if (names.TryGetValue(fact.Node, out var name))
            {
                yield return new AnalyzerFinding(
                    RDCoreDiagnosticId.CollectionSetToNothingInItsOwnLoop.ToCode(),
                    fact.Location.Range,
                    DiagnosticSeverity.Warning,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.CollectionSetToNothingInItsOwnLoop_Message, name.IdentifierName));
            }
        }
    }
}
