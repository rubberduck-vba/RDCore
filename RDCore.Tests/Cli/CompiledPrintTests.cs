using static RDCore.Tests.Cli.CompiledArrayFieldTests;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>Debug.Print</c> of Strings, compiled: each item an expression of Strings, written as it is, separated by <c>;</c> or by nothing, and the line ended
/// after the last item when no separator follows it (MS-VBAL §5.4.5.8). <c>TypeName</c> of an object variable, of <c>Me</c> or of <c>Nothing</c>, the
/// library's <c>Replace</c> given Strings, and a <c>String</c> constant (<c>vbNullString</c> among them) are compiled code's too. A number, a <c>,</c>, a
/// <c>Spc</c> or a <c>Tab</c> is the interpreter's, which steps the statement. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledPrintTests
{
    // a class that says it is going, as a game's view adapter does.
    private static (string Name, string Source)[] Adapters()
        =>
        [
            ("Adapter", ModuleWorkspace.ClassModule("Adapter", ["Option Explicit",
                "Private Sub Class_Terminate()",
                "    Debug.Print TypeName(Me) & \" is terminating\"",
                "End Sub"])),
            ("Thing", ModuleWorkspace.ClassModule("Thing", ["Option Explicit", "Public Id As Long"])),
        ];

    // Class_Terminate names its class through TypeName(Me) and prints it, compiled, where the last reference to the object goes.
    [TestMethod]
    public async Task ClassTerminate_PrintsTypeNameOfMe_Compiled()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Dim A As Adapter",
            "    Set A = New Adapter",
            "    Set A = Nothing",
            "    Debug.Print \"done\"",
            "End Sub"), Adapters(), "| Adapter is terminating ; done |",
            ("Class_Terminate", 0));

    // items separated by ; are written one after the other; a ; after the last leaves the line open for the next Print, a Print of nothing ends a line,
    // and a String constant is the text it holds: vbNullString none.
    [TestMethod]
    public async Task ItemsSeparatedBySemicolons_AreWrittenAsTheyAre_AndATrailingSemicolonLeavesTheLineOpen()
        => await AssertCompiledAsync(Program(
            "Private Const Sep As String = \"-\"",
            "Public Sub Main()",
            "    Show \"x\"",
            "End Sub",
            "Private Sub Show(ByVal S As String)",
            "    Debug.Print \"<\"; S;",
            "    Debug.Print Sep; vbNullString; \">\"",
            "    Debug.Print",
            "    Debug.Print S & Sep & S",
            "End Sub"), [], "| <x-> ;  ; x-x |",
            ("Show", 0));

    // Replace given Strings, nested, and vbNullString to replace with, as a coordinate is parsed from its text: the library's function, called by
    // compiled code.
    [TestMethod]
    public async Task ReplaceGivenStrings_IsCalledByCompiledCode()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Stripped(\"(3,4)\")",
            "End Sub",
            "Private Function Stripped(ByVal Coord As String) As String",
            "    Coord = Replace(Replace(Coord, \"(\", vbNullString), \")\", vbNullString)",
            "    Stripped = Coord & \"|\" & Replace(\"aXbX\", \"X\", \"\")",
            "End Function"), [], "| 3,4|ab |",
            ("Stripped", 0));

    // TypeName of an object variable that holds Nothing, of Nothing itself, and of an object of a class of the workspace.
    [TestMethod]
    public async Task TypeNameOfAnObjectVariable_AndOfNothing_IsCompiled()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Named()",
            "End Sub",
            "Private Function Named() As String",
            "    Dim O As Thing, S As String",
            "    S = TypeName(O) & \" \" & TypeName(Nothing)",
            "    Set O = New Thing",
            "    S = S & \" \" & TypeName(O)",
            "    Named = S",
            "End Function"), Adapters(), "| Nothing Nothing Thing |",
            ("Named", 0));

    // a number, a , and Spc are the interpreter's to print, and so is Replace given a start: each such statement is stepped, and the Print of Strings after
    // them compiled.
    [TestMethod]
    public async Task APrintOfAnythingButStrings_IsSteppedByTheInterpreter()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Shapes \"a\"",
            "End Sub",
            "Private Sub Shapes(ByVal S As String)",
            "    Debug.Print S; 5",
            "    Debug.Print S, S",
            "    Debug.Print Spc(2); S",
            "    Debug.Print Replace(S & \"ba\", \"a\", \"c\", 2)",
            "    Debug.Print S & \"!\"",
            "End Sub"), [], "| a 5 ; a", ["Statement DebugPrint", "Statement DebugPrint", "Statement DebugPrint", "Call Replace"],
            ("Shapes", 4, 0));
}
