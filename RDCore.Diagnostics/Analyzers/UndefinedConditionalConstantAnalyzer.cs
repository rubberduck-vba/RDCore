using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01020</c>: a directive reads a conditional-compilation constant that nothing defines, which is <c>0</c>: a condition that tests it is
/// <c>False</c>, as in MS-VBA, and not an error.
/// </summary>
/// <remarks>
/// The host states which names are undefined (<see cref="ModuleSemanticsDto.UndefinedConditionalConstants"/>), the project's constants and the command
/// line's considered. A retired predefined constant (<see cref="PredefinedConditionalConstants.Retired"/>) names the condition that tests the same thing,
/// and its fix writes it, in parentheses where the name is an operand.
/// </remarks>
internal sealed class UndefinedConditionalConstantAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.UndefinedConditionalConstant.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        var trivia = context.ParseResult.PrecompilerTrivia;
        if (context.Semantics?.UndefinedConditionalConstants is not { IsDefaultOrEmpty: false } undefined || trivia.IsDefaultOrEmpty)
        {
            return [];
        }

        var names = undefined.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ConditionalConstantReads.Of(trivia).Where(read => names.Contains(read.Name.Name)).Select(Finding);
    }

    private static AnalyzerFinding Finding(ConditionalConstantRead read)
    {
        var code = RDCoreDiagnosticId.UndefinedConditionalConstant.ToCode();
        var range = read.Name.Location.Range;
        if (!PredefinedConditionalConstants.Retired.TryGetValue(read.Name.Name, out var replacement))
        {
            return new AnalyzerFinding(code, range, DiagnosticSeverity.Hint,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.UndefinedConditionalConstant_Message, read.Name.Name));
        }

        return new AnalyzerFinding(code, range, DiagnosticSeverity.Hint,
            AnalyzerMessages.Format(RDCoreDiagnosticsResources.UndefinedConditionalConstant_Retired_Message, read.Name.Name, replacement))
        {
            Fixes = new DiagnosticFixes
            {
                Edits =
                [
                    // the retired constant is 0 everywhere, and its replacement is not: the branch it guards is compiled where the replacement is true.
                    SyntaxEdits.Fix(AnalyzerMessages.Format(RDCoreDiagnosticsResources.UndefinedConditionalConstant_Fix, replacement),
                        SyntaxEdits.Replace(range, read.IsWholeExpression ? replacement : $"({replacement})")) with { ChangesBehavior = true },
                ],
            },
        };
    }
}
