using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics;

/// <summary>
/// The shape of the <c>#If</c> blocks of a module (<strong>MS-VBAL §3.4</strong>): which source ranges are the branches of one block, whatever they evaluate to.
/// </summary>
/// <remarks>
/// A name can be declared in each branch of a <c>#If</c> block and be one declaration, whichever branch compiles: the branches are alternatives of one another.
/// A name declared twice in the same branch - or twice with no <c>#If</c> at all - is not an alternative of itself, but a duplicate. Telling them apart takes no
/// constant to be evaluated, only where each branch begins and ends; it is the structure that the evaluation of the constants (<c>PrecompilerLiveBranchEvaluator</c>)
/// then chooses a branch of.
/// </remarks>
public sealed class ConditionalCompilationBlocks
{
    private readonly ImmutableArray<ImmutableArray<SourceRange>> _blocks;

    private ConditionalCompilationBlocks(ImmutableArray<ImmutableArray<SourceRange>> blocks) => _blocks = blocks;

    /// <summary>
    /// A module with no <c>#If</c> block: nothing is an alternative of anything.
    /// </summary>
    public static ConditionalCompilationBlocks None { get; } = new([]);

    /// <summary>
    /// The blocks of a module, read off its precompiler trivia, nested ones included.
    /// </summary>
    /// <param name="precompilerTrivia">The module's <c>ModuleParseResult.PrecompilerTrivia</c>.</param>
    public static ConditionalCompilationBlocks Of(ImmutableArray<SyntaxNode> precompilerTrivia)
    {
        if (precompilerTrivia.IsDefaultOrEmpty)
        {
            return None;
        }

        var blocks = ImmutableArray.CreateBuilder<ImmutableArray<SourceRange>>();
        foreach (var node in precompilerTrivia)
        {
            Collect(node, blocks);
        }

        return new(blocks.ToImmutable());
    }

    /// <summary>
    /// Whether the declarations at <paramref name="first"/> and <paramref name="second"/> are in different branches of one block, so that at most one of them is ever compiled.
    /// </summary>
    /// <param name="first">Where the first is declared.</param>
    /// <param name="second">Where the second is declared.</param>
    public bool AreAlternatives(SourcePosition first, SourcePosition second)
    {
        foreach (var branches in _blocks)
        {
            var inFirst = BranchAt(branches, first);
            var inSecond = BranchAt(branches, second);
            if (inFirst >= 0 && inSecond >= 0 && inFirst != inSecond)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether what is written at <paramref name="position"/> is in a branch of a block, and so compiled only when that branch is live.
    /// </summary>
    /// <param name="position">Where it is written.</param>
    public bool IsConditional(SourcePosition position) => _blocks.Any(branches => BranchAt(branches, position) >= 0);

    /// <summary>
    /// The branches that hold what is written at <paramref name="position"/>, the outermost first: for each block that holds it, which block it is and which of
    /// its branches. Neither says where the block is written, only how many blocks come before it and how many branches before the one that holds it.
    /// </summary>
    /// <param name="position">Where it is written.</param>
    /// <param name="counted">Which blocks are counted, by the range from the start of the first branch to the end of the last: a block that is not counted
    /// holds nothing, and is not counted in the ordinal of those after it.</param>
    /// <returns>For each block that holds the position, its ordinal among the blocks counted, in the order they are written, and the ordinal of the branch -
    /// the <c>#If</c>, each <c>#ElseIf</c>, then the <c>#Else</c>; nothing when no counted block holds it.</returns>
    /// <remarks>
    /// Two declarations with the same branches are compiled together, whatever the constants are; a declaration moved into a branch, out of one or to another
    /// is compiled when other constants say so (<strong>MS-VBAL §3.4</strong>).
    /// </remarks>
    public IReadOnlyList<(int Block, int Branch)> BranchesAt(SourcePosition position, Func<SourceRange, bool> counted)
    {
        var branchesAt = new List<(int Block, int Branch)>();
        var ordinal = 0;
        foreach (var branches in _blocks)
        {
            if (branches.IsDefaultOrEmpty || !counted(new SourceRange(branches[0].Start, branches[^1].End)))
            {
                continue;
            }

            if (BranchAt(branches, position) is var branch and >= 0)
            {
                branchesAt.Add((ordinal, branch));
            }

            ordinal++;
        }

        return branchesAt;
    }

    // the range of a branch ends at the start of its last token, and is inclusive of it.
    private static int BranchAt(ImmutableArray<SourceRange> branches, SourcePosition position)
    {
        for (var index = 0; index < branches.Length; index++)
        {
            if (position >= branches[index].Start && position <= branches[index].End)
            {
                return index;
            }
        }

        return -1;
    }

    private static void Collect(SyntaxNode node, ImmutableArray<ImmutableArray<SourceRange>>.Builder blocks)
    {
        switch (node)
        {
            case PrecompilerIfBlockStatementNode ifBlock:
                var bodies = BranchBodies(ifBlock);
                blocks.Add([.. bodies.Select(body => body.SourceLocation.Range)]);

                // a branch may itself contain a block.
                foreach (var body in bodies)
                {
                    foreach (var child in body.Children)
                    {
                        Collect(child, blocks);
                    }
                }

                break;
            case PrecompilerTriviaNode trivia:
                foreach (var child in trivia.Children)
                {
                    Collect(child, blocks);
                }

                break;
        }
    }

    // one body per #If/#ElseIf/#Else branch, in source order.
    private static List<PrecompilerTriviaNode> BranchBodies(PrecompilerIfBlockStatementNode ifBlock)
    {
        var bodies = new List<PrecompilerTriviaNode>();
        if (ifBlock.Children is [PrecompilerInlineIfStatementNode, PrecompilerTriviaNode body, ..])
        {
            bodies.Add(body);
        }

        bodies.AddRange(ifBlock.Children.OfType<PrecompilerElseIfBlockStatementNode>()
            .Select(elseIf => elseIf.Children.OfType<PrecompilerTriviaNode>().FirstOrDefault()).OfType<PrecompilerTriviaNode>());

        if (ifBlock.Children.OfType<PrecompilerElseBlockStatementNode>().FirstOrDefault()?.Children.OfType<PrecompilerTriviaNode>().FirstOrDefault() is { } elseBody)
        {
            bodies.Add(elseBody);
        }

        return bodies;
    }
}
