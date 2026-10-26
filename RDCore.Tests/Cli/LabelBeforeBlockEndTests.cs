using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBAL §5.4.1.1: a statement label is defined at the start of a line, and a name followed by a colon there is one, even right before the line that ends a
/// block (<c>Fin: End If</c>) and even when a procedure has that name. Each row is what MS-VBA prints (recorded in Excel, grid <c>oracle\findings-labels</c>
/// L01-L12, L20-L22; HexIDE's findings desk #22).
/// </summary>
[TestClass]
public sealed class LabelBeforeBlockEndTests
{
    private static readonly (string Name, string Source)[] Greetings =
        [("Greetings", StandardModule("Greetings", "Public Greeted As Long", "Public Sub PrintGreeting()", "    Greeted = 42", "End Sub"))];

    private static Task<string[]> Run(params string[] body)
        => RunAsync([], Greetings, StandardModule("Program", ["Option Explicit", "Public Sub Main()", .. body, "End Sub"]));

    [TestMethod]
    [DataRow("End If", new[] { "If True Then", "Debug.Print \"T\"", "Fin: End If", "Debug.Print \"AFTER\"" }, new[] { "T", "AFTER" })]
    [DataRow("Loop", new[] { "Dim i As Long", "Do While i < 2", "i = i + 1", "Fin: Loop", "Debug.Print i" }, new[] { "2" })]
    [DataRow("Next", new[] { "Dim i As Long", "For i = 1 To 2", "Debug.Print i", "Fin: Next", "Debug.Print \"AFTER\"" }, new[] { "1", "2", "AFTER" })]
    [DataRow("Wend", new[] { "Dim i As Long", "While i < 2", "i = i + 1", "Fin: Wend", "Debug.Print i" }, new[] { "2" })]
    [DataRow("End Select", new[] { "Select Case 1", "Case 1: Debug.Print \"one\"", "Fin: End Select", "Debug.Print \"AFTER\"" }, new[] { "one", "AFTER" })]
    [DataRow("End With", new[] { "Dim c As New Collection", "With c", ".Add 1", "Fin: End With", "Debug.Print c.Count" }, new[] { "1" })]
    [DataRow("Else", new[] { "If False Then", "Debug.Print \"T\"", "Fin: Else", "Debug.Print \"E\"", "End If" }, new[] { "E" })]
    [DataRow("ElseIf", new[] { "If False Then", "Debug.Print \"T\"", "Fin: ElseIf True Then", "Debug.Print \"EI\"", "End If" }, new[] { "EI" })]
    [DataRow("Loop Until", new[] { "Do", "Debug.Print \"once\"", "Fin: Loop Until True" }, new[] { "once" })]
    [DataRow("a line number", new[] { "If True Then", "Debug.Print \"T\"", "10 End If", "Debug.Print \"AFTER\"" }, new[] { "T", "AFTER" })]
    [DataRow("a line number and a colon", new[] { "If True Then", "Debug.Print \"T\"", "10: End If", "Debug.Print \"AFTER\"" }, new[] { "T", "AFTER" })]
    public async Task ALabel_RightBeforeTheEndOfABlock_IsALabel(string end, string[] body, string[] printed)
        => CollectionAssert.AreEqual(printed, await Run(body), end);

    [TestMethod]
    public async Task TheNameOfAProcedure_AndAColon_AtTheStartOfALine_IsALabel()
        => CollectionAssert.AreEqual(new[] { "0" }, await Run("Greeted = 0", "PrintGreeting: Debug.Print Greeted"));

    [TestMethod]
    public async Task TheNameOfAProcedure_AndAColon_AfterAStatement_IsACall()
        => CollectionAssert.AreEqual(new[] { "1", "42" }, await Run("Greeted = 0", "Debug.Print 1: PrintGreeting: Debug.Print Greeted"));

    [TestMethod]
    public async Task TheNameOfAProcedure_OnALineOfItsOwn_IsACall()
        => CollectionAssert.AreEqual(new[] { "42" }, await Run("Greeted = 0", "PrintGreeting", "Debug.Print Greeted"));
}
