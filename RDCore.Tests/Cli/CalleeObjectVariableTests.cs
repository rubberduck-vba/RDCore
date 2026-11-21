using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// The object variables of a procedure a loop calls, and what a function that returns an object returns (MS-VBAL §5.3.1.10): an object only a variable of
/// the callee holds is destroyed when the variable lets go of it, at the statement that does or when the callee returns, before the caller's next statement;
/// one the callee returns outlives the callee's variable that held it, and is destroyed when the caller lets go of it. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CalleeObjectVariableTests
{
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Thing", ModuleWorkspace.ClassModule("Thing",
            "Option Explicit",
            "Public Value As Long",
            "Private Sub Class_Terminate()",
            "    Debug.Print \"terminated \" & Value",
            "End Sub")),
    ];

    [TestMethod]
    public async Task AnObjectOnlyACalleesVariableHolds_IsDestroyedWhereItIsLetGoOf_AndOneItReturns_WhereTheCallerLetsGoOfIt()
        => _ = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim I As Long, T As Thing",
            "    For I = 1 To 2",
            "        Kept I",
            "        Debug.Print \"after kept \" & I",
            "        Set T = Made(I)",
            "        Debug.Print \"made \" & T.Value",
            "        Set T = Nothing",
            "        Debug.Print \"after made \" & I",
            "    Next",
            "End Sub",
            "Private Sub Kept(ByVal N As Long)",
            "    Dim A As Thing, B As Thing",
            "    Set A = New Thing",
            "    A.Value = N * 10",
            "    Set B = New Thing",
            "    B.Value = N * 10 + 1",
            "    Set A = B",
            "    Debug.Print \"in kept \" & N",
            "End Sub",
            "Private Function Made(ByVal N As Long) As Thing",
            "    Dim R As Thing",
            "    Set R = New Thing",
            "    R.Value = N * 100",
            "    Set Made = R",
            "End Function"), classes: Classes,
            expected: "terminated 10 ; in kept 1 ; terminated 11 ; after kept 1 ; made 100 ; terminated 100 ; after made 1 ; "
                + "terminated 20 ; in kept 2 ; terminated 21 ; after kept 2 ; made 200 ; terminated 200 ; after made 2");
}
