using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11037</c>: a declared name with a non-breaking space (U+00A0) in it, which no identifier of <strong>MS-VBAL §3.3.5</strong> has, and MS-VBA compiles:
/// the name looks like two words, and the name typed with a space is another one.
/// </summary>
/// <remarks>
/// Code copied from a web page often has such spaces. The names are those of the module's variables, constants, procedures, parameters and line labels.
/// </remarks>
internal sealed class NonBreakingSpaceInNameAnalyzer : SyntaxTreeAnalyzer
{
    private const char NonBreakingSpace = ' ';

    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
    {
        foreach (var node in module.Descendants())
        {
            var (name, range) = node switch
            {
                VariableDeclarationNode variable => (variable.Name, variable.NameRange ?? variable.SourceLocation.Range),
                ConstantDeclarationNode constant => (constant.Name, constant.NameRange ?? constant.Location.Range),
                MemberDeclarationNode member => (member.Name, member.NameRange ?? member.SourceLocation.Range),
                ParameterDeclarationNode parameter => (parameter.Name, parameter.NameRange ?? parameter.Location.Range),
                LineLabelNode label => (label.Name, label.SourceLocation.Range),
                _ => (null, default(SourceRange)),
            };

            if (name is not null && name.Contains(NonBreakingSpace))
            {
                yield return new AnalyzerFinding(
                    RDCoreDiagnosticId.NonBreakingSpaceInName.ToCode(),
                    range,
                    DiagnosticSeverity.Warning,
                    Say(RDCoreDiagnosticsResources.NonBreakingSpaceInName_Message, name.Replace(NonBreakingSpace, ' ')));
            }
        }
    }
}
