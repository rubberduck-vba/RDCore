using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01017</c>: a public property with a <c>Property Let</c> or a <c>Property Set</c> and no <c>Property Get</c> (<strong>MS-VBAL §5.3.1.7</strong>): code
/// that uses the module can assign the property, and never read it.
/// </summary>
/// <remarks>
/// A property with both a <c>Let</c> and a <c>Set</c> is reported once, at the first of them. A <c>Private</c> or <c>Friend</c> property is not reported.
/// </remarks>
internal sealed class WriteOnlyPropertyAnalyzer : SyntaxTreeAnalyzer
{
    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
    {
        var members = module.Children.OfType<MemberDeclarationNode>().ToList();
        var readable = members.Where(member => member.MemberKind is MemberKind.PropertyGet).Select(member => member.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return members
            .Where(member => member is { MemberKind: MemberKind.PropertyLet or MemberKind.PropertySet, AccessModifier: AccessModifier.Public or AccessModifier.Implicit or AccessModifier.Global }
                && !readable.Contains(member.Name))
            .DistinctBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
            .Select(member => new AnalyzerFinding(
                RDCoreDiagnosticId.WriteOnlyProperty.ToCode(),
                member.NameRange ?? member.SourceLocation.Range,
                DiagnosticSeverity.Information,
                Say(RDCoreDiagnosticsResources.WriteOnlyProperty_Message, member.Name)));
    }
}
