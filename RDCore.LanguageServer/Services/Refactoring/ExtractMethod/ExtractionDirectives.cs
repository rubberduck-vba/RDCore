using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.LanguageServer.Services.Refactoring.ExtractMethod;

/// <summary>
/// The <c>#If</c> blocks (<strong>MS-VBAL §3.4</strong>) of a module as a selection to extract sees them: where each is written, and which of its lines are its
/// directives (the <c>#If</c>, each <c>#ElseIf</c> and <c>#Else</c>, the <c>#End If</c>).
/// </summary>
/// <remarks>
/// A block moves whole or not at all: a selection inside one of its branches is extracted from that branch, and a selection that
/// holds one of its directives holds all of it.
/// </remarks>
internal sealed class ExtractionDirectives
{
    private ExtractionDirectives(ImmutableArray<ExtractionDirectiveBlock> blocks) => Blocks = blocks;

    /// <summary>
    /// A module with no <c>#If</c> block.
    /// </summary>
    public static ExtractionDirectives None { get; } = new([]);

    /// <summary>
    /// The blocks of the module, nested ones included, in the order they are written.
    /// </summary>
    public ImmutableArray<ExtractionDirectiveBlock> Blocks { get; }

    /// <summary>
    /// The blocks of a module, read off its precompiler trivia.
    /// </summary>
    /// <param name="precompilerTrivia">The module's <c>ModuleParseResult.PrecompilerTrivia</c>.</param>
    /// <param name="text">The module's text, which says where the last line of each block ends.</param>
    public static ExtractionDirectives Of(ImmutableArray<SyntaxNode> precompilerTrivia, string text)
    {
        if (precompilerTrivia.IsDefaultOrEmpty)
        {
            return None;
        }

        var lines = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        var blocks = ImmutableArray.CreateBuilder<ExtractionDirectiveBlock>();
        foreach (var node in precompilerTrivia)
        {
            Collect(node, lines, blocks);
        }

        return new(blocks.ToImmutable());
    }

    private static void Collect(SyntaxNode node, string[] lines, ImmutableArray<ExtractionDirectiveBlock>.Builder blocks)
    {
        switch (node)
        {
            case PrecompilerIfBlockStatementNode ifBlock:
                var bodies = BodiesOf(ifBlock);
                var first = ifBlock.SourceLocation.Range.Start.Line;
                var last = LastLineOf(ifBlock.SourceLocation.Range);
                var code = bodies.Where(body => !body.SourceLocation.Range.IsEmpty)
                    .SelectMany(body => Enumerable.Range(body.SourceLocation.Range.Start.Line, LastLineOf(body.SourceLocation.Range) - body.SourceLocation.Range.Start.Line + 1))
                    .ToHashSet();
                var end = last < lines.Length ? lines[last].Length : 0;
                blocks.Add(new ExtractionDirectiveBlock(
                    new SourceRange(ifBlock.SourceLocation.Range.Start, new SourcePosition(last, end)),
                    Enumerable.Range(first, last - first + 1).Where(line => !code.Contains(line)).ToHashSet()));

                // a branch may itself hold a block.
                foreach (var child in bodies.SelectMany(body => body.Children))
                {
                    Collect(child, lines, blocks);
                }

                break;
            case PrecompilerTriviaNode trivia:
                foreach (var child in trivia.Children)
                {
                    Collect(child, lines, blocks);
                }

                break;
        }
    }

    // the code of each branch: the body after the #If, then the bodies of the #ElseIf and #Else branches.
    private static IEnumerable<PrecompilerTriviaNode> BodiesOf(PrecompilerIfBlockStatementNode ifBlock)
        => ifBlock.Children.OfType<PrecompilerTriviaNode>()
            .Concat(ifBlock.Children.Where(child => child is PrecompilerElseIfBlockStatementNode or PrecompilerElseBlockStatementNode)
                .SelectMany(branch => branch.Children.OfType<PrecompilerTriviaNode>()));

    // a range of precompiler trivia ends at the start of the line after its last.
    private static int LastLineOf(SourceRange range)
        => range.End.Character == 0 && range.End.Line > range.Start.Line ? range.End.Line - 1 : range.End.Line;
}

/// <summary>
/// An <c>#If</c> block of a module.
/// </summary>
/// <param name="Range">Where it is written: from its <c>#If</c> to the end of the line of its <c>#End If</c>.</param>
/// <param name="DirectiveLines">The lines its directives are written on, a directive continued over several lines included; not those of a block it holds.</param>
internal sealed record class ExtractionDirectiveBlock(SourceRange Range, IReadOnlySet<int> DirectiveLines);
