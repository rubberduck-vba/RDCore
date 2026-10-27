using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00207</c>: <c>IsMissing</c> of a parameter that can never be missing (<strong>MS-VBAL §6.1.2.7.1.6</strong>), so that it is always <c>False</c>.
/// </summary>
/// <remarks>
/// Only a <c>Variant</c> parameter can be missing: an omitted argument leaves a parameter of any other type its type's default value, and gives an
/// <c>Optional</c> <c>Variant</c> its default value when it has one. An <c>IsMissing</c> of such a parameter looks like a test and never is one, which MS-VBA
/// compiles without a word (recorded in Excel: oracle\ismissing I01-I09). The host states it from the parameter's declaration
/// (<see cref="ValueExpressionSemanticFlags.IsMissingAlwaysFalse"/>); the analyzer reads the argument's declared type only to say which of the two it is.
/// </remarks>
internal sealed class IsMissingAlwaysFalseAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.ParseResult.SyntaxTree is not { } module || context.Semantics is not { } semantics)
        {
            yield break;
        }

        var calls = module.Descendants().OfType<IndexExpressionNode>().ToDictionary(call => call.Identity);
        foreach (var procedure in semantics.Procedures)
        {
            foreach (var fact in procedure.Expressions.Where(fact => fact.Flags.HasFlag(ValueExpressionSemanticFlags.IsMissingAlwaysFalse)))
            {
                if (!calls.TryGetValue(fact.Node, out var call) || call.Arguments is not [SimpleNameExpressionNode argument])
                {
                    continue;
                }

                var argumentType = procedure.Expressions.FirstOrDefault(candidate => candidate.Node == argument.Identity)?.DeclaredType;
                var reason = string.Equals(argumentType, "Variant", StringComparison.OrdinalIgnoreCase)
                    ? RDCoreDiagnosticsResources.IsMissingAlwaysFalse_HasADefault
                    : RDCoreDiagnosticsResources.IsMissingAlwaysFalse_NotAVariant;
                yield return new AnalyzerFinding(
                    RDCoreDiagnosticId.IsMissingAlwaysFalse,
                    fact.Location.Range,
                    DiagnosticSeverity.Warning,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.IsMissingAlwaysFalse_Message, argument.IdentifierName, reason));
            }
        }
    }
}
