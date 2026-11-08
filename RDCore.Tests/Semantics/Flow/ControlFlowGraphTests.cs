using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Semantics.Flow;
using RDCore.SDK.Semantics.Instructions;
using RDCore.Runtime.LanguageCore.Instructions;

namespace RDCore.Tests.Semantics.Flow;

/// <summary>
/// The code paths of a procedure: where control can go from each instruction, and the questions about them that other analyses ask.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 5.0.3 Semantic Analysis")]
public sealed class ControlFlowGraphTests
{
    private static ControlFlowGraph Graph(string body)
    {
        var source = $"Public Sub S(ByVal c As Boolean, ByVal n As Long)\r\n{body.ReplaceLineEndings("\r\n")}\r\nEnd Sub\r\n";
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), source);
        Assert.IsEmpty(result.SyntaxErrors);
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        return ControlFlowGraph.Of(InstructionListLowering.Lower(new StatementBlock([.. member.Children])).InstructionList);
    }

    // the offset of the instruction lowered from the statement written on a line (zero-based, counting the declaration as line 0).
    private static int At(ControlFlowGraph graph, int line)
    {
        Assert.IsTrue(graph.Instructions.TryGetOffsetAtLine(line, out var offset));
        return offset;
    }

    [TestMethod]
    public void ABodyWithNoInstruction_StartsWhereItEnds()
    {
        var graph = Graph(string.Empty);

        Assert.AreEqual(graph.Exit, graph.Entry);
        Assert.IsTrue(graph.IsReachable(graph.Exit));
    }

    [TestMethod]
    public void EveryInstructionOfAStraightBody_IsReachable_AndTheEndToo()
    {
        var graph = Graph("n = 1\nn = 2");

        Assert.IsTrue(graph.IsReachable(At(graph, 1)));
        Assert.IsTrue(graph.IsReachable(At(graph, 2)));
        Assert.IsTrue(graph.IsReachable(graph.Exit));
    }

    [TestMethod]
    public void WhatFollowsAnExit_IsNotReachable()
    {
        var graph = Graph("Exit Sub\nn = 1");

        Assert.IsFalse(graph.IsReachable(At(graph, 2)), "dead code");
        Assert.IsTrue(graph.IsReachable(graph.Exit));
    }

    [TestMethod]
    public void WhatFollowsAGoToOverIt_IsNotReachable_UnlessALabelIsJumpedTo()
    {
        var graph = Graph("GoTo Done\nn = 1\nDone:\nn = 2");

        Assert.IsFalse(graph.IsReachable(At(graph, 2)));
        Assert.IsTrue(graph.IsReachable(At(graph, 4)));
    }

    [TestMethod]
    public void TheEndOfTheProgram_DoesNotEndTheActivation()
    {
        var graph = Graph("End\nn = 1");

        Assert.IsFalse(graph.IsReachable(graph.Exit));
        Assert.IsFalse(graph.IsReachable(At(graph, 2)));
    }

    [TestMethod]
    public void TheBranchOfACondition_CanBeAvoided_ByThePathThatDoesNotTakeIt()
    {
        var graph = Graph("If c Then\nn = 1\nEnd If");
        var assignment = At(graph, 2);

        Assert.IsTrue(graph.CanReach(graph.Exit, instruction => instruction.Offset == assignment), "the condition may be false");
    }

    [TestMethod]
    public void EveryPath_ThatGoesThroughAnInstruction_CannotAvoidIt()
    {
        var graph = Graph("If c Then\nn = 1\nElse\nn = 2\nEnd If");
        var assignments = new[] { At(graph, 2), At(graph, 4) };

        Assert.IsFalse(graph.CanReach(graph.Exit, instruction => assignments.Contains(instruction.Offset)));
        Assert.IsTrue(graph.CanReach(graph.Exit, instruction => instruction.Offset == assignments[0]), "the Else branch avoids the first one");
    }

    [TestMethod]
    public void ALoopThatMayNotRun_CanBeSkipped_AndALiteralTrueLoopCannotBeLeftByItsCondition()
    {
        var skippable = Graph("Do While c\nn = 1\nLoop");
        var forever = Graph("Do While True\nn = 1\nLoop");

        Assert.IsTrue(skippable.CanReach(skippable.Exit, instruction => instruction.Offset == At(skippable, 2)));
        Assert.IsFalse(forever.IsReachable(forever.Exit), "nothing but an Exit leaves it");
    }

    [TestMethod]
    public void AnErrorHandler_IsReachedFromTheInstructionsThatCanRaiseWhileItIsInEffect()
    {
        var graph = Graph("n = 0\nOn Error GoTo Handler\nn = 1\nExit Sub\nHandler:\nn = 2");
        var raising = At(graph, 3);
        var beforeIt = At(graph, 1);

        Assert.IsTrue(graph.SuccessorsOf(raising).Any(edge => edge.Kind is ControlFlowEdgeKind.Error));
        Assert.IsFalse(graph.SuccessorsOf(beforeIt).Any(edge => edge.Kind is ControlFlowEdgeKind.Error), "the handler was not in effect yet");
        Assert.IsTrue(graph.IsReachable(At(graph, 6)));
    }

    [TestMethod]
    public void AnErrorHandler_IsNotReachedAfterItIsDisabled()
    {
        var graph = Graph("On Error GoTo Handler\nn = 1\nOn Error GoTo 0\nn = 2\nExit Sub\nHandler:\nn = 3");

        Assert.IsTrue(graph.SuccessorsOf(At(graph, 2)).Any(edge => edge.Kind is ControlFlowEdgeKind.Error));
        Assert.IsFalse(graph.SuccessorsOf(At(graph, 4)).Any(edge => edge.Kind is ControlFlowEdgeKind.Error));
    }

    [TestMethod]
    public void AnInstructionThatRaisesAnError_DoesNotCompleteBeforeTheHandler()
    {
        var graph = Graph("On Error GoTo Handler\nn = 1\nExit Sub\nHandler:\nn = 2");
        var raising = At(graph, 2);
        var handlerBody = At(graph, 5);

        // the handler is only reached by the error the assignment raises, which leaves it before it completes: a path that must go through a completed
        // assignment does not exist, and one that may stop at it does.
        Assert.IsTrue(graph.CanReach(handlerBody, instruction => instruction.Offset == raising));
        Assert.IsFalse(graph.CanReach(At(graph, 3), instruction => instruction.Offset == raising), "the Exit Sub is only reached when the assignment completed");
    }

    [TestMethod]
    [DataRow("GoSub Work\nExit Sub\nWork:\nn = 1\nReturn", ControlFlowImprecision.Subroutines)]
    [DataRow("On Error GoTo Handler\nn = 1\nExit Sub\nHandler:\nResume Next", ControlFlowImprecision.Resumption)]
    [DataRow("On Error GoTo Handler\nn = 1\nExit Sub\nHandler:\nResume", ControlFlowImprecision.Resumption)]
    [DataRow("GoTo Nowhere", ControlFlowImprecision.UnresolvedJump)]
    [DataRow("n = 1\nIf c Then Exit Sub", ControlFlowImprecision.None)]
    public void WhatTheGraphApproximates_IsSaid(string body, ControlFlowImprecision expected)
        => Assert.AreEqual(expected, Graph(body).Imprecision);
}
