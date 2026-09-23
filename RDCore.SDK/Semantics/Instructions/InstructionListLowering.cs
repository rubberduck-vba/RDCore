using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Semantics.Static;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Instructions;

/// <summary>
/// Lowers a procedure body into an <see cref="InstructionList"/> — <strong>RD-VBAL §3.5</strong>. This
/// is the statement-tree analogue of <see cref="StatementStaticSemanticsEvaluator"/>: where that walker
/// recurses through the whole tree checking types and label references, lowering produces the flat,
/// offset-addressable list a future interpreter drives with a program counter, because <c>GoTo</c>/
/// <c>GoSub</c>/<c>On…GoTo</c>/<c>Resume</c> can jump anywhere in the procedure and a recursive tree
/// walk cannot express that.
/// </summary>
/// <remarks>
/// <strong>Scope of this pass.</strong> Every statement lexically inside <paramref name="body"/> — at
/// any nesting depth, through <c>If</c>/<c>ElseIf</c>/inline <c>If</c>/<c>Select Case</c>/a loop/
/// <c>With</c> — is lowered. Structured statements stay instructions; block closers (a loop's back-edge,
/// an <c>If</c>/<c>Case</c> branch's trailing jump) are synthesized, carry no source
/// <see cref="StatementNode"/> (<see cref="Instruction.Node"/> is <c>null</c>), and are never keyed in
/// <c>ByNode</c>. An unconditional "else" branch (<c>Else</c>, <c>Case Else</c>) gets no header
/// instruction of its own — it has no condition to evaluate, so its chain's previous header branches
/// straight to its body's first instruction. Any statement kind this pass does not recognize —
/// including <c>GoSub</c>/<c>Return</c>/<c>On…GoSub</c> and error-handling statements — falls through
/// as <see cref="InstructionKind.Simple"/>.
/// <para>
/// Lowering doubles as a validator for the one static-semantics rule it needs to resolve jump targets
/// at all: every label a jump names must be defined exactly once in the procedure
/// (<strong>MS-VBAL §5.4.1.1</strong>). A jump whose target does not resolve gets a <c>null</c>
/// <see cref="Instruction.Target"/>/<see cref="Instruction.Targets"/> entry and a
/// <see cref="VBCompileErrorId.LabelNotDefined"/> diagnostic; a repeated label definition gets a
/// <see cref="VBCompileErrorId.DuplicateLabelDefinition"/> diagnostic and keeps its first offset. This
/// pass needs no symbol resolver: a label is not a symbol, so <see cref="LabelOperands"/> reads a jump's
/// operand directly off the expression tree, the same way <see cref="StatementStaticSemanticsEvaluator"/>
/// does. An <c>Exit For</c>/<c>Exit Do</c> with no enclosing loop of the matching kind is left with an
/// unresolved <see cref="Instruction.Target"/> and no diagnostic.
/// </para>
/// </remarks>
public static class InstructionListLowering
{
    /// <summary>
    /// Lowers <paramref name="body"/> — a procedure's top-level statement list — into an
    /// <see cref="InstructionList"/>.
    /// </summary>
    /// <param name="body">
    /// A procedure body: a <see cref="MemberDeclarationNode"/>'s own <c>Children</c>, wrapped in a
    /// <see cref="StatementBlock"/>. A label is scoped to the whole procedure, so passing a nested
    /// block on its own would under-resolve every jump into or out of it.
    /// </param>
    public static InstructionListLoweringResult Lower(StatementBlock body)
    {
        var state = new LoweringState();
        LowerBlock(body, state, default);

        // Every label in the procedure is now known, however deeply nested its definition was, so every
        // deferred jump target can be resolved in one final pass.
        foreach (var (index, operand) in state.PendingJumps)
        {
            state.Items[index] = state.Items[index] with { Target = ResolveLabel(operand, state.Labels, state.Errors) };
        }
        foreach (var (index, operands) in state.PendingJumpTables)
        {
            var targets = operands.Select(operand => ResolveLabel(operand, state.Labels, state.Errors)).ToImmutableArray();
            state.Items[index] = state.Items[index] with { Targets = targets };
        }

        return new InstructionListLoweringResult(new InstructionList([.. state.Items], state.Labels, state.ByNode), state.Errors.ToImmutable());
    }

    private static void LowerBlock(StatementBlock block, LoweringState state, LoweringScope scope)
    {
        foreach (var child in block.Children)
        {
            switch (child)
            {
                case LineLabelNode label:
                    if (!state.Labels.TryAdd(label.Name, state.Items.Count))
                    {
                        state.Errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.DuplicateLabelDefinition, label.SourceLocation, label.Name));
                    }
                    break;
                case StatementNode statement:
                    LowerStatement(statement, state, scope);
                    break;
            }
        }
    }

    private static void LowerStatement(StatementNode statement, LoweringState state, LoweringScope scope)
    {
        switch (statement)
        {
            case GoToStatementNode goTo:
                state.PendingJumps.Add((Emit(state, scope, statement, InstructionKind.Jump), goTo.LabelExpression));
                break;

            case OnGoToStatementNode onGoTo:
                state.PendingJumpTables.Add((Emit(state, scope, statement, InstructionKind.JumpTable), onGoTo.Labels));
                break;

            case KeywordStatementNode { Token: Tokens.ExitSub or Tokens.ExitFunction or Tokens.ExitProperty }:
                Emit(state, scope, statement, InstructionKind.ExitProcedure);
                break;

            case KeywordStatementNode { Token: Tokens.End }:
                Emit(state, scope, statement, InstructionKind.Halt);
                break;

            case KeywordStatementNode { Token: Tokens.Stop }:
                Emit(state, scope, statement, InstructionKind.Break);
                break;

            case KeywordStatementNode { Token: Tokens.ExitFor }:
                LowerExit(statement, state, scope, scope.EnclosingFor);
                break;

            case KeywordStatementNode { Token: Tokens.ExitDo }:
                LowerExit(statement, state, scope, scope.EnclosingDo);
                break;

            case IfBlockStatementNode ifBlock:
                LowerIf(ifBlock, state, scope);
                break;

            case InlineIfStatementNode inlineIf:
                LowerInlineIf(inlineIf, state, scope);
                break;

            case SelectCaseStatementNode selectCase:
                LowerSelectCase(selectCase, state, scope);
                break;

            case WhileWendStatementNode whileWend:
                // While/Wend has no Exit statement of its own (MS-VBAL grants it neither Exit Do nor an
                // Exit Wend) - an Exit Do written inside one resolves against whatever Do loop already
                // encloses it, so no new EnclosingDo context is pushed here.
                LowerPreTestLoop(whileWend, whileWend.Body, state, scope);
                break;

            case DoWhileLoopStatementNode doWhile:
                LowerLoopWithExit(state, scope, exit => LowerPreTestLoop(doWhile, doWhile.Body, state, scope with { EnclosingDo = exit }));
                break;

            case DoUntilLoopStatementNode doUntil:
                LowerLoopWithExit(state, scope, exit => LowerPreTestLoop(doUntil, doUntil.Body, state, scope with { EnclosingDo = exit }));
                break;

            case DoLoopWhileStatementNode doLoopWhile:
                LowerLoopWithExit(state, scope, exit => LowerPostTestLoop(doLoopWhile, doLoopWhile.Body, state, scope with { EnclosingDo = exit }));
                break;

            case DoLoopUntilStatementNode doLoopUntil:
                LowerLoopWithExit(state, scope, exit => LowerPostTestLoop(doLoopUntil, doLoopUntil.Body, state, scope with { EnclosingDo = exit }));
                break;

            case DoLoopStatementNode doLoop:
                LowerLoopWithExit(state, scope, exit => LowerBareLoop(doLoop, doLoop.Body, state, scope with { EnclosingDo = exit }));
                break;

            case ForStatementNode forStatement:
                LowerLoopWithExit(state, scope, exit => LowerFor(forStatement, state, scope with { EnclosingFor = exit }));
                break;

            case ForEachStatementNode forEach:
                LowerLoopWithExit(state, scope, exit => LowerForEach(forEach, state, scope with { EnclosingFor = exit }));
                break;

            case WithStatementNode withStatement:
                LowerWith(withStatement, state, scope);
                break;

            default:
                Emit(state, scope, statement, InstructionKind.Simple);
                break;
        }
    }

    // Shares the "push an exit context, lower the loop, patch every Exit For/Do found inside to land
    // right past the closer" ceremony across all six loop shapes; only the shape-specific opener/closer
    // emission differs between them.
    private static void LowerLoopWithExit(LoweringState state, LoweringScope scope, Func<LoopExit, int> lowerLoop)
    {
        var exit = new LoopExit();
        var after = lowerLoop(exit);
        foreach (var index in exit.ExitIndexes)
        {
            PatchTarget(state, index, after);
        }
    }

    private static void LowerExit(StatementNode statement, LoweringState state, LoweringScope scope, LoopExit? exit)
    {
        var index = Emit(state, scope, statement, InstructionKind.ExitLoop);
        exit?.ExitIndexes.Add(index);
    }

    private static void LowerIf(IfBlockStatementNode ifBlock, LoweringState state, LoweringScope scope)
    {
        var trailingJumps = new List<int>();
        var firstHeader = Emit(state, scope, ifBlock, InstructionKind.ConditionalBranch);
        var previousHeader = firstHeader;
        LowerBlock(ifBlock.Body, state, scope);
        trailingJumps.Add(Emit(state, scope, null, InstructionKind.Jump));

        foreach (var elseIf in ifBlock.ElseIfBlocks)
        {
            PatchElse(state, previousHeader, state.Items.Count);
            previousHeader = Emit(state, scope, elseIf, InstructionKind.ConditionalBranch);
            LowerBlock(elseIf.Body, state, scope);
            trailingJumps.Add(Emit(state, scope, null, InstructionKind.Jump));
        }

        var lastHeaderNeedsElse = true;
        if (ifBlock.ElseBlock is { } elseBlock)
        {
            PatchElse(state, previousHeader, state.Items.Count);
            lastHeaderNeedsElse = false;
            LowerBlock(elseBlock.Body, state, scope);
            trailingJumps.Add(Emit(state, scope, null, InstructionKind.Jump));
        }

        var after = state.Items.Count;
        if (lastHeaderNeedsElse)
        {
            PatchElse(state, previousHeader, after);
        }
        PatchEnd(state, firstHeader, after);
        foreach (var jump in trailingJumps)
        {
            PatchTarget(state, jump, after);
        }
    }

    private static void LowerInlineIf(InlineIfStatementNode inlineIf, LoweringState state, LoweringScope scope)
    {
        var header = Emit(state, scope, inlineIf, InstructionKind.ConditionalBranch);
        LowerBlock(inlineIf.ThenBody, state, scope);

        if (inlineIf.ElseBody is { } elseBody)
        {
            var trailingJump = Emit(state, scope, null, InstructionKind.Jump);
            PatchElse(state, header, state.Items.Count);
            LowerBlock(elseBody, state, scope);
            PatchTarget(state, trailingJump, state.Items.Count);
        }
        else
        {
            PatchElse(state, header, state.Items.Count);
        }
    }

    private static void LowerSelectCase(SelectCaseStatementNode selectCase, LoweringState state, LoweringScope scope)
    {
        var opener = Emit(state, scope, selectCase, InstructionKind.Select);
        var trailingJumps = new List<int>();
        int? previousHeader = null;

        foreach (var caseBlock in selectCase.CaseExpressionBlocks)
        {
            if (previousHeader is { } previous)
            {
                PatchElse(state, previous, state.Items.Count);
            }
            var header = Emit(state, scope, caseBlock, InstructionKind.ConditionalBranch, matching: opener);
            LowerBlock(caseBlock.Block, state, scope);
            trailingJumps.Add(Emit(state, scope, null, InstructionKind.Jump));
            previousHeader = header;
        }

        if (selectCase.CaseElseBlock is { } caseElse)
        {
            if (previousHeader is { } previous)
            {
                PatchElse(state, previous, state.Items.Count);
            }
            previousHeader = null;
            LowerBlock(caseElse.Body, state, scope);
            trailingJumps.Add(Emit(state, scope, null, InstructionKind.Jump));
        }

        var after = state.Items.Count;
        if (previousHeader is { } last)
        {
            PatchElse(state, last, after);
        }
        PatchEnd(state, opener, after);
        foreach (var jump in trailingJumps)
        {
            PatchTarget(state, jump, after);
        }
    }

    private static int LowerPreTestLoop(StatementNode node, StatementBlock body, LoweringState state, LoweringScope scope)
    {
        var header = Emit(state, scope, node, InstructionKind.ConditionalBranch);
        LowerBlock(body, state, scope);
        Emit(state, scope, null, InstructionKind.Jump, target: header);
        var after = state.Items.Count;
        PatchElse(state, header, after);
        PatchEnd(state, header, after);
        return after;
    }

    private static int LowerPostTestLoop(StatementNode node, StatementBlock body, LoweringState state, LoweringScope scope)
    {
        var bodyStart = state.Items.Count;
        LowerBlock(body, state, scope);
        Emit(state, scope, node, InstructionKind.LoopBack, target: bodyStart);
        return state.Items.Count;
    }

    private static int LowerBareLoop(StatementNode node, StatementBlock body, LoweringState state, LoweringScope scope)
    {
        var bodyStart = state.Items.Count;
        LowerBlock(body, state, scope);
        Emit(state, scope, node, InstructionKind.Jump, target: bodyStart);
        return state.Items.Count;
    }

    private static int LowerFor(ForStatementNode forStatement, LoweringState state, LoweringScope scope)
    {
        var opener = Emit(state, scope, forStatement, InstructionKind.ForOpener);
        var bodyStart = state.Items.Count;
        LowerBlock(forStatement.Body, state, scope);
        Emit(state, scope, null, InstructionKind.ForNext, target: bodyStart);
        var after = state.Items.Count;
        PatchEnd(state, opener, after);
        return after;
    }

    private static int LowerForEach(ForEachStatementNode forEach, LoweringState state, LoweringScope scope)
    {
        var opener = Emit(state, scope, forEach, InstructionKind.ForEachOpener);
        var bodyStart = state.Items.Count;
        LowerBlock(forEach.Body, state, scope);
        Emit(state, scope, null, InstructionKind.ForEachNext, target: bodyStart);
        var after = state.Items.Count;
        PatchEnd(state, opener, after);
        return after;
    }

    private static void LowerWith(WithStatementNode withStatement, LoweringState state, LoweringScope scope)
    {
        var opener = Emit(state, scope, withStatement, InstructionKind.With);
        LowerBlock(withStatement.Body, state, scope with { EnclosingWith = opener });
        PatchEnd(state, opener, state.Items.Count);
    }

    private static int Emit(LoweringState state, LoweringScope scope, StatementNode? node, InstructionKind kind, int? target = null, int? matching = null)
    {
        var offset = state.Items.Count;
        if (node is not null)
        {
            state.ByNode[node.Identity] = offset;
        }
        state.Items.Add(new Instruction(offset, node, kind, target, ImmutableArray<int?>.Empty, Else: null, End: null, matching, scope.EnclosingWith));
        return offset;
    }

    private static void PatchElse(LoweringState state, int index, int value) => state.Items[index] = state.Items[index] with { Else = value };
    private static void PatchEnd(LoweringState state, int index, int value) => state.Items[index] = state.Items[index] with { End = value };
    private static void PatchTarget(LoweringState state, int index, int value) => state.Items[index] = state.Items[index] with { Target = value };

    private static int? ResolveLabel(ExpressionNode operand, IReadOnlyDictionary<string, int> labels, ImmutableArray<VBCompileErrorInfo>.Builder errors)
    {
        if (!LabelOperands.TryGetLabelName(operand, out var name))
        {
            errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.LabelNotDefined, operand.Location,
                "A jump target must be a line label or a line number."));
            return null;
        }

        if (labels.TryGetValue(name, out var target))
        {
            return target;
        }

        errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.LabelNotDefined, operand.Location, name));
        return null;
    }

    // Shared, mutable across the whole recursive lowering of one procedure body.
    private sealed class LoweringState
    {
        public List<Instruction> Items { get; } = [];
        public Dictionary<string, int> Labels { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<SyntaxNodeId, int> ByNode { get; } = [];
        public ImmutableArray<VBCompileErrorInfo>.Builder Errors { get; } = ImmutableArray.CreateBuilder<VBCompileErrorInfo>();
        public List<(int Index, ExpressionNode Operand)> PendingJumps { get; } = [];
        public List<(int Index, ImmutableArray<ExpressionNode> Operands)> PendingJumpTables { get; } = [];
    }

    // The offsets of every Exit For/Exit Do instruction found inside one loop, patched once that loop's
    // own closer offset is known.
    private sealed class LoopExit
    {
        public List<int> ExitIndexes { get; } = [];
    }

    // What changes as lowering descends into a nested block; everything else lives on LoweringState.
    private readonly record struct LoweringScope(int? EnclosingWith, LoopExit? EnclosingFor, LoopExit? EnclosingDo);
}
