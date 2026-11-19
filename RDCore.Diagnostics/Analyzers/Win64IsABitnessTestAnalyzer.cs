using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Runtime;
using RDCore.SDK.Server.Configuration;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01019</c>: a directive reads <c>Win64</c>, which is <c>1</c> on a 64-bit Mac too, as in Excel for Mac
/// (<see cref="PredefinedConditionalConstants.Win64"/>). Most such tests are about the bitness of the environment, which <c>Is64BitEnv</c> tests.
/// </summary>
/// <remarks>
/// Not reported when the platform sets <c>Win64</c> on Windows only (<see cref="SdkEnvironmentOptions.Win64OnlySetOnWindows"/>), nor in a module that
/// defines its own <c>Win64</c>. The two fixes replace the name: with <c>Is64BitEnv</c>, and with <c>Win32 And Is64BitEnv</c> for a <c>Win64</c> that
/// means 64-bit Windows, in parentheses where the name is an operand.
/// </remarks>
internal sealed class Win64IsABitnessTestAnalyzer(IOptions<SdkAppOptions> options) : IModuleAnalyzer
{
    private const string Windows64 = "Win32 And " + PredefinedConditionalConstants.Is64BitEnv;

    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.Win64IsABitnessTest.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        var trivia = context.ParseResult.PrecompilerTrivia;
        if (options.Value.Environment.Win64OnlySetOnWindows || trivia.IsDefaultOrEmpty
            || ConditionalConstantReads.DeclaredBy(trivia).Contains(PredefinedConditionalConstants.Win64))
        {
            return [];
        }

        return ConditionalConstantReads.Of(trivia)
            .Where(read => string.Equals(read.Name.Name, PredefinedConditionalConstants.Win64, StringComparison.OrdinalIgnoreCase))
            .Select(Finding);
    }

    private static AnalyzerFinding Finding(ConditionalConstantRead read)
    {
        var range = read.Name.Location.Range;
        return new AnalyzerFinding(
            RDCoreDiagnosticId.Win64IsABitnessTest.ToCode(),
            range,
            DiagnosticSeverity.Hint,
            AnalyzerMessages.Format(RDCoreDiagnosticsResources.Win64IsABitnessTest_Message, read.Name.Name))
        {
            Fixes = new DiagnosticFixes
            {
                Edits =
                [
                    // each changes which branch is compiled: the first on 64-bit Linux, the second on a 64-bit Mac.
                    SyntaxEdits.Fix(RDCoreDiagnosticsResources.Win64IsABitnessTest_Fix, SyntaxEdits.Replace(range, PredefinedConditionalConstants.Is64BitEnv))
                        with { ChangesBehavior = true },
                    SyntaxEdits.Fix(RDCoreDiagnosticsResources.Win64IsABitnessTest_WindowsFix,
                        SyntaxEdits.Replace(range, read.IsWholeExpression ? Windows64 : $"({Windows64})")) with { ChangesBehavior = true },
                ],
            },
        };
    }
}
