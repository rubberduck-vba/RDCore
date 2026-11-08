using RDCore.SDK.Semantics.Flow;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Semantics.Flow;

/// <summary>
/// On how many of the code paths of a function or a property getter the return value is assigned: the host states the fact when it loads the code, from the code paths
/// of the lowered body and the expressions the static pass analyzed.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 5.0.3 Semantic Analysis")]
public sealed class ReturnValueAnalysisTests
{
    private static async Task<ReturnValueFact?> FactOfAsync(string body, string declaration = "Public Function F(ByVal c As Boolean, ByVal n As Long) As Long", string end = "End Function")
    {
        var program = $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n{declaration}\r\n{body.ReplaceLineEndings("\r\n")}\r\n{end}\r\n"
            + "Private Function G() As Long\r\nG = 1\r\nEnd Function\r\n";

        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        var procedure = payload.Modules.Single().Procedures.Single(candidate => candidate.Procedure.Fragment.EndsWith(".F", StringComparison.Ordinal));
        return procedure.ReturnValue;
    }

    private static async Task AssertAsync(string body, ReturnValueAssignment expected)
    {
        var fact = await FactOfAsync(body);

        Assert.IsNotNull(fact, "the fact is stated");
        Assert.AreEqual(expected, fact.Assignment);
        Assert.AreEqual("F", fact.Name);
    }

    [TestMethod]
    [DataRow("F = 1", DisplayName = "assigned in sequence")]
    [DataRow("If c Then\nF = 1\nElse\nF = 2\nEnd If", DisplayName = "assigned by both branches of an If")]
    [DataRow("If c Then\nF = 1\nElseIf n > 3 Then\nF = 2\nElse\nF = 3\nEnd If", DisplayName = "assigned by every branch of an If/ElseIf/Else")]
    [DataRow("If c Then F = 1 Else F = 2", DisplayName = "assigned by both branches of an inline If")]
    [DataRow("Select Case n\nCase 1\nF = 1\nCase 2, 3\nF = 2\nCase Else\nF = 3\nEnd Select", DisplayName = "assigned by every Case and the Case Else")]
    [DataRow("F = 1\nExit Function\nF = 2", DisplayName = "assigned before an Exit Function, with unreachable code after")]
    [DataRow("F = 1\nIf c Then Exit Function\nDebug.Print \"more\"", DisplayName = "assigned before a conditional exit")]
    public async Task ReturnValueAssignedOnEveryPath_IsAlways(string body)
        => await AssertAsync(body, ReturnValueAssignment.Always);

    [TestMethod]
    [DataRow("Do While True\nF = 1\nExit Function\nLoop", DisplayName = "a loop that only ends with an Exit Function that assigns")]
    [DataRow("While True\nF = 1\nExit Function\nWend", DisplayName = "While True")]
    [DataRow("Do\nF = 1\nExit Do\nLoop", DisplayName = "a bare loop")]
    [DataRow("Do\nF = 1\nLoop Until False", DisplayName = "a post-test loop whose condition is False")]
    [DataRow("For n = 1 To 3\nF = n\nNext", DisplayName = "a For loop whose bounds say it runs")]
    [DataRow("For n = 3 To 1 Step -1\nF = n\nNext", DisplayName = "a For loop that counts down")]
    [DataRow("If True Then\nF = 1\nEnd If", DisplayName = "If True")]
    public async Task AConditionThatIsALiteral_IsNotTakenBothWays(string body)
        => await AssertAsync(body, ReturnValueAssignment.Always);

    [TestMethod]
    [DataRow("On Error GoTo Handler\nF = G()\nExit Function\nHandler:\nF = -1", DisplayName = "the handler assigns too")]
    [DataRow("On Error GoTo Handler\nF = G()\nExit Function\nHandler:\nF = -1\nResume Done\nDone:", DisplayName = "the handler assigns, then resumes at a label")]
    [DataRow("F = 0\nOn Error GoTo Handler\nF = G()\nExit Function\nHandler:\nDebug.Print \"failed\"", DisplayName = "assigned before the error handler is in effect")]
    // a literal the result's type holds cannot fail, Resume Next or not.
    [DataRow("On Error Resume Next\nF = 1", DisplayName = "a literal under Resume Next")]
    public async Task ErrorHandlers_AreCodePathsToo(string body)
        => await AssertAsync(body, ReturnValueAssignment.Always);

    [TestMethod]
    [DataRow("If c Then F = 1", DisplayName = "only the Then branch assigns")]
    [DataRow("If c Then Exit Function\nF = 1", DisplayName = "a conditional exit before the assignment")]
    [DataRow("If c Then\nExit Function\nEnd If\nF = 1", DisplayName = "an Exit Function before the assignment")]
    [DataRow("If c Then\nF = 1\nElseIf n > 3 Then\nF = 2\nEnd If", DisplayName = "no Else")]
    [DataRow("Select Case n\nCase 1\nF = 1\nCase 2\nF = 2\nEnd Select", DisplayName = "no Case Else")]
    [DataRow("Select Case n\nCase 1\nF = 1\nCase Else\nDebug.Print \"?\"\nEnd Select", DisplayName = "a Case Else that does not assign")]
    [DataRow("Do While c\nF = 1\nLoop", DisplayName = "a loop that may not run")]
    [DataRow("For n = 1 To G()\nF = n\nNext", DisplayName = "a For loop whose bounds do not say it runs")]
    [DataRow("If c Then GoTo Skip\nF = 1\nSkip:", DisplayName = "a GoTo over the assignment")]
    [DataRow("On Error GoTo Handler\nF = G()\nExit Function\nHandler:\nDebug.Print \"failed\"", DisplayName = "an error handler that does not assign")]
    [DataRow("On Error GoTo Handler\nF = G()\nHandler:\nDebug.Print \"falls into it\"", DisplayName = "an error raised by the assignment itself, before it completes")]
    [DataRow("If n > 1 Then\nDo While c\nF = 1\nLoop\nElse\nF = 2\nEnd If", DisplayName = "a nested path")]
    // under Resume Next, an assignment whose expression fails does not complete, and the function returns what it had.
    [DataRow("On Error Resume Next\nF = G()", DisplayName = "an assignment that may fail under Resume Next")]
    // the graph has no edge for the Resume Next, and said Never.
    [DataRow("On Error GoTo H\nError 5\nF = 1\nExit Function\nH:\nIf n = 0 Then Resume Next", DisplayName = "an assignment reached by a Resume Next on some paths")]
    public async Task ReturnValueNotAssignedOnAPath_IsSometimes(string body)
        => await AssertAsync(body, ReturnValueAssignment.Sometimes);

    [TestMethod]
    [DataRow("Dim x As Long\nx = 1", DisplayName = "statements that do not assign it")]
    [DataRow("If c Then\nExit Function\nEnd If", DisplayName = "an exit")]
    [DataRow("Dim other As Long\nother = 1", DisplayName = "a variable of another name")]
    [DataRow("For n = 3 To 1\nF = n\nNext", DisplayName = "the only assignment is in a For loop whose bounds say it does not run")]
    [DataRow("If False Then\nF = 1\nEnd If", DisplayName = "the only assignment is where a literal False condition never goes")]
    public async Task ReturnValueNoPathAssigns_IsNever(string body)
    {
        var fact = await FactOfAsync(body);

        Assert.IsNotNull(fact);
        Assert.AreEqual(ReturnValueAssignment.Never, fact.Assignment);
        Assert.IsFalse(fact.IsEmpty);
    }

    [TestMethod]
    public async Task ABodyWithNoStatement_IsEmpty_AsTheMembersOfAnInterfaceAre()
    {
        var fact = await FactOfAsync(string.Empty);

        Assert.IsNotNull(fact);
        Assert.AreEqual(ReturnValueAssignment.Never, fact.Assignment);
        Assert.IsTrue(fact.IsEmpty);
    }

    [TestMethod]
    public async Task APropertyGetter_ReturnsAValueToo()
    {
        var always = await FactOfAsync("F = 1", "Public Property Get F() As Long", "End Property");
        var sometimes = await FactOfAsync("If Rnd > 0.5 Then Exit Property\nF = 1", "Public Property Get F() As Long", "End Property");

        Assert.AreEqual(ReturnValueAssignment.Always, always?.Assignment);
        Assert.AreEqual(ReturnValueAssignment.Sometimes, sometimes?.Assignment);
    }

    [TestMethod]
    public async Task ASubroutine_ReturnsNoValue_AndHasNoFact()
        => Assert.IsNull(await FactOfAsync("Dim x As Long", "Public Sub F(ByVal c As Boolean, ByVal n As Long)", "End Sub"));

    [TestMethod]
    public async Task ACodePathThatIsOnlyKnownAtRunTime_IsNotStated()
    {
        // a Return goes back to whichever GoSub sent control there: the code paths are not known.
        var subroutines = await FactOfAsync("GoSub Work\nF = 1\nExit Function\nWork:\nn = 2\nReturn");
        // a jump that lands nowhere is a compile error, and the body that was lowered is not the one that was written.
        var unresolved = await FactOfAsync("GoTo Nowhere\nF = 1");

        Assert.IsNull(subroutines);
        Assert.IsNull(unresolved);
    }
}
