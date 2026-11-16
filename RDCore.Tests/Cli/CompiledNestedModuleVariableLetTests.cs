using static RDCore.Tests.Cli.CompiledArrayFieldTests;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A Let of a variable of a module, of a standard module or of Me's class, whose value calls a function inside an expression (<c>Total = F(2) + 1</c>)
/// is compiled (MS-VBAL §5.4.3.8): the value is evaluated once, then the variable is given it, whatever the call wrote to it.
/// </summary>
[TestClass]
public sealed class CompiledNestedModuleVariableLetTests
{
    private const string Counted = "Public Calls As Long";

    private static readonly string[] Twice =
        ["Public Function Twice(ByVal x As Long) As Long", "    Calls = Calls + 1", "    Twice = x * 2", "End Function"];

    private static readonly (string Name, string Source)[] Tallies =
    [
        ("Tally", ModuleWorkspace.ClassModule("Tally", "Option Explicit",
            "Private mCount As Long",
            "Public Function Run(ByVal n As Long) As Long",
            "    Dim i As Long",
            "    For i = 1 To n",
            "        mCount = Twice(i) + 1",
            "        Run = Run + mCount",
            "    Next",
            "End Function")),
    ];

    // a variable of the standard module, given a call and a constant: the call made once each time.
    [TestMethod]
    public async Task AVariableOfTheModuleGivenACallInsideItsValue_IsCompiled()
        => await AssertCompiledAsync(Program([
            Counted,
            "Public Total As Long",
            "Public Sub Main()",
            "    Debug.Print Outer(10); Calls",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    Dim i As Long, t As Long",
            "    For i = 1 To n",
            "        Total = Twice(i) + 1",
            "        t = t + Total",
            "    Next",
            "    Outer = t",
            "End Function",
            .. Twice]), [], "| 120  10 |", ("Outer", 0));

    // a variable of Me's class.
    [TestMethod]
    public async Task AVariableOfMesClassGivenACallInsideItsValue_IsCompiled()
        => await AssertCompiledAsync(Program([
            Counted,
            "Public Sub Main()",
            "    Dim C As Tally",
            "    Set C = New Tally",
            "    Debug.Print C.Run(10); Calls",
            "End Sub",
            .. Twice]), Tallies, "| 120  10 |", ("Run", 0));

    // the call writes the variable, and the Let then gives it the value: what the call wrote is not what it holds after the statement.
    [TestMethod]
    public async Task AVariableTheCallWrites_HoldsTheValueAfterTheStatement()
        => await AssertCompiledAsync(Program(
            Counted,
            "Public Total As Long",
            "Public Sub Main()",
            "    Debug.Print Outer(); Calls",
            "End Sub",
            "Private Function Outer() As Long",
            "    Total = Bump() + 1",
            "    Outer = Total",
            "End Function",
            "Public Function Bump() As Long",
            "    Calls = Calls + 1",
            "    Total = 100",
            "    Bump = 1",
            "End Function"), [], "| 2  1 |", ("Outer", 0));
}
