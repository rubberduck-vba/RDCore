using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>String</c> field of variable length of a record, compiled: read as the text it holds, and let-assigned the text as it is (<strong>MS-VBAL
/// §5.4.3.8</strong>), where the record holds it, as a String variable is. A field of a record variable, of the record a class holds its state in, and of a
/// record of a standard module. A field given by reference is the interpreter's to pass, and compiled code reads what the callee assigned it; a
/// fixed-length String field is the interpreter's. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledRecordTextFieldTests
{
    private static readonly string[] Types =
    [
        "Private Type TEntry",
        "    Name As String",
        "    Count As Long",
        "    Code As String * 3",
        "End Type",
    ];

    private static string Module(params string[] lines) => Program([.. Types, .. lines]);

    // a field of a record variable appended to in a loop, compared, joined to other text and to a whole number, and assigned to the function result.
    [TestMethod]
    public async Task AStringFieldOfARecordVariable_IsReadAndWrittenWhereTheRecordHoldsIt()
        => await AssertAlikeAsync(Module(
            "Public Sub Main()",
            "    Debug.Print Built(3)",
            "End Sub",
            "Private Function Built(ByVal N As Long) As String",
            "    Dim E As TEntry, I As Long, S As String",
            "    For I = 1 To N",
            "        E.Name = E.Name & Chr$(64 + I)",
            "        E.Count = E.Count + 1",
            "        If E.Name = \"AB\" Then S = \"[\" & E.Name & \"]\"",
            "    Next",
            "    Built = S & E.Name & E.Count",
            "End Function"), "Built", recoveries: 0, expected: "| [AB]ABC3 |");

    // the record a class holds its state in: a field assigned a parameter, read into what a member returns, and printed between texts.
    [TestMethod]
    public async Task AStringFieldOfTheRecordAClassHolds_IsReadAndWrittenWhereTheObjectHoldsIt()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim S As Ship",
            "    Set S = New Ship",
            "    S.Rename \"Sloop\"",
            "    S.Hit",
            "    S.Rename S.Label",
            "    S.Hit",
            "End Sub"), "Hit", recoveries: 0, classes: [("Ship", ModuleWorkspace.ClassModule("Ship",
                "Option Explicit",
                "Private Type TShip",
                "    Name As String",
                "    Hits As Long",
                "End Type",
                "Private this As TShip",
                "Public Sub Rename(ByVal Value As String)",
                "    this.Name = Value",
                "End Sub",
                "Public Function Label() As String",
                "    Label = \"<\" & this.Name & \">\"",
                "End Function",
                "Public Sub Hit()",
                "    this.Hits = this.Hits + 1",
                "    Debug.Print \"the \" & this.Name & \" is hit \" & this.Hits & \" times\"",
                "End Sub"))], expected: "| the Sloop is hit 1 times ; the <Sloop> is hit 2 times |");

    // a record of a standard module, written by one procedure and read by another, each compiled.
    [TestMethod]
    public async Task AStringFieldOfAModuleRecord_IsReadAndWrittenWhereTheModuleHoldsIt()
        => await CompiledModuleFieldTests.AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        Note \"n\" & I",
            "    Next",
            "    Debug.Print Written()",
            "End Sub"), [("Note", 0), ("Written", 0)], modules: [("Journal", ModuleWorkspace.StandardModule("Journal",
                "Option Explicit",
                "Private Type TJournal",
                "    Entries As String",
                "    Count As Long",
                "End Type",
                "Private Kept As TJournal",
                "Public Sub Note(ByVal Entry As String)",
                "    Kept.Entries = Kept.Entries & Entry & \";\"",
                "    Kept.Count = Kept.Count + 1",
                "End Sub",
                "Public Function Written() As String",
                "    Written = Kept.Count & \":\" & Kept.Entries",
                "End Function"))]);

    // a field given by reference to a String parameter is the field itself (MS-VBAL §5.3.1.11): the interpreter passes it, and compiled code reads what the
    // callee assigned it. Given by value, the callee is given its text.
    [TestMethod]
    public async Task AStringFieldGivenByReference_IsAssignedWhatTheCalleeAssigns()
        => await AssertAlikeAsync(Module(
            "Public Sub Main()",
            "    Debug.Print Passed(2)",
            "End Sub",
            "Private Sub Shout(ByRef S As String)",
            "    S = S & \"!\"",
            "End Sub",
            "Private Sub Echo(ByVal S As String)",
            "    Debug.Print S",
            "End Sub",
            "Private Function Passed(ByVal N As Long) As String",
            "    Dim E As TEntry, I As Long",
            "    E.Name = \"go\"",
            "    For I = 1 To N",
            "        Shout E.Name",
            "        Echo E.Name",
            "        E.Name = E.Name & I",
            "    Next",
            "    Passed = E.Name",
            "End Function"), "Passed", recoveries: 0, expected: "| go! ; go!1! ; go!1!2 |");

    // a fixed-length String field pads or truncates what it is assigned (MS-VBAL §5.5.1.2.5): the interpreter's, which runs each statement that reads or
    // writes it, the Let of the field and the Let the field is read in (two recoveries).
    [TestMethod]
    public async Task AFixedLengthStringField_IsTheInterpreters()
        => await AssertAlikeAsync(Module(
            "Public Sub Main()",
            "    Debug.Print Coded()",
            "End Sub",
            "Private Function Coded() As String",
            "    Dim E As TEntry",
            "    E.Code = \"abcdef\"",
            "    E.Name = E.Code & \"|\"",
            "    Coded = E.Name",
            "End Function"), "Coded", recoveries: 2, expected: "| abc| |");
}
