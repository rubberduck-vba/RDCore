using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00203</c>: a parameter whose own declaration is split over several lines with a line continuation (<strong>MS-VBAL §5.3.1.5</strong>).
/// </summary>
/// <remarks>
/// A parameter list with a parameter on each line is not reported: it is the declaration of one parameter that is broken, not the list.
/// </remarks>
internal sealed class MultilineParameterAnalyzer : SyntaxTreeAnalyzer
{
    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
        => module.Descendants()
            .OfType<ParameterDeclarationNode>()
            .Where(parameter => parameter.Location.Range.Start.Line != parameter.Location.Range.End.Line)
            .Select(parameter => new AnalyzerFinding(
                RDCoreDiagnosticId.MultilineParameterDeclaration,
                parameter.Location.Range,
                DiagnosticSeverity.Information,
                Say(RDCoreDiagnosticsResources.MultilineParameterDeclaration_Message, parameter.Name)));
}
