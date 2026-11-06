using RDCore.CLI.Host;
using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledModuleFieldTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Static</c> variable of a scalar type, compiled (IR lowering S2.6c): held where the interpreter holds it - the session's, for a procedure of a
/// standard module, and the object's, for a member of a class, each object its own (MS-VBA) - kept from call to call, and given its storage again by
/// <c>End</c>, which compiled code finds again. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledStaticTests
{
    // the program run again in the same session: Statics and the fields of modules are kept from one run to the next.
    private static readonly Func<EnvironmentSessionProvider, Task> RunAgain = _ => Task.CompletedTask;

    [TestMethod]
    public async Task AStatic_IsKeptFromCallToCall()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Counted(3); Counted(4); Counted(0); Counted(1)",
            "End Sub",
            "Private Function Counted(ByVal N As Long) As Long",
            "    Static Seen As Long",
            "    Static Halves As Double",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Seen = Seen + 1",
            "        Halves = Halves + 0.5",
            "    Next",
            "    Counted = Seen * 100 + Halves",
            "End Function"), [("Counted", 0)]);

    // a Static of each procedure is its own, whatever its name.
    [TestMethod]
    public async Task TwoProceduresStatics_OfOneName_AreEachTheirOwn()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Up(2); Down(3); Up(1); Down(1)",
            "End Sub",
            "Private Function Up(ByVal N As Long) As Long",
            "    Static Seen As Long",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Seen = Seen + 1",
            "    Next",
            "    Up = Seen",
            "End Function",
            "Private Function Down(ByVal N As Long) As Long",
            "    Static Seen As Long",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Seen = Seen - 10",
            "    Next",
            "    Down = Seen",
            "End Function"), [("Up", 0), ("Down", 0)]);

    // a Static of a member of a class is the object's: each object counts its own (MS-VBA, oracle\s19 N37).
    [TestMethod]
    public async Task AStaticOfAMemberOfAClass_IsEachObjectsOwn()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim A As New Tally, B As New Tally",
            "    Debug.Print A.Counted(2); B.Counted(5); A.Counted(1); B.Counted(0)",
            "End Sub"), [("Counted", 0)], classes: [("Tally", ModuleWorkspace.ClassModule("Tally", "Option Explicit",
                "Public Function Counted(ByVal N As Long) As Long",
                "    Static Seen As Long",
                "    Dim I As Long",
                "    For I = 1 To N",
                "        Seen = Seen + 1",
                "    Next",
                "    Counted = Seen",
                "End Function"))]);

    // what a Static holds, and a field of the module, is kept from one run to the next; End gives both their storage again, from nothing, which compiled
    // code finds again in the run after it.
    [TestMethod]
    public async Task AStaticAndAField_AreKeptFromRunToRun_AndResetByEnd()
        => await AssertAlikeAsync(Program(
            "Private Total As Long",
            "Public Sub Main()",
            "    Debug.Print Counted(2); Counted(3); Total",
            "    If Total > 20 Then End",
            "End Sub",
            "Private Function Counted(ByVal N As Long) As Long",
            "    Static Seen As Long",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Seen = Seen + 1",
            "        Total = Total + Seen",
            "    Next",
            "    Counted = Seen",
            "End Function"), [("Counted", 0)], between: [RunAgain, RunAgain, RunAgain]);

    // a Static the interpreter writes at a statement it runs alone is what compiled code reads after it.
    [TestMethod]
    public async Task AStatementTheInterpreterRuns_ReadsAndWritesTheStatic()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Shown 2",
            "    Shown 2",
            "End Sub",
            "Private Sub Shown(ByVal N As Long)",
            "    Static Seen As Long",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Seen = Seen + I",
            "        Debug.Print Seen",
            "    Next",
            "End Sub"), [("Shown", 4)]);
}
