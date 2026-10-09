using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00205</c>: <c>ByRef</c> written on the value parameter of a property <c>Let</c> or <c>Set</c>, which is always passed by value (<strong>MS-VBAL §5.3.1.7</strong>).
/// </summary>
internal sealed class MisleadingByRefParameterAnalyzer : SyntaxTreeAnalyzer
{
    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
        => module.Descendants()
            .OfType<ParameterDeclarationNode>()
            .Where(parameter => parameter.IsByRefIgnored)
            .Select(parameter => new AnalyzerFinding(
                RDCoreDiagnosticId.MisleadingByRefParameter,
                parameter.Location.Range,
                DiagnosticSeverity.Information,
                Say(RDCoreDiagnosticsResources.MisleadingByRefParameter_Message, parameter.Name)));
}
