using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;

namespace RDCore.Diagnostics.Analyzers;

// what makes a block empty: a block with no executable statement, however many comments, line labels, Dim, Static and Const declarations it has.
internal static class EmptyBlocks
{
    public static bool HasNoStatement(this StatementBlock block) => !block.Children.Any(IsStatement);

    public static bool HasNoStatement(this IEnumerable<SyntaxNode> nodes) => !nodes.Any(IsStatement);

    // a block of a #If is judged by what it holds, whichever branch is live.
    private static bool IsStatement(SyntaxNode node) => node switch
    {
        PrecompilerIfBlockStatementNode or PrecompilerElseIfBlockStatementNode or PrecompilerElseBlockStatementNode or PrecompilerInlineIfStatementNode
            => node.Children.Any(IsStatement),
        StatementNode => true,
        _ => false,
    };

    // the keyword a block begins with, as long as it is written.
    public static SourceRange Keyword(SyntaxNode node, int length)
        => new(node.SourceLocation.Range.Start, new SourcePosition(node.SourceLocation.Range.Start.Line, node.SourceLocation.Range.Start.Character + length));

    // a header from its keyword to the end of what it states.
    public static SourceRange Header(SyntaxNode node, SyntaxNode last) => new(node.SourceLocation.Range.Start, last.SourceLocation.Range.End);
}
