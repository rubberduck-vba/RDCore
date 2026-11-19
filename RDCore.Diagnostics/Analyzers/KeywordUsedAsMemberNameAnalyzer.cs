using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01018</c>: an Enum member or a member of a user-defined type named with a <c>reserved-identifier</c> (<strong>MS-VBAL §3.3.5.2</strong>), in brackets or
/// not: <c>Next As Long</c>, <c>[Select] = 1</c>.
/// </summary>
/// <remarks>
/// A member of a user-defined type may have such a name (<strong>MS-VBAL §5.2.3.3</strong>), and an Enum member may have it in brackets
/// (<strong>MS-VBAL §3.3.5.3</strong>). A name that <see cref="DeclarationNameDivergenceAnalyzer"/> reports is left to it.
/// </remarks>
internal sealed class KeywordUsedAsMemberNameAnalyzer : SyntaxTreeAnalyzer
{
    public override IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.KeywordUsedAsMemberName.ToCode()];

    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
    {
        foreach (var node in module.Descendants())
        {
            var (name, range, enumMember) = node switch
            {
                ConstantDeclarationNode { ConstKind: ConstKind.EnumMember } constant => (constant.Name, constant.NameRange, true),
                MemberDeclarationNode { MemberKind: MemberKind.UserDefinedTypeField } member => (member.Name, member.NameRange, false),
                _ => (string.Empty, (SourceRange?)null, false),
            };

            if (range is not { } written || !VBIdentifier.IsReserved(name)
                || !DeclarationNameDivergenceAnalyzer.IsBracketed(name, written) && DeclarationNameDivergenceAnalyzer.ReportsMember(name, enumMember))
            {
                continue;
            }

            yield return new AnalyzerFinding(
                RDCoreDiagnosticId.KeywordUsedAsMemberName.ToCode(), written, DiagnosticSeverity.Information, Say(RDCoreDiagnosticsResources.KeywordUsedAsMemberName_Message, name))
            {
                Fixes = new DiagnosticFixes { Rename = new RenameFix() },
            };
        }
    }
}
