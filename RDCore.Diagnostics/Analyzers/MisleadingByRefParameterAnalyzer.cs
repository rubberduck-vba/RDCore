using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00205</c>: <c>ByRef</c> written on the value parameter of a property <c>Let</c> or <c>Set</c>, which is always passed by value (<strong>MS-VBAL §5.3.1.7</strong>).
/// </summary>
/// <remarks>
/// The fix writes <c>ByVal</c> in place of the <c>ByRef</c> the parameter starts with (it cannot be <c>Optional</c>): it is passed as it was.
/// </remarks>
internal sealed class MisleadingByRefParameterAnalyzer : SyntaxTreeAnalyzer
{
    public override IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.MisleadingByRefParameter.ToCode()];

    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
        => module.Descendants()
            .OfType<ParameterDeclarationNode>()
            .Where(parameter => parameter.IsByRefIgnored)
            .Select(parameter => new AnalyzerFinding(
                RDCoreDiagnosticId.MisleadingByRefParameter.ToCode(),
                parameter.Location.Range,
                DiagnosticSeverity.Information,
                Say(RDCoreDiagnosticsResources.MisleadingByRefParameter_Message, parameter.Name))
            {
                Fixes = parameter.IsOptional
                    ? null
                    : new DiagnosticFixes
                    {
                        Edits = [SyntaxEdits.Fix(Say(RDCoreDiagnosticsResources.MisleadingByRefParameter_Fix, parameter.Name),
                            SyntaxEdits.Replace(SyntaxEdits.Following(parameter.Location.Range.Start, "ByRef".Length), "ByVal"))],
                    },
            });
}
