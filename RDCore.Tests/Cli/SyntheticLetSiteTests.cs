using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// One Let statement, or one <c>For Each</c> control, assigned a value of another subtype each time it runs: each assignment is the let-coercion of the
/// value it is given then (MS-VBAL §5.4.3.8, §5.4.2.4), whatever the statement was given before; and a Let that runs again inside its own source
/// expression, before it completes, assigns what each run evaluates.
/// </summary>
[TestClass]
public sealed class SyntheticLetSiteTests
{
    private static string ProgramOf(params string[] lines)
        => string.Join("\r\n", ["Attribute VB_Name = \"Program\"", "Option Explicit", .. lines]) + "\r\n";

    private static async Task AssertPrintsAsync(string program, params string[] printed)
    {
        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            CollectionAssert.AreEqual(printed, await ModuleWorkspace.RunAsync([], program, engine), engine.ToString());
        }
    }

    [TestMethod]
    public Task AForEachControlAndALetInItsBody_AreEachLetTheSubtypeOfEachElement()
        => AssertPrintsAsync(ProgramOf(
            "Public Sub Main()",
            "    Dim V(1 To 6) As Variant, E As Variant, N As Long, S As String",
            "    V(1) = 5",
            "    V(2) = \"7\"",
            "    V(3) = 2.5",
            "    V(4) = True",
            "    V(6) = CLng(3)",
            "    For Each E In V",
            "        N = E",
            "        S = S & TypeName(E) & \" \" & N & \";\"",
            "    Next",
            "    Debug.Print S",
            "End Sub"), "Integer 5;String 7;Double 2;Boolean -1;Empty 0;Long 3;");

    // the Let of Sum runs inside its own source expression, in the call it makes: each run assigns the value it evaluated.
    [TestMethod]
    public Task ALetThatRunsAgainInsideItsOwnSource_AssignsWhatEachRunEvaluates()
        => AssertPrintsAsync(ProgramOf(
            "Public Sub Main()",
            "    Debug.Print Sum(4); Sum(1)",
            "End Sub",
            "Private Function Sum(ByVal N As Variant) As Variant",
            "    If N <= 1 Then",
            "        Sum = \"1\"",
            "    Else",
            "        Sum = N + Sum(N - 1)",
            "    End If",
            "End Function"), "10 1");
}
