using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00105</c>: a procedure, property, event, enum or user-defined type that states no access modifier, and is <c>Public</c> (<strong>MS-VBAL §5.3.1.1</strong>).
/// </summary>
/// <remarks>
/// A module-level variable is not among them: <c>Dim</c> there declares a <c>Private</c> one, which <see cref="ModuleScopeDimAnalyzer"/> reports. The fix
/// writes <c>Public</c> where the declaration starts, before <c>Static</c> too: the member is as accessible as it was.
/// </remarks>
internal sealed class ImplicitPublicMemberAnalyzer : SyntaxTreeAnalyzer
{
    public override IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ImplicitPublicMember.ToCode()];

    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
        => module.Children
            .OfType<MemberDeclarationNode>()
            .Where(member => member is { AccessModifier: AccessModifier.Implicit, MemberKind: not MemberKind.ModuleField })
            .Select(member => new AnalyzerFinding(
                RDCoreDiagnosticId.ImplicitPublicMember.ToCode(),
                member.NameRange ?? member.SourceLocation.Range,
                DiagnosticSeverity.Information,
                Say(RDCoreDiagnosticsResources.ImplicitPublicMember_Message, member.Name))
            {
                Fixes = new DiagnosticFixes
                {
                    Edit = SyntaxEdits.Fix(Say(RDCoreDiagnosticsResources.ImplicitPublicMember_Fix, member.Name), SyntaxEdits.Insert(member.SourceLocation.Range.Start, "Public ")),
                },
            });
}
