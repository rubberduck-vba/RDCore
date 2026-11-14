using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00107</c>: a function or a property getter that is declared without a return type, and returns a <c>Variant</c> (<strong>MS-VBAL §5.3.1.4</strong>).
/// </summary>
internal sealed class ImplicitVariantReturnTypeAnalyzer : SyntaxTreeAnalyzer
{
    public override IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ImplicitVariantReturnType.ToCode()];

    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
        => module.Children
            .OfType<MemberDeclarationNode>()
            .Where(member => member.MemberKind is MemberKind.Function or MemberKind.PropertyGet or MemberKind.ExternalFunction)
            .Where(member => !ImplicitTypes.IsTypeStated(member, member.TypeHint) && ImplicitTypes.IsVariant(module, member.Name))
            .Select(member => new AnalyzerFinding(
                RDCoreDiagnosticId.ImplicitVariantReturnType.ToCode(),
                member.NameRange ?? member.SourceLocation.Range,
                DiagnosticSeverity.Information,
                Say(RDCoreDiagnosticsResources.ImplicitVariantReturnType_Message, member.Name)));
}
