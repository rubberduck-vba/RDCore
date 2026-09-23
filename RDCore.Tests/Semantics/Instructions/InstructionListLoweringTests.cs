using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Semantics.Instructions;
using System.Collections.Immutable;

namespace RDCore.Tests.Semantics.Instructions;

/// <summary>
/// <see cref="InstructionListLowering"/> lowers a straight-line/jump procedure body into an
/// <see cref="InstructionList"/> without any symbol resolver — a label is not a symbol, so these tests
/// are parse-driven only, the same way <c>LabelStaticSemanticsTests</c> proves the G2 label rules
/// against the AST shapes the parser really builds.
/// </summary>
[TestClass]
public sealed class InstructionListLoweringTests
{
    private static InstructionListLoweringResult Lower(params string[] procedureBody)
    {
        var source = $"Sub Foo()\r\n{string.Join("\r\n", procedureBody)}\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var member = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        return InstructionListLowering.Lower(new StatementBlock([.. member.Children]));
    }

    private static void AssertNoErrors(InstructionListLoweringResult result)
        => Assert.IsEmpty(result.Errors, string.Join("; ", result.Errors.Select(error => $"{error.VBCompileErrorId}: {error.Verbose}")));

    private static void AssertSingleError(InstructionListLoweringResult result, VBCompileErrorId id, string verbose)
    {
        Assert.HasCount(1, result.Errors, string.Join("; ", result.Errors.Select(error => $"{error.VBCompileErrorId}: {error.Verbose}")));
        Assert.AreEqual(id, result.Errors[0].VBCompileErrorId);
        Assert.AreEqual(verbose, result.Errors[0].Verbose);
    }

    [TestMethod]
    public void SimpleStatements_LowerOneToOne_InSourceOrder()
    {
        var result = Lower("x = 1", "Foo 2", "y = 3");

        AssertNoErrors(result);
        Assert.HasCount(3, result.InstructionList.Items);
        for (var offset = 0; offset < result.InstructionList.Items.Length; offset++)
        {
            var instruction = result.InstructionList.Items[offset];
            Assert.AreEqual(offset, instruction.Offset);
            Assert.AreEqual(InstructionKind.Simple, instruction.Kind);
        }
    }

    [TestMethod]
    public void EveryInstruction_IsAddressableByItsSourceStatementIdentity()
    {
        var result = Lower("x = 1", "y = 2");
        var list = result.InstructionList;

        foreach (var instruction in list.Items)
        {
            Assert.IsTrue(list.TryGetOffset(instruction.Node!.Identity, out var offset));
            Assert.AreEqual(instruction.Offset, offset);
        }
    }

    [TestMethod]
    public void ALabel_DefinesNoInstructionOfItsOwn_AndDoesNotAdvanceTheOffset()
    {
        var result = Lower("Top:", "x = 1");

        AssertNoErrors(result);
        Assert.HasCount(1, result.InstructionList.Items);
        Assert.IsTrue(result.InstructionList.TryGetLabelOffset("Top", out var offset));
        Assert.AreEqual(0, offset);
    }

    [TestMethod]
    public void ATrailingLabel_ResolvesToTheOffsetPastTheLastInstruction()
    {
        var result = Lower("x = 1", "Done:");

        AssertNoErrors(result);
        Assert.HasCount(1, result.InstructionList.Items);
        Assert.IsTrue(result.InstructionList.TryGetLabelOffset("Done", out var offset));
        Assert.AreEqual(result.InstructionList.Items.Length, offset);
    }

    [TestMethod]
    public void LabelNames_AreCaseInsensitive()
    {
        var result = Lower("done:", "x = 1");

        Assert.IsTrue(result.InstructionList.TryGetLabelOffset("DONE", out _));
    }

    [TestMethod]
    public void GoTo_AForwardLabel_ResolvesToTheLabelsOffset()
    {
        var result = Lower("GoTo Done", "x = 1", "Done:", "y = 2");

        AssertNoErrors(result);
        var jump = result.InstructionList.Items[0];
        Assert.AreEqual(InstructionKind.Jump, jump.Kind);
        Assert.AreEqual(2, jump.Target);
    }

    [TestMethod]
    public void GoTo_ABackwardLabel_ResolvesToTheLabelsOffset()
    {
        var result = Lower("Top:", "x = 1", "GoTo Top");

        AssertNoErrors(result);
        var jump = result.InstructionList.Items[1];
        Assert.AreEqual(InstructionKind.Jump, jump.Kind);
        Assert.AreEqual(0, jump.Target);
    }

    [TestMethod]
    public void GoTo_ALineNumberLabel_ResolvesLikeAnyOtherLabel()
    {
        var result = Lower("GoTo 100", "x = 0", "100:", "x = 1");

        AssertNoErrors(result);
        Assert.AreEqual(2, result.InstructionList.Items[0].Target);
    }

    [TestMethod]
    public void GoTo_AnUndefinedLabel_LeavesTheTargetUnresolved_AndReportsLabelNotDefined()
    {
        var result = Lower("GoTo Nowhere");

        AssertSingleError(result, VBCompileErrorId.LabelNotDefined, "Nowhere");
        Assert.IsNull(result.InstructionList.Items[0].Target);
        Assert.AreEqual(InstructionKind.Jump, result.InstructionList.Items[0].Kind);
    }

    [TestMethod]
    public void OnGoTo_ResolvesEveryLabelInOrder()
    {
        var result = Lower("On n GoTo A, B, C", "A:", "x = 1", "B:", "x = 2", "C:");

        AssertNoErrors(result);
        var jumpTable = result.InstructionList.Items[0];
        Assert.AreEqual(InstructionKind.JumpTable, jumpTable.Kind);
        CollectionAssert.AreEqual(new int?[] { 1, 2, 3 }, jumpTable.Targets.ToArray());
    }

    [TestMethod]
    public void OnGoTo_AnUndefinedLabelInTheList_LeavesOnlyThatEntryUnresolved()
    {
        var result = Lower("On n GoTo A, Missing, C", "A:", "x = 1", "C:");

        AssertSingleError(result, VBCompileErrorId.LabelNotDefined, "Missing");
        var targets = result.InstructionList.Items[0].Targets;
        Assert.AreEqual(1, targets[0]);
        Assert.IsNull(targets[1]);
        Assert.AreEqual(2, targets[2]);
    }

    [TestMethod]
    public void ADuplicateLabelDefinition_KeepsTheFirstOffset_AndReportsDuplicateLabelDefinition()
    {
        var result = Lower("Top:", "x = 1", "Top:", "y = 2");

        AssertSingleError(result, VBCompileErrorId.DuplicateLabelDefinition, "Top");
        Assert.IsTrue(result.InstructionList.TryGetLabelOffset("Top", out var offset));
        Assert.AreEqual(0, offset);
    }

    [TestMethod]
    public void ADuplicateLabelDefinition_IsCaseInsensitiveToo()
        => AssertSingleError(Lower("Top:", "top:"), VBCompileErrorId.DuplicateLabelDefinition, "top");

    [TestMethod]
    [DataRow("Exit Sub", InstructionKind.ExitProcedure)]
    [DataRow("Exit Function", InstructionKind.ExitProcedure)]
    [DataRow("Exit Property", InstructionKind.ExitProcedure)]
    [DataRow("End", InstructionKind.Halt)]
    [DataRow("Stop", InstructionKind.Break)]
    public void KeywordStatements_LowerToTheirDedicatedKind(string statement, InstructionKind expectedKind)
    {
        var result = Lower(statement);

        AssertNoErrors(result);
        Assert.AreEqual(expectedKind, result.InstructionList.Items[0].Kind);
    }

    [TestMethod]
    [DataRow("Exit Do")]
    [DataRow("Exit For")]
    public void ExitForOrDo_WithNoEnclosingLoopOfTheMatchingKind_LeavesTheTargetUnresolved_NoDiagnostic(string statement)
    {
        var result = Lower(statement);

        AssertNoErrors(result);
        Assert.AreEqual(InstructionKind.ExitLoop, result.InstructionList.Items[0].Kind);
        Assert.IsNull(result.InstructionList.Items[0].Target);
    }

    // ---- If / ElseIf / Else (§5.4.2.8) ----

    [TestMethod]
    public void If_NoElse_FalseBranchSkipsToWhateverFollowsTheBlock()
    {
        var result = Lower("If True Then", "x = 1", "End If", "y = 2");
        var items = result.InstructionList.Items;

        AssertNoErrors(result);
        Assert.HasCount(4, items);
        var header = items[0];
        Assert.AreEqual(InstructionKind.ConditionalBranch, header.Kind);
        Assert.AreEqual(3, header.Else);
        Assert.AreEqual(3, header.End);
        Assert.AreEqual(InstructionKind.Simple, items[1].Kind);
        Assert.IsNull(items[2].Node);
        Assert.AreEqual(InstructionKind.Jump, items[2].Kind);
        Assert.AreEqual(3, items[2].Target);
    }

    [TestMethod]
    public void IfElse_FalseBranchTargetsTheElseBody_BothBranchesConvergeAfter()
    {
        var result = Lower("If True Then", "x = 1", "Else", "x = 2", "End If", "y = 3");
        var items = result.InstructionList.Items;

        AssertNoErrors(result);
        Assert.HasCount(6, items);
        Assert.AreEqual(3, items[0].Else); // header -> Else body (x = 2)
        Assert.AreEqual(5, items[0].End);  // right past the whole If
        Assert.AreEqual(5, items[2].Target); // trailing jump after x = 1 skips the Else body
        Assert.AreEqual(5, items[4].Target); // trailing jump after x = 2 lands in the same place
    }

    [TestMethod]
    public void IfElseIfElse_ChainsElseThroughEachHeaderInTurn()
    {
        var result = Lower("If a Then", "s1", "ElseIf b Then", "s2", "Else", "s3", "End If", "after");
        var items = result.InstructionList.Items;

        AssertNoErrors(result);
        Assert.HasCount(9, items);
        var ifHeader = items[0];
        var elseIfHeader = items[3];
        Assert.AreEqual(InstructionKind.ConditionalBranch, elseIfHeader.Kind);
        Assert.AreEqual(3, ifHeader.Else);      // If false -> the ElseIf header
        Assert.AreEqual(6, elseIfHeader.Else);  // ElseIf false -> the Else body (s3)
        Assert.AreEqual(8, ifHeader.End);       // right past the whole chain
        foreach (var trailingJumpOffset in new[] { 2, 5, 7 })
        {
            Assert.AreEqual(8, items[trailingJumpOffset].Target);
        }
    }

    [TestMethod]
    public void InlineIf_NoElse_FalseBranchSkipsTheThenStatement()
    {
        var result = Lower("If x Then y = 1", "z = 2");
        var items = result.InstructionList.Items;

        AssertNoErrors(result);
        Assert.HasCount(3, items);
        Assert.AreEqual(InstructionKind.ConditionalBranch, items[0].Kind);
        Assert.AreEqual(2, items[0].Else);
        Assert.IsNull(items[0].End); // no single closer instruction exists for an inline If
    }

    [TestMethod]
    public void InlineIf_WithElse_BothBranchesConvergeOnWhateverFollows()
    {
        var result = Lower("If x Then y = 1 Else y = 2", "z = 3");
        var items = result.InstructionList.Items;

        AssertNoErrors(result);
        Assert.HasCount(5, items);
        Assert.AreEqual(3, items[0].Else);   // false -> Else statement (y = 2)
        Assert.AreEqual(4, items[2].Target); // trailing jump after y = 1 skips the Else statement
    }

    // ---- Select Case (§5.4.2.10) ----

    [TestMethod]
    public void SelectCase_EveryCaseHeaderMatchesAgainstTheSameOpener_AndChainsToTheNextOnNoMatch()
    {
        var result = Lower("Select Case n", "Case 1", "s1", "Case 2", "s2", "Case Else", "s3", "End Select", "after");
        var items = result.InstructionList.Items;

        AssertNoErrors(result);
        Assert.HasCount(10, items);
        var opener = items[0];
        var case1 = items[1];
        var case2 = items[4];
        Assert.AreEqual(InstructionKind.Select, opener.Kind);
        Assert.AreEqual(9, opener.End);
        Assert.AreEqual(0, case1.Matching);
        Assert.AreEqual(0, case2.Matching);
        Assert.AreEqual(4, case1.Else); // no match -> try the next Case header
        Assert.AreEqual(7, case2.Else); // no match -> the Case Else body (s3)
        foreach (var trailingJumpOffset in new[] { 3, 6, 8 })
        {
            Assert.AreEqual(9, items[trailingJumpOffset].Target);
        }
    }

    [TestMethod]
    public void SelectCase_NoCaseElse_TheLastCaseFallsStraightThroughOnNoMatch()
    {
        var result = Lower("Select Case n", "Case 1", "s1", "End Select", "after");
        var items = result.InstructionList.Items;

        AssertNoErrors(result);
        Assert.HasCount(5, items);
        Assert.AreEqual(4, items[1].Else); // no match -> right past the whole Select
        Assert.AreEqual(4, items[0].End);
    }

    // ---- Loops (§5.4.2.2–.7) ----

    [TestMethod]
    public void DoWhileLoop_PreTestHeader_FalseSkipsPastTheBackEdge()
    {
        var result = Lower("Do While x", "s1", "Loop", "after");
        var items = result.InstructionList.Items;

        AssertNoErrors(result);
        Assert.HasCount(4, items);
        var header = items[0];
        Assert.AreEqual(InstructionKind.ConditionalBranch, header.Kind);
        Assert.AreEqual(3, header.Else); // false -> "after", past the back-edge at offset 2
        Assert.AreEqual(3, header.End);
        Assert.AreEqual(InstructionKind.Jump, items[2].Kind);
        Assert.AreEqual(0, items[2].Target); // back-edge to the header
    }

    [TestMethod]
    public void DoLoopUntil_PostTestCloser_BranchesBackWhenContinuing()
    {
        var result = Lower("Do", "s1", "Loop Until x", "after");
        var items = result.InstructionList.Items;

        AssertNoErrors(result);
        Assert.HasCount(3, items);
        Assert.AreEqual(InstructionKind.LoopBack, items[1].Kind);
        Assert.AreEqual(0, items[1].Target); // continuing branches back to the body's first instruction
        Assert.IsNotNull(items[1].Node); // the only real instruction this construct has to carry it
    }

    [TestMethod]
    public void BareDoLoop_IsAnUnconditionalBackEdge()
    {
        var result = Lower("Do", "s1", "Loop", "after");
        var items = result.InstructionList.Items;

        AssertNoErrors(result);
        Assert.HasCount(3, items);
        Assert.AreEqual(InstructionKind.Jump, items[1].Kind);
        Assert.AreEqual(0, items[1].Target);
    }

    [TestMethod]
    public void ForLoop_OpenerAndNext_BracketTheBody()
    {
        var result = Lower("For i = 1 To 3", "s1", "Next", "after");
        var items = result.InstructionList.Items;

        AssertNoErrors(result);
        Assert.HasCount(4, items);
        Assert.AreEqual(InstructionKind.ForOpener, items[0].Kind);
        Assert.AreEqual(3, items[0].End);
        Assert.AreEqual(InstructionKind.ForNext, items[2].Kind);
        Assert.IsNull(items[2].Node); // no dedicated "Next" AST node exists to attribute it to
        Assert.AreEqual(1, items[2].Target); // back to the body's first instruction
    }

    [TestMethod]
    public void ForEachLoop_OpenerAndNext_BracketTheBody()
    {
        var result = Lower("For Each item In coll", "s1", "Next", "after");
        var items = result.InstructionList.Items;

        AssertNoErrors(result);
        Assert.HasCount(4, items);
        Assert.AreEqual(InstructionKind.ForEachOpener, items[0].Kind);
        Assert.AreEqual(InstructionKind.ForEachNext, items[2].Kind);
        Assert.AreEqual(1, items[2].Target);
    }

    [TestMethod]
    public void ExitFor_InsideAForLoop_ResolvesToRightPastNext()
    {
        var result = Lower("For i = 1 To 3", "Exit For", "Next", "after");
        var items = result.InstructionList.Items;

        AssertNoErrors(result);
        var exit = items[1];
        Assert.AreEqual(InstructionKind.ExitLoop, exit.Kind);
        Assert.AreEqual(items.Length - 1, exit.Target); // "after"'s offset
    }

    [TestMethod]
    public void ExitDo_InsideADoLoop_ResolvesToRightPastTheCloser()
    {
        var result = Lower("Do", "Exit Do", "Loop", "after");
        var items = result.InstructionList.Items;

        AssertNoErrors(result);
        var exit = items[0];
        Assert.AreEqual(InstructionKind.ExitLoop, exit.Kind);
        Assert.AreEqual(items.Length - 1, exit.Target);
    }

    [TestMethod]
    public void ExitDo_InsideAWhileWendInsideADoLoop_ResolvesAgainstTheOuterDoLoop()
        // While/Wend has no Exit statement of its own — an Exit Do written inside one is not consumed
        // by it, and resolves against whatever Do loop already encloses it.
    {
        var result = Lower("Do", "While True", "Exit Do", "Wend", "Loop", "after");
        var items = result.InstructionList.Items;

        AssertNoErrors(result);
        var exit = items.Single(instruction => instruction.Kind == InstructionKind.ExitLoop);
        Assert.AreEqual(items.Length - 1, exit.Target);
    }

    [TestMethod]
    public void GoTo_IntoALoopBody_TargetsTheLabelInsideIt_NotTheLoopOpener()
        // MS-VBAL §5.4.2.3: a GoTo from outside a For loop into its body means the opener never runs.
        // Structurally, the jump target is the label's own offset inside the body, never the opener.
    {
        var result = Lower("GoTo Inside", "For i = 1 To 3", "Inside:", "s1", "Next");
        var items = result.InstructionList.Items;

        AssertNoErrors(result);
        var opener = items[1];
        Assert.AreEqual(InstructionKind.ForOpener, opener.Kind);
        Assert.IsTrue(result.InstructionList.TryGetLabelOffset("Inside", out var insideOffset));
        Assert.AreNotEqual(opener.Offset, insideOffset);
        Assert.AreEqual(insideOffset, items[0].Target);
    }

    // ---- With (§5.4.2.21) ----

    [TestMethod]
    public void With_EveryInstructionInsideCarriesTheOpenersOffset_EvenNestedInsideAnIf()
    {
        var result = Lower("With Foo", "x = 1", "If y Then", "z = 2", "End If", "End With", "after");
        var items = result.InstructionList.Items;

        AssertNoErrors(result);
        var withOpener = items[0];
        Assert.AreEqual(InstructionKind.With, withOpener.Kind);
        Assert.IsNull(withOpener.EnclosingWith);
        for (var offset = 1; offset < items.Length - 1; offset++)
        {
            Assert.AreEqual(withOpener.Offset, items[offset].EnclosingWith, $"offset {offset}");
        }
        Assert.IsNull(items[^1].EnclosingWith); // "after" is back outside the With block
    }
}
