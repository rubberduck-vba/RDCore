using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00310</c>: an argument written <c>ByVal</c> for a parameter that is <c>ByVal</c> already, where the keyword changes nothing.
/// </summary>
/// <remarks>
/// MS-VBA compiles it and MS-VBAL §5.6.13.1 refuses it. The analyzer reads the <c>ByVal</c> arguments off the syntax tree, and what the procedure called
/// declares of each one's parameter off the argument facts: a parameter declared <c>ByVal</c> (or a library's parameter taken by value), which is not
/// a <c>ParamArray</c>, and not declared <c>As Any</c> (where <c>ByVal</c> passes a value for an address). A <c>ByVal</c> that is a compile error is
/// not reported.
/// </remarks>
internal sealed class RedundantByValArgumentAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.ParseResult.SyntaxTree is not { } module || context.Semantics is not { } semantics)
        {
            yield break;
        }

        var byVals = module.Descendants().OfType<ByValArgumentExpressionNode>().ToDictionary(argument => argument.Identity);
        foreach (var procedure in semantics.Procedures)
        {
            var errors = procedure.CompileErrors.Select(error => error.Location.Range)
                .Concat(procedure.Expressions.Where(fact => fact.Error is not null).Select(fact => fact.Error!.Location.Range))
                .ToHashSet();
            foreach (var argument in procedure.Arguments ?? [])
            {
                if (!byVals.TryGetValue(argument.Argument, out var byVal) || errors.Contains(byVal.Location.Range)
                    || argument is not { Mechanism: ParameterKind.ExplicitByVal, IsParamArray: false }
                    || string.Equals(argument.ParameterType, VBTypeNames.VBUnknown, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return new AnalyzerFinding(RDCoreDiagnosticId.RedundantByValArgument.ToCode(), byVal.Location.Range, DiagnosticSeverity.Hint,
                    RDCoreDiagnosticsResources.RedundantByValArgument_Message);
            }
        }
    }
}
