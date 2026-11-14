using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00204</c>: a <c>Dim</c>, <c>Static</c> or <c>Const</c> statement that declares several names (<strong>MS-VBAL §5.2.3.1.1</strong>, <strong>§5.4.3.1</strong>).
/// </summary>
/// <remarks>
/// Each name has a type of its own, and a clause that is written once at the end of the statement is the type of the last name only. One finding is made for each statement.
/// <para>
/// The fix declares each name on a line of its own, with the statement's keywords and at its indentation: <c>Dim a, b As Long</c> is <c>Dim a</c> and
/// <c>Dim b As Long</c>, and <c>a</c> is still the <c>Variant</c> it was. It is not offered in a single-line <c>If</c>, which a line break would end, nor
/// for a statement that declares a <c>WithEvents</c> variable.
/// </para>
/// <para>
/// A <c>Dim</c> statement at the module level is <c>RDC00202</c>'s first: its keyword declares every variable of its list (<strong>MS-VBAL §5.2.3.1</strong>),
/// and its fix makes the statement <c>Private</c> and declares each name on a line of its own, where a split first would write a <c>Dim</c> statement for
/// each name. A <c>Private</c> statement of several names is this finding's.
/// </para>
/// </remarks>
internal sealed class MultipleDeclarationsAnalyzer : SyntaxTreeAnalyzer
{
    private const string NewLine = "\r\n";

    public override IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.MultipleDeclarations.ToCode()];

    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
    {
        var inlineIfs = module.Descendants().OfType<InlineIfStatementNode>().Select(node => node.SourceLocation.Range).ToArray();
        return module.Descendants()
            .Prepend(module)
            .SelectMany(node => node.SiblingLists().Select(siblings => (Parent: node, Siblings: siblings)))
            .SelectMany(list => DeclarationStatements.In(list.Siblings).Select(statement => (list.Parent, Statement: statement)))
            .Where(each => each.Statement.Count > 1 && !(each.Parent is ModuleNode && ModuleScopeDimAnalyzer.IsDim(each.Statement)))
            .Select(each => new AnalyzerFinding(
                RDCoreDiagnosticId.MultipleDeclarations.ToCode(),
                DeclarationStatements.RangeOf(each.Statement),
                DiagnosticSeverity.Information,
                RDCoreDiagnosticsResources.MultipleDeclarations_Message)
            {
                Fixes = inlineIfs.Any(range => range.Start.CompareTo(each.Statement[0].SourceLocation.Range.Start) <= 0
                        && each.Statement[0].SourceLocation.Range.Start.CompareTo(range.End) <= 0)
                    ? null
                    : FixOf(each.Statement, atModuleLevel: each.Parent is ModuleNode),
            });
    }

    private static DiagnosticFixes? FixOf(IReadOnlyList<SyntaxNode> statement, bool atModuleLevel)
        => KeywordsOf(statement[0], atModuleLevel) is { } keywords && SplitEdits(statement, keywords) is { } edits
            ? new DiagnosticFixes { Edit = SyntaxEdits.Fix(RDCoreDiagnosticsResources.MultipleDeclarations_Fix, edits) }
            : null;

    // a line break and the given keywords in place of each comma between two names: the text between them is the comma and the blanks around it.
    // None for a statement that declares a WithEvents variable.
    internal static TextEdit[]? SplitEdits(IReadOnlyList<SyntaxNode> statement, string keywords)
    {
        if (statement.Any(node => node is VariableDeclarationNode { IsWithEvents: true }))
        {
            return null;
        }

        var indent = new string(' ', statement[0].SourceLocation.Range.Start.Character);
        return [.. statement.Zip(statement.Skip(1), (before, after) => SyntaxEdits.Replace(
            new SourceRange(before.SourceLocation.Range.End, after.SourceLocation.Range.Start), $"{NewLine}{indent}{keywords} "))];
    }

    // the keywords the statement starts with, which the tree says by its access modifier and whether it is static or constant.
    private static string? KeywordsOf(SyntaxNode first, bool atModuleLevel) => first switch
    {
        VariableDeclarationNode { AccessModifier: AccessModifier.Implicit, IsStatic: true } => "Static",
        VariableDeclarationNode { AccessModifier: AccessModifier.Implicit } => "Dim",
        VariableDeclarationNode { AccessModifier: var access } when atModuleLevel => Keyword(access),
        ConstantDeclarationNode { AccessModifier: AccessModifier.Implicit } => "Const",
        ConstantDeclarationNode { AccessModifier: var access } when atModuleLevel => $"{Keyword(access)} Const",
        _ => null,
    };

    private static string Keyword(AccessModifier access) => access.ToString();
}
