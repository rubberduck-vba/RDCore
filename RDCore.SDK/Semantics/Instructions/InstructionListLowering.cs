using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Workspace;
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
/// straight to its body's first instruction. Any statement kind this pass does not recognize as one of
/// the control-flow shapes above — an ordinary data-manipulation statement (Let/Set-assignment, a
/// <c>Call</c>) chiefly — falls through as <see cref="InstructionKind.Simple"/>.
/// <para>
/// What is wrong with a body is not lowering's to find out: the <see cref="InstructionListLoweringResult.Errors"/> it reports are those of
/// <see cref="StatementStaticSemanticsEvaluator.CheckStructure"/>, which is the one place the rules are written - every label a jump names must be
/// defined exactly once in the procedure (<strong>MS-VBAL §5.4.1.1</strong>), an <c>Exit</c> statement must be where it may be
/// (<see cref="ExitStatementStaticSemantics"/>), and a statement must exist in the language. Lowering only has to act on the outcome: a jump whose
/// target does not resolve gets a <c>null</c> <see cref="Instruction.Target"/>/<see cref="Instruction.Targets"/> entry, a repeated label definition
/// keeps its first offset, and an <c>Exit</c> that is where it may not be, or a bare <c>Print</c> in a language that has none, gets no instruction.
/// </para>
/// <para>
/// A statement (or label) lexically inside a dead <c>#If</c>/<c>#ElseIf</c>/<c>#Else</c> branch — a
/// <c>Lower</c> call's own <c>deadRanges</c> argument, from
/// <c>RDCore.Runtime.Semantics.Precompiler.PrecompilerLiveBranchEvaluator</c> — is skipped entirely: no
/// instruction, no <c>ByNode</c> entry, no label definition, exactly as if the excluded text had never
/// been there.
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
    /// <param name="options">
    /// What the build being lowered for decides: which conditional-compilation branches are dead, and
    /// whether <c>Debug</c> statements are lowered at all. The default is a debug build of a body with
    /// no conditional compilation in it.
    /// </param>
    /// <param name="procedure">
    /// The kind of the procedure <paramref name="body"/> belongs to, which the <c>Exit Sub</c>, <c>Exit Function</c> and <c>Exit Property</c>
    /// statements are checked against. <see langword="null"/> - the default - when it is not known: those statements are not checked then.
    /// </param>
    public static InstructionListLoweringResult Lower(StatementBlock body, InstructionLoweringOptions options = default, MemberKind? procedure = null)
    {
        var state = new LoweringState(options.Dead, options.IncludeDebugStatements, options.Language, procedure);
        LowerBlock(body, state, default);

        // Every label in the procedure is now known, however deeply nested its definition was, so every
        // deferred jump target can be resolved in one final pass.
        foreach (var (index, operand) in state.PendingJumps)
        {
            state.Items[index] = state.Items[index] with { Target = ResolveLabel(operand, state.Labels) };
        }
        foreach (var (index, operands) in state.PendingJumpTables)
        {
            var targets = operands.Select(operand => ResolveLabel(operand, state.Labels)).ToImmutableArray();
            state.Items[index] = state.Items[index] with { Targets = targets };
        }

        // what is wrong with the body is the static pass's to say, once, whoever lowers it: lowering only needs to know where a jump lands, and a jump
        // that lands nowhere has no target.
        var errors = StatementStaticSemanticsEvaluator.CheckStructure(body, new StaticSemanticsOptions(options.Dead, options.Language), procedure);
        return new InstructionListLoweringResult(new InstructionList([.. state.Items], state.Labels, state.ByNode), errors);
    }

    private static void LowerBlock(StatementBlock block, LoweringState state, LoweringScope scope)
    {
        foreach (var child in block.Children)
        {
            if (IsDead(child.SourceLocation.Range, state.DeadRanges))
            {
                continue;
            }

            switch (child)
            {
                case LineLabelNode label:
                    // a label defined twice keeps its first offset: that it is one is the static pass's error to report.
                    state.Labels.TryAdd(label.Name, state.Items.Count);
                    break;
                case StatementNode statement:
                    LowerStatement(statement, state, scope);
                    break;
            }
        }
    }

    // A dead branch's whole range was already reported by PrecompilerLiveBranchEvaluator - a child
    // lexically inside it (at any depth) is simply never lowered, so no instruction, label, or ByNode
    // entry for it ever exists.
    private static bool IsDead(SourceRange range, ImmutableArray<SourceRange> deadRanges)
    {
        foreach (var dead in deadRanges)
        {
            if (dead.Start <= range.Start && range.End <= dead.End)
            {
                return true;
            }
        }
        return false;
    }

    private static void LowerStatement(StatementNode statement, LoweringState state, LoweringScope scope)
    {
        switch (statement)
        {
            // MS-VBAL has no Debug object; RDCore's own build configuration decides whether these exist
            // at run time. Leaving them out means exactly that - no instruction, not a no-op one - so a
            // release build pays nothing at all for a Debug.Print left in the source.
            case DebugStatementNode when !state.IncludeDebugStatements:
                break;

            // a bare Print is the Print member of a form or a report in VB6, which the platform has none of. In VBA it is a reserved identifier with no
            // semantics and no statement inside a procedure (only the Immediate window, which is not procedure scope, accepts it as Debug.Print), so
            // in a language without one (HasBarePrint) it is as undefined as any other name the language does not declare.
            case PrintStatementNode barePrint when BarePrintStaticSemantics.Evaluate(barePrint, state.Language) is not null:
                break;

            // a graphics statement with no object to draw on is the method of a form or a report, which the platform has none of: undefined, and so there is
            // nothing to run.
            case GraphicsMethodStatementNode graphics when GraphicsStatementStaticSemantics.Evaluate(graphics) is not null:
                break;

            case GoToStatementNode goTo:
                state.PendingJumps.Add((Emit(state, scope, statement, InstructionKind.Jump), goTo.LabelExpression));
                break;

            case OnGoToStatementNode onGoTo:
                state.PendingJumpTables.Add((Emit(state, scope, statement, InstructionKind.JumpTable), onGoTo.Labels));
                break;

            case GoSubStatementNode goSub:
                state.PendingJumps.Add((Emit(state, scope, statement, InstructionKind.GoSub), goSub.LabelExpression));
                break;

            case OnGoSubStatementNode onGoSub:
                state.PendingJumpTables.Add((Emit(state, scope, statement, InstructionKind.GoSubTable), onGoSub.Labels));
                break;

            case ReturnStatementNode:
                Emit(state, scope, statement, InstructionKind.Return);
                break;

            case OnErrorGoToStatementNode onErrorGoTo:
                LowerOnErrorGoTo(onErrorGoTo, state, scope);
                break;

            case OnErrorResumeStatementNode:
                Emit(state, scope, statement, InstructionKind.OnErrorResumeNext);
                break;

            case ResumeStatementNode resume:
                LowerResume(resume, state, scope);
                break;

            case ResumeNextStatementNode:
                Emit(state, scope, statement, InstructionKind.ResumeNext);
                break;

            case ErrorStatementNode:
                Emit(state, scope, statement, InstructionKind.RaiseError);
                break;

            case KeywordStatementNode { Token: Tokens.ExitSub or Tokens.ExitFunction or Tokens.ExitProperty } exitProcedure:
                if (CheckExit(exitProcedure, state, scope))
                {
                    Emit(state, scope, statement, InstructionKind.ExitProcedure);
                }
                break;

            case KeywordStatementNode { Token: Tokens.End }:
                Emit(state, scope, statement, InstructionKind.Halt);
                break;

            case KeywordStatementNode { Token: Tokens.Stop }:
                Emit(state, scope, statement, InstructionKind.Break);
                break;

            case KeywordStatementNode { Token: Tokens.ExitFor } exitFor:
                if (CheckExit(exitFor, state, scope))
                {
                    LowerExit(statement, state, scope, scope.EnclosingFor);
                }
                break;

            case KeywordStatementNode { Token: Tokens.ExitDo } exitDo:
                if (CheckExit(exitDo, state, scope))
                {
                    LowerExit(statement, state, scope, scope.EnclosingDo);
                }
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

    // an Exit statement is where it may be, or it is an error and there is nothing to lower: the same rule the static pass reports it by.
    private static bool CheckExit(KeywordStatementNode exit, LoweringState state, LoweringScope scope)
        => ExitStatementStaticSemantics.Evaluate(exit, scope.EnclosingFor is not null, scope.EnclosingDo is not null, state.Procedure) is null;

    private static void LowerExit(StatementNode statement, LoweringState state, LoweringScope scope, LoopExit? exit)
    {
        var index = Emit(state, scope, statement, InstructionKind.ExitLoop);
        exit?.ExitIndexes.Add(index);
    }

    // MS-VBAL §5.4.4.1 carves the line-number-label 0 out as a sentinel ("error handling disabled"), not
    // a label to resolve; real-world VBA also accepts -1 the same way (OnErrorGoToStatementNode's own doc
    // already anticipated both). Neither sentinel gets a PendingJumps entry - OnErrorDisable needs no
    // Target at all, it always means "disable, right now."
    private static void LowerOnErrorGoTo(OnErrorGoToStatementNode onErrorGoTo, LoweringState state, LoweringScope scope)
    {
        if (LabelOperands.IsIntegerConstant(onErrorGoTo.LabelExpression, 0) || LabelOperands.IsIntegerConstant(onErrorGoTo.LabelExpression, -1))
        {
            Emit(state, scope, onErrorGoTo, InstructionKind.OnErrorDisable);
            return;
        }

        state.PendingJumps.Add((Emit(state, scope, onErrorGoTo, InstructionKind.OnErrorGoTo), onErrorGoTo.LabelExpression));
    }

    // MS-VBAL §5.4.4.2: a bare Resume and a Resume whose label is the line-number-label 0 both
    // re-execute the fault statement (ResumeCurrentStatement, no static Target - the executor reads it
    // off the activation's own ErrorHandlerState at runtime); a real label branches there instead
    // (ResumeLabel, resolved the same way GoTo's own label is).
    private static void LowerResume(ResumeStatementNode resume, LoweringState state, LoweringScope scope)
    {
        if (resume.LabelExpression is null || LabelOperands.IsIntegerConstant(resume.LabelExpression, 0))
        {
            Emit(state, scope, resume, InstructionKind.ResumeCurrentStatement);
            return;
        }

        state.PendingJumps.Add((Emit(state, scope, resume, InstructionKind.ResumeLabel), resume.LabelExpression));
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
        Emit(state, scope, null, InstructionKind.ForNext, target: bodyStart, matching: opener);
        var after = state.Items.Count;
        PatchEnd(state, opener, after);
        return after;
    }

    private static int LowerForEach(ForEachStatementNode forEach, LoweringState state, LoweringScope scope)
    {
        var opener = Emit(state, scope, forEach, InstructionKind.ForEachOpener);
        var bodyStart = state.Items.Count;
        LowerBlock(forEach.Body, state, scope);
        Emit(state, scope, null, InstructionKind.ForEachNext, target: bodyStart, matching: opener);
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

    // a jump whose operand is no label, or names one that is not defined, lands nowhere: the static pass reports it.
    private static int? ResolveLabel(ExpressionNode operand, IReadOnlyDictionary<string, int> labels)
        => LabelOperands.TryGetLabelName(operand, out var name) && labels.TryGetValue(name, out var target) ? target : null;

    // Shared, mutable across the whole recursive lowering of one procedure body.
    private sealed class LoweringState(ImmutableArray<SourceRange> deadRanges, bool includeDebugStatements, SupportedLanguage? language, MemberKind? procedure)
    {
        public SupportedLanguage? Language { get; } = language;
        public MemberKind? Procedure { get; } = procedure;
        public List<Instruction> Items { get; } = [];
        public Dictionary<string, int> Labels { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<SyntaxNodeId, int> ByNode { get; } = [];
        public List<(int Index, ExpressionNode Operand)> PendingJumps { get; } = [];
        public List<(int Index, ImmutableArray<ExpressionNode> Operands)> PendingJumpTables { get; } = [];
        public ImmutableArray<SourceRange> DeadRanges { get; } = deadRanges;
        public bool IncludeDebugStatements { get; } = includeDebugStatements;
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
