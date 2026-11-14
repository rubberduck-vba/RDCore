using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00202</c>: a <c>Dim</c> statement at the module level, which declares <c>Private</c> variables (<strong>MS-VBAL §5.2.3</strong>).
/// </summary>
/// <remarks>
/// A module-level variable that states no access modifier can only have been declared with <c>Dim</c>. One finding is made for each statement, however many variables it declares.
/// The fix writes <c>Private</c> in place of the <c>Dim</c> the statement starts with: its variables are as accessible as they were.
/// <para>
/// A statement of several names is this finding's and not <c>RDC00204</c>'s, since its one keyword declares every variable of the list
/// (<strong>MS-VBAL §5.2.3.1</strong>). The fix then also declares each name on a line of its own, as <c>RDC00204</c>'s fix does:
/// <c>Dim a, b As Long</c> is <c>Private a</c> and <c>Private b As Long</c>, and nothing is left for <c>RDC00204</c> to find. It is not offered for a
/// statement of several names that declares a <c>WithEvents</c> variable, which <c>RDC00204</c>'s fix does not split either: written <c>Private</c>
/// and not split, the statement would be <c>RDC00204</c>'s.
/// </para>
/// </remarks>
internal sealed class ModuleScopeDimAnalyzer : SyntaxTreeAnalyzer
{
    public override IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ModuleScopeDimDeclaration.ToCode()];

    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
        => DeclarationStatements.In(module.Children)
            .Where(IsDim)
            .Select(statement => new AnalyzerFinding(
                RDCoreDiagnosticId.ModuleScopeDimDeclaration.ToCode(),
                DeclarationStatements.RangeOf(statement),
                DiagnosticSeverity.Information,
                RDCoreDiagnosticsResources.ModuleScopeDimDeclaration_Message)
            {
                Fixes = FixOf(statement),
            });

    // Private for the Dim, and each name on a line of its own; none for a statement of several names that cannot be split, which RDC00204 would then find.
    private static DiagnosticFixes? FixOf(IReadOnlyList<SyntaxNode> statement)
    {
        var keyword = SyntaxEdits.Replace(SyntaxEdits.Following(statement[0].SourceLocation.Range.Start, "Dim".Length), "Private");
        if (statement.Count == 1)
        {
            return new DiagnosticFixes { Edit = SyntaxEdits.Fix(RDCoreDiagnosticsResources.ModuleScopeDimDeclaration_Fix, keyword) };
        }

        return MultipleDeclarationsAnalyzer.SplitEdits(statement, "Private") is { } split
            ? new DiagnosticFixes { Edit = SyntaxEdits.Fix(RDCoreDiagnosticsResources.ModuleScopeDimDeclaration_Fix, [keyword, .. split]) }
            : null;
    }

    // a statement of the module's declarations that declares variables with no access modifier: a Dim statement.
    internal static bool IsDim(IReadOnlyList<SyntaxNode> statement) => statement[0] is VariableDeclarationNode { AccessModifier: AccessModifier.Implicit };
}
