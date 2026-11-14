using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11016</c>: the <c>Empty</c> or the <c>Null</c> literal is Let-assigned to an array - a variable, or the result of a function, declared as one -
/// which raises run-time error 13 (Type mismatch) whenever it runs.
/// </summary>
/// <remarks>
/// The <c>Empty</c> and <c>Null</c> literals have a declared type of <c>Variant</c> (<strong>MS-VBAL §3.3.5.2</strong>), and a <c>Variant</c> is valid to
/// Let-coerce to an array (<strong>MS-VBAL §5.5.1.1</strong>): the code compiles, in MS-VBA as in RD-VBA. The value each holds is no array, and the assignment
/// raises 13 when it runs (recorded in Excel). The analyzer finds the <c>Let</c> in the syntax tree, whose target's <c>ExpressionFactDto</c> is an array
/// and whose value's is <c>Empty</c> or <c>Null</c>; and, by its <c>ArgumentFact</c>, an argument of either given to a parameter declared as an array, which
/// takes it by Let-coercion (<strong>MS-VBAL §5.3.1.11</strong>). The declared types alone say it, so it holds of every path, whether or not the run-time
/// facts reach the statement.
/// </remarks>
internal sealed class EmptyLetToArrayAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.Semantics is not { } semantics)
        {
            return [];
        }

        var facts = semantics.Procedures.SelectMany(procedure => procedure.Expressions).DistinctBy(fact => fact.Node).ToDictionary(fact => fact.Node);
        var assigned = (context.ParseResult.SyntaxTree?.Descendants() ?? []).OfType<AssignmentStatementNode>()
            .Where(assignment => assignment.Kind is AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet
                && facts.TryGetValue(assignment.Target.Identity, out var target) && target.IsArray)
            .Select(assignment => assignment.Value.Identity);

        // an argument given to a parameter declared as an array, other than a ParamArray or one of a Declare.
        var given = semantics.Procedures.SelectMany(procedure => procedure.Arguments ?? [])
            .Where(argument => argument is { ParameterType: VBTypeNames.VBArray, IsParamArray: false, IsExternal: false })
            .Select(argument => argument.Argument);

        return assigned.Concat(given).Distinct()
            .Select(node => facts.TryGetValue(node, out var value) ? value : null)
            .OfType<ExpressionFactDto>()
            .Where(fact => fact.DeclaredType is VBTypeNames.VBEmpty or VBTypeNames.VBNull)
            .Select(fact => new AnalyzerFinding(
                RDCoreDiagnosticId.EmptyLetToArray.ToCode(),
                fact.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.EmptyLetToArray_Message, fact.DeclaredType ?? "Empty")));
    }
}
