using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00104</c>: a parameter that states neither <c>ByRef</c> nor <c>ByVal</c>, and is passed by reference (<strong>MS-VBAL §5.3.1.5</strong>).
/// </summary>
/// <remarks>
/// A <c>ParamArray</c> cannot state either, and the value parameter of a property <c>Let</c> or <c>Set</c> is always passed by value, so neither is reported.
/// </remarks>
internal sealed class ImplicitByRefModifierAnalyzer : SyntaxTreeAnalyzer
{
    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
        => module.Descendants()
            .OfType<ParameterDeclarationNode>()
            .Where(parameter => parameter is { ParameterKind: ParameterKind.ImplicitByRef, IsParamArray: false })
            .Select(parameter => new AnalyzerFinding(
                RDCoreDiagnosticId.ImplicitByRefModifier,
                parameter.Location.Range,
                DiagnosticSeverity.Information,
                Say(RDCoreDiagnosticsResources.ImplicitByRefModifier_Message, parameter.Name)));
}
