using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Refactoring;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace RDCore.LanguageServer.Services.Refactoring.MoveMember;

/// <summary>
/// Which way a member moves: above the member before it, or below the one after it.
/// </summary>
internal enum MemberMoveDirection
{
    Up,
    Down,
}

/// <summary>
/// Why a member is not moved past the one next to it.
/// </summary>
internal enum MemberMoveRefusalKind
{
    /// <summary>
    /// The member is in a branch of an <c>#If</c> block that the one it would pass is not in: it would leave that branch.
    /// </summary>
    LeavesItsBranch,

    /// <summary>
    /// The one it would pass is in a branch of an <c>#If</c> block that the member is not in: it would go into that branch.
    /// </summary>
    EntersABranch,
}

/// <summary>
/// A procedure of a module, as it moves: a <c>Sub</c>, a <c>Function</c>, or the <c>Property Get</c>, <c>Let</c> and <c>Set</c> of one name written one
/// after another, in the same branches of the <c>#If</c> blocks, which move together.
/// </summary>
/// <param name="Extent">The whole text of the procedures, from the comments above the first to the line after the last.</param>
/// <param name="Members">The procedures, in the order they are written.</param>
/// <param name="Branches">The branches of the <c>#If</c> blocks of the module that hold them, the outermost first.</param>
internal sealed record class MemberUnit(MemberExtent Extent, ImmutableArray<MemberDeclarationNode> Members, IReadOnlyList<(int Block, int Branch)> Branches)
{
    public string Name => Members[0].Name;
}

/// <summary>
/// A member moved past the one next to it, or why it is not.
/// </summary>
/// <param name="Units">The procedures of the module, as they move, in the order they are written.</param>
/// <param name="Moved">The ordinal of the one moved among them.</param>
/// <param name="Passed">The ordinal of the one it goes past: the one before it, or the one after it.</param>
/// <param name="Refusal">Why it is not moved; null when it is.</param>
internal sealed record class MemberMovePlan(ImmutableArray<MemberUnit> Units, int Moved, int Passed, MemberMoveRefusalKind? Refusal)
{
    public MemberMoveDirection Direction => Passed < Moved ? MemberMoveDirection.Up : MemberMoveDirection.Down;
}

/// <summary>
/// Move Member Up and Move Member Down: a procedure of a module, with its comments and its <c>Attribute</c> lines, goes above the procedure before it or below
/// the one after it. The <c>Property Get</c>, <c>Let</c> and <c>Set</c> of one name written one after another move together, and are passed together.
/// </summary>
/// <remarks>
/// What a module declares does not depend on the order of its procedures: a move changes what the module means only when it takes a procedure into a branch of
/// an <c>#If</c> block, or out of one (<strong>MS-VBAL §3.4</strong>), which is refused. Procedures are written in the code section of the module, after its
/// declaration section (<strong>MS-VBAL §5.2</strong>, <strong>§5.3</strong>): its first procedure is not moved up.
/// </remarks>
internal static partial class MemberMoves
{
    /// <summary>
    /// The move of the procedure a range is in, past the one before it or the one after it; null when the range is in no procedure, spans more than one, or the
    /// procedure is the first or the last, or when the module does not parse.
    /// </summary>
    public static MemberMovePlan? Plan(ModuleParseResult parse, string text, SourceRange range, MemberMoveDirection direction)
    {
        if (!parse.IsSuccess || parse.SyntaxTree is null)
        {
            return null;
        }

        var units = UnitsOf(parse, text);
        var moved = units.ToList().FindIndex(unit => Holds(unit.Extent.Range, range));
        var passed = direction is MemberMoveDirection.Up ? moved - 1 : moved + 1;
        if (moved < 0 || passed < 0 || passed >= units.Length)
        {
            return null;
        }

        var (branches, passing) = (units[moved].Branches, units[passed].Branches);
        MemberMoveRefusalKind? refusal = branches.SequenceEqual(passing) ? null
            : passing.Count > branches.Count && passing.Take(branches.Count).SequenceEqual(branches) ? MemberMoveRefusalKind.EntersABranch
            : MemberMoveRefusalKind.LeavesItsBranch;
        return new MemberMovePlan(units, moved, passed, refusal);
    }

    /// <summary>
    /// The edits of the module that move the procedure (<see cref="MemberEdits.Move"/>): its removal, and its text written past the other one.
    /// </summary>
    public static IReadOnlyList<RefactorTextEdit> Edits(string text, MemberMovePlan plan)
        => MemberEdits.Move(text, plan.Units[plan.Moved].Extent, plan.Units[plan.Passed].Extent, after: plan.Direction is MemberMoveDirection.Down);

    /// <summary>
    /// Whether the module, moved, is the module as the plan moves it: it parses, it has the procedures it had, in the order the move leaves them, and each is
    /// in the branches of the <c>#If</c> blocks it was in.
    /// </summary>
    public static bool Verify(MemberMovePlan plan, ModuleParseResult moved)
    {
        if (!moved.IsSuccess || moved.SyntaxTree is null)
        {
            return false;
        }

        var expected = plan.Units.ToArray();
        (expected[plan.Moved], expected[plan.Passed]) = (expected[plan.Passed], expected[plan.Moved]);
        var written = ProceduresOf(moved);
        return expected.SelectMany(unit => unit.Members.Select(member => (member.Name, member.MemberKind, unit.Branches)))
            .Select(member => (member.Name, member.MemberKind, Branches: string.Join(",", member.Branches)))
            .SequenceEqual(written.Select(member => (member.Member.Name, member.Member.MemberKind, Branches: string.Join(",", member.Branches))));
    }

    /// <summary>
    /// A text with edits made, each of the text as it was before any of them.
    /// </summary>
    public static string Applied(string text, IReadOnlyList<RefactorTextEdit> edits)
    {
        var starts = new List<int> { 0 };
        starts.AddRange(LineEnds().Matches(text).Select(end => end.Index + end.Length));
        int Offset(SourcePosition position) => starts[position.Line] + position.Character;
        foreach (var edit in edits.OrderByDescending(edit => Offset(edit.Range.Start)))
        {
            text = string.Concat(text.AsSpan(0, Offset(edit.Range.Start)), edit.NewText, text.AsSpan(Offset(edit.Range.End)));
        }

        return text;
    }

    // the procedures of a module as they move: a Property Get, Let or Set goes with the one of its name before it, when they are in the same branches.
    private static ImmutableArray<MemberUnit> UnitsOf(ModuleParseResult parse, string text)
    {
        var units = new List<MemberUnit>();
        foreach (var (member, branches) in ProceduresOf(parse))
        {
            var extent = MemberEdits.Extent(parse, text, member);
            if (units is [.., var last] && IsProperty(member) && IsProperty(last.Members[^1])
                && last.Name.Equals(member.Name, StringComparison.OrdinalIgnoreCase) && last.Branches.SequenceEqual(branches))
            {
                units[^1] = last with
                {
                    Extent = last.Extent with { Range = new SourceRange(last.Extent.Range.Start, extent.Range.End) },
                    Members = last.Members.Add(member),
                };
                continue;
            }

            units.Add(new MemberUnit(extent, [member], branches));
        }

        return [.. units];
    }

    // the procedures of a module, each with the branches that hold it. A block written in a procedure moves with it, so only the blocks of the module itself
    // are counted: their ordinals are the same before a move and after it.
    private static IReadOnlyList<(MemberDeclarationNode Member, IReadOnlyList<(int Block, int Branch)> Branches)> ProceduresOf(ModuleParseResult parse)
    {
        var procedures = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Where(member => IsProcedure(member.MemberKind)).ToArray();
        var blocks = ConditionalCompilationBlocks.Of(parse.PrecompilerTrivia);
        bool OfTheModule(SourceRange block) => !procedures.Any(procedure => Holds(procedure.SourceLocation.Range, block));
        return [.. procedures.Select(procedure => (procedure, blocks.BranchesAt(procedure.SourceLocation.Range.Start, OfTheModule)))];
    }

    private static bool IsProcedure(MemberKind kind)
        => kind is MemberKind.Procedure or MemberKind.Function or MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet;

    private static bool IsProperty(MemberDeclarationNode member) => member.MemberKind is MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet;

    // whether a range is in another: a position at the end of the other is in it only when the other ends on its last line, at the end of the text.
    private static bool Holds(SourceRange outer, SourceRange inner)
        => outer.Start <= inner.Start && inner.End <= outer.End && (inner.Start < outer.End || outer.End.Character != 0);

    [GeneratedRegex(@"\r\n|\r|\n")]
    private static partial Regex LineEnds();
}
