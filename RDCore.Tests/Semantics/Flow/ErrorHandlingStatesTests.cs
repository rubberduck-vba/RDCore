using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Semantics.Flow;
using RDCore.SDK.Semantics.Instructions;
using RDCore.Runtime.LanguageCore.Instructions;

namespace RDCore.Tests.Semantics.Flow;

/// <summary>
/// How an error raised by each instruction would be handled, over every code path to it: what tells whether code can be moved to another procedure without
/// changing what an error in it does.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 5.0.3 Semantic Analysis")]
public sealed class ErrorHandlingStatesTests
{
    private static ErrorHandlingStates States(string body)
    {
        var source = $"Public Sub S(ByVal c As Boolean, ByVal n As Long)\r\n{body.ReplaceLineEndings("\r\n")}\r\nEnd Sub\r\n";
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), source);
        Assert.IsEmpty(result.SyntaxErrors);
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        return ErrorHandlingStates.Of(ControlFlowGraph.Of(InstructionListLowering.Lower(new StatementBlock([.. member.Children])).InstructionList));
    }

    // the offset of the instruction lowered from the statement written on a line (zero-based, counting the declaration as line 0).
    private static int At(ErrorHandlingStates states, int line)
    {
        Assert.IsTrue(states.Graph.Instructions.TryGetOffsetAtLine(line, out var offset));
        return offset;
    }

    private static int Label(ErrorHandlingStates states, string label)
    {
        Assert.IsTrue(states.Graph.Instructions.TryGetLabelOffset(label, out var offset));
        return offset;
    }

    [TestMethod]
    public void WithNoOnError_AnErrorIsRaisedToTheCaller()
    {
        var states = States("n = 1\nn = 2");

        Assert.AreEqual(ErrorHandlingState.Initial, states.At(At(states, 2)).Single());
        Assert.IsTrue(states.At(At(states, 2)).Single().RaisesToCaller);
        Assert.IsEmpty(states.HandlersAt(At(states, 2)));
    }

    [TestMethod]
    public void AfterOnErrorResumeNext_AnErrorResumesAtTheNextStatement()
    {
        var states = States("On Error Resume Next\nn = 1");

        var state = states.At(At(states, 2)).Single();
        Assert.AreEqual(ErrorHandlingPolicy.ResumeNext, state.Policy);
        Assert.IsFalse(state.RaisesToCaller);
    }

    [TestMethod]
    public void AfterOnErrorGoTo_AnErrorGoesToTheHandler()
    {
        var states = States("On Error GoTo Handler\nn = 1\nExit Sub\nHandler:\nDebug.Print \"failed\"");

        CollectionAssert.AreEqual(new[] { Label(states, "Handler") }, states.HandlersAt(At(states, 2)).ToArray());
    }

    [TestMethod]
    public void InTheHandler_AnErrorIsActive_AndIsRaisedToTheCaller()
    {
        var states = States("On Error GoTo Handler\nn = 1\nExit Sub\nHandler:\nDebug.Print \"failed\"");

        var state = states.At(At(states, 5)).Single();
        Assert.IsTrue(state.IsHandling);
        Assert.IsTrue(state.RaisesToCaller, "an error raised in a handler is not caught by it");
        Assert.IsEmpty(states.HandlersAt(At(states, 5)));
    }

    [TestMethod]
    public void AHandlerThatIsFallenInto_HasNoActiveError_OnThatPath()
    {
        var states = States("On Error GoTo Handler\nn = 1\nHandler:\nDebug.Print \"falls into it\"");

        var handling = states.At(At(states, 4)).Select(state => state.IsHandling).Order().ToArray();
        CollectionAssert.AreEqual(new[] { false, true }, handling, "fallen into, and taken");
    }

    [TestMethod]
    public void OnErrorGoToZero_EndsThePolicy()
    {
        var states = States("On Error GoTo Handler\nn = 1\nOn Error GoTo 0\nn = 2\nExit Sub\nHandler:\nResume Next");

        Assert.AreEqual(ErrorHandlingState.Initial, states.At(At(states, 4)).Single());
        Assert.IsEmpty(states.HandlersAt(At(states, 4)));
    }

    [TestMethod]
    public void TheStatesOfTwoBranches_AreBothKept()
    {
        var states = States("If c Then\nOn Error Resume Next\nElse\nOn Error GoTo Handler\nEnd If\nn = 1\nExit Sub\nHandler:\nResume Next");

        var policies = states.At(At(states, 6)).Select(state => state.Policy).Order().ToArray();
        CollectionAssert.AreEqual(new[] { ErrorHandlingPolicy.ResumeNext, ErrorHandlingPolicy.GoTo }, policies);
        CollectionAssert.AreEqual(new[] { Label(states, "Handler") }, states.HandlersAt(At(states, 6)).ToArray());
    }

    [TestMethod]
    public void AfterResumeNext_WhatOnlyItReaches_RunsWithTheHandlerInEffect()
    {
        // n = 2 is reached by no other path: Error 5 never completes. The handler resumes after it, and the next error goes to the handler as the first did.
        var states = States("On Error GoTo Handler\nError 5\nn = 2\nExit Sub\nHandler:\nResume Next");

        var state = states.At(At(states, 3)).Single();
        Assert.AreEqual(Label(states, "Handler"), state.CatchingHandler);
        Assert.IsFalse(state.IsHandling, "a resumed error is not active any more");
    }

    [TestMethod]
    public void AfterResume_TheFaultStatementRunsAgain_WithThePolicyTheHandlerSet()
    {
        // only the Resume brings n = 1 back under On Error Resume Next, which the handler set.
        var states = States("On Error GoTo Handler\nn = 1\nExit Sub\nHandler:\nOn Error Resume Next\nResume");

        CollectionAssert.AreEquivalent(new[] { ErrorHandlingPolicy.GoTo, ErrorHandlingPolicy.ResumeNext }, states.At(At(states, 2)).Select(state => state.Policy).ToArray());
        Assert.IsTrue(states.At(At(states, 2)).All(state => !state.IsHandling));
    }

    [TestMethod]
    public void AnOnErrorInAHandler_SetsThePolicyForAfterIt_TheErrorStaysActive()
    {
        // MS-VBA: an error raised in the handler after On Error GoTo Other is raised to the caller, not caught by Other (Excel-verified).
        var states = States("On Error GoTo Handler\nn = 1\nExit Sub\nHandler:\nOn Error GoTo Other\nn = 2\nExit Sub\nOther:\nDebug.Print \"other\"");

        var state = states.At(At(states, 6)).Single();
        Assert.IsTrue(state.IsHandling);
        Assert.AreEqual(ErrorHandlingPolicy.GoTo, state.Policy);
        Assert.IsTrue(state.RaisesToCaller);
    }

    [TestMethod]
    public void OnErrorGoToMinusOne_EndsTheActiveError_AndKeepsThePolicy()
    {
        // MS-VBA: after On Error GoTo -1 in the handler, a second error goes to the handler again (Excel-verified).
        var states = States("On Error GoTo Handler\nn = 1\nExit Sub\nHandler:\nOn Error GoTo -1\nn = 2\nExit Sub");

        var state = states.At(At(states, 6)).Single();
        Assert.IsFalse(state.IsHandling);
        Assert.AreEqual(Label(states, "Handler"), state.CatchingHandler);
    }

    [TestMethod]
    public void AnUnreachableInstruction_HasNoState()
    {
        var states = States("Exit Sub\nn = 1");

        Assert.IsEmpty(states.At(At(states, 2)));
    }
}
