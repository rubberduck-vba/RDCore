using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11025</c>: an array given a second empty argument list, <c>f()()</c>, which <strong>MS-VBAL §5.6.13.1</strong> makes invalid (an empty argument
/// list was already specified for it) and <strong>MS-VBA</strong> compiles, as the array.
/// </summary>
/// <remarks>
/// RD-VBA does as MS-VBA does (recorded in Excel: oracle\s53 E07, E08, F07, F08): <c>UBound(f()())</c> compiles, and <c>f()()</c> raises run-time error 9
/// (Subscript out of range) whenever it runs. A warning, because the specification refuses it, and it can never run. The analyzer finds the two empty lists in
/// the syntax tree, and reads the declared type of what the first one is given to.
/// </remarks>
internal sealed class ArrayGivenASecondEmptyArgumentListAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.ParseResult.SyntaxTree is not { } module || context.Semantics is not { } semantics)
        {
            return [];
        }

        var facts = semantics.Procedures.SelectMany(procedure => procedure.Expressions).DistinctBy(fact => fact.Node).ToDictionary(fact => fact.Node);
        return module.Descendants().OfType<IndexExpressionNode>()
            .Where(index => IndexExpressionNode.IsEmptyArgumentList(index.Arguments)
                && index.Callee is IndexExpressionNode given && IndexExpressionNode.IsEmptyArgumentList(given.Arguments)
                && facts.TryGetValue(given.Identity, out var callee) && callee.DeclaredType == VBTypeNames.VBArray
                && facts.TryGetValue(index.Identity, out var fact) && !fact.Flags.HasFlag(ValueExpressionSemanticFlags.ProcedureCall))
            .Select(index => new AnalyzerFinding(
                RDCoreDiagnosticId.ArrayGivenASecondEmptyArgumentList.ToCode(),
                facts[index.Identity].Location.Range,
                DiagnosticSeverity.Warning,
                RDCoreDiagnosticsResources.ArrayGivenASecondEmptyArgumentList_Message));
    }
}
