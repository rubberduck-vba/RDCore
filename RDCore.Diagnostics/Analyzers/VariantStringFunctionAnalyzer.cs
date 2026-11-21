using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00118</c>: a function of a library that returns a <c>Variant</c>, called where its <c>$</c> form is the same function returning a <c>String</c>
/// (<strong>MS-VBAL §6.1.2</strong>: <c>Left</c> and <c>Left$</c>, <c>Hex</c> and <c>Hex$</c>).
/// </summary>
/// <remarks>
/// The host states the form of the function the name is bound to (<see cref="RDCore.SDK.Platform.Protocol.ExpressionFactDto.StringForm"/>). The fix writes the
/// <c>$</c> after the name.
/// </remarks>
internal sealed class VariantStringFunctionAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.VariantStringFunction.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context is not { Semantics: { } semantics })
        {
            return [];
        }

        return semantics.Procedures.SelectMany(procedure => procedure.Expressions)
            .Where(fact => fact.StringForm is not null)
            .DistinctBy(fact => fact.Location.Range)
            .OrderBy(fact => fact.Location.Range.Start)
            .Select(fact =>
            {
                var name = fact.StringForm![..^1];
                return new AnalyzerFinding(
                    RDCoreDiagnosticId.VariantStringFunction.ToCode(),
                    fact.Location.Range,
                    DiagnosticSeverity.Information,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.VariantStringFunction_Message, name, fact.StringForm))
                {
                    Fixes = new DiagnosticFixes
                    {
                        Edits =
                        [
                            SyntaxEdits.Fix(
                                AnalyzerMessages.Format(RDCoreDiagnosticsResources.VariantStringFunction_Fix, fact.StringForm),
                                SyntaxEdits.Insert(fact.Location.Range.End, "$")),
                        ],
                    },
                };
            });
    }
}
