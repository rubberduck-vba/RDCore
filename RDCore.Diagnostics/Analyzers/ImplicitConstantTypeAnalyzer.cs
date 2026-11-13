using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00117</c>: a constant declared with no <c>As</c> clause and no type-declaration character (<strong>MS-VBAL §5.2.3.1</strong>): it has a declared type
/// all the same, which the reader works out from its value.
/// </summary>
internal sealed class ImplicitConstantTypeAnalyzer : SyntaxTreeAnalyzer
{
    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
        => module.Descendants()
            .OfType<ConstantDeclarationNode>()
            .Where(constant => !ImplicitTypes.IsTypeStated(constant, constant.TypeHint))
            .Select(constant => new AnalyzerFinding(
                RDCoreDiagnosticId.ImplicitConstantType.ToCode(),
                constant.NameRange ?? constant.Location.Range,
                DiagnosticSeverity.Information,
                Say(RDCoreDiagnosticsResources.ImplicitConstantType_Message, constant.Name)));
}
