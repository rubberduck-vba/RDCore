using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Set</c> of a property (<strong>MS-VBAL §5.4.3.9</strong>) of an object a parameter declared as an interface refers to, or of the object an object
/// field of a record holds: compiled code calls its <c>Property Set</c>, the value evaluated before the object, with no support call. A field that holds
/// <c>Nothing</c> is the interpreter's, which raises error 91. A program runs as it does in the interpreter.
/// </summary>
[TestClass]
public sealed class CompiledPropertySetOwnerTests
{
    private static readonly (string Name, string Source)[] Classes =
    [
        ("IListener", ModuleWorkspace.ClassModule("IListener", "Option Explicit", "Public Sub Heard()", "End Sub")),
        ("ICommands", ModuleWorkspace.ClassModule("ICommands", "Option Explicit",
            "Public Property Get Events() As IListener",
            "End Property",
            "Public Property Set Events(ByVal Value As IListener)",
            "End Property",
            "Public Property Get Size() As Long",
            "End Property",
            "Public Property Let Size(ByVal Value As Long)",
            "End Property")),
        ("View", ModuleWorkspace.ClassModule("View", "Option Explicit",
            "Implements ICommands",
            "Public Name As String",
            "Private mEvents As IListener",
            "Private Property Get ICommands_Events() As IListener",
            "    Set ICommands_Events = mEvents",
            "End Property",
            "Private Property Set ICommands_Events(ByVal Value As IListener)",
            "    Debug.Print Name & \" set \" & TypeName(Value)",
            "    Set mEvents = Value",
            "End Property",
            "Private Property Get ICommands_Size() As Long",
            "End Property",
            "Private Property Let ICommands_Size(ByVal Value As Long)",
            "    Debug.Print Name & \" size \" & Value",
            "End Property")),
        ("Adapter", ModuleWorkspace.ClassModule("Adapter",
            "Attribute VB_PredeclaredId = True",
            "Option Explicit",
            "Implements IListener",
            "Private Type TAdapter",
            "    Count As Long",
            "    GridView As ICommands",
            "    Spare As ICommands",
            "End Type",
            "Private this As TAdapter",
            "Public Function Create(ByVal View As ICommands) As Adapter",
            "    With New Adapter",
            "        Set .GridView = View",
            "        Set View.Events = .Self",
            "        Set Create = .Self",
            "    End With",
            "End Function",
            "Public Property Get Self() As Adapter",
            "    Set Self = Me",
            "End Property",
            "Public Property Get GridView() As ICommands",
            "    Set GridView = this.GridView",
            "End Property",
            "Public Property Set GridView(ByVal Value As ICommands)",
            "    Set this.GridView = Value",
            "End Property",
            "Public Sub Attach(ByVal View As ICommands, ByVal Listener As IListener)",
            "    Set View.Events = Listener",
            "End Sub",
            "Public Sub Detach()",
            "    Set this.GridView.Events = Nothing",
            "End Sub",
            "Public Sub Keep(ByVal Spare As ICommands)",
            "    Set this.Spare = Spare",
            "End Sub",
            "Public Sub Swap()",
            "    Set this.GridView.Events = Swapped()",
            "End Sub",
            "Private Function Swapped() As IListener",
            "    Set this.GridView = this.Spare",
            "    Set Swapped = Me",
            "End Function",
            "Public Function Unbound() As Long",
            "    On Error GoTo Failed",
            "    Set this.Spare.Events = Me",
            "    Exit Function",
            "Failed:",
            "    Unbound = Err.Number",
            "End Function",
            "Public Sub Resize(ByVal Size As Long)",
            "    Dim I As Long",
            "    For I = 1 To Size",
            "        this.GridView.Size = I * 10",
            "    Next",
            "End Sub",
            "Private Sub IListener_Heard()",
            "End Sub")),
    ];

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // the views each named, then given to a new Adapter.
    private static readonly string[] Views =
    [
        "    Dim First As View, Second As View",
        "    Set First = New View",
        "    First.Name = \"first\"",
        "    Set Second = New View",
        "    Second.Name = \"second\"",
    ];

    // the program run both ways, printing expected; then the statements the procedure named had the interpreter run as support calls. Under the random
    // walk-back any activation may be handed to the interpreter, which runs the statements after.
    private static async Task AssertSetAsync(string program, string procedure, long supportCalls, string expected)
    {
        var compiled = await AssertAlikeAsync(program, Classes, expected);
        Assert.IsTrue(compiled.TryGetValue(procedure, out var result), $"{procedure} was not called");
        var body = result.Body ?? throw new AssertFailedException($"{procedure}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
        if (CompiledExecution.HandsOverAtRandom)
        {
            return;
        }

        Assert.AreEqual(supportCalls, body.SupportCallsMade, $"{procedure}'s support calls");
    }

    // a parameter declared as an interface: the Property Set of the class that implements it is called, given the object.
    [TestMethod]
    public async Task APropertySetOfAParameterDeclaredAsAnInterface_IsCalledByCompiledCode()
        => await AssertSetAsync(Program([
            "Public Sub Main()",
            .. Views,
            "    Dim A As Adapter",
            "    Set A = New Adapter",
            "    A.Attach First, A",
            "    A.Attach Second, Nothing",
            "End Sub"]), "Attach", supportCalls: 0, expected: "first set Adapter ; second set Nothing");

    // the factory a class's default instance has, with the object of its With block given to the view.
    [TestMethod]
    public async Task APropertySetOfAParameter_GivenTheObjectOfAWithBlock_IsCalledByCompiledCode()
        => await AssertSetAsync(Program([
            "Public Sub Main()",
            .. Views,
            "    Dim A As Adapter",
            "    Set A = Adapter.Create(First)",
            "    Debug.Print A.GridView Is First",
            "End Sub"]), "Create", supportCalls: 0, expected: "first set Adapter ; True");

    // an object field of a record: the Property Set of the object the field holds is called, given Nothing.
    [TestMethod]
    public async Task APropertySetOfARecordFieldsObject_IsCalledByCompiledCode()
        => await AssertSetAsync(Program([
            "Public Sub Main()",
            .. Views,
            "    Dim A As Adapter",
            "    Set A = Adapter.Create(First)",
            "    A.Detach",
            "End Sub"]), "Detach", supportCalls: 0, expected: "first set Adapter ; first set Nothing");

    // the value evaluated before the object (MS-VBAL §5.4.3.9): the call in the value puts another view in the field, which is the one given it.
    [TestMethod]
    public async Task TheValue_IsEvaluatedBeforeTheRecordFieldsObject()
        => await AssertSetAsync(Program([
            "Public Sub Main()",
            .. Views,
            "    Dim A As Adapter",
            "    Set A = Adapter.Create(First)",
            "    A.Keep Second",
            "    A.Swap",
            "    Debug.Print A.GridView Is Second",
            "End Sub"]), "Swap", supportCalls: 0, expected: "first set Adapter ; second set Adapter ; True");

    // a field that holds Nothing: the interpreter is given the statement, which raises error 91, handled by the procedure.
    [TestMethod]
    public async Task APropertySetOfAFieldThatHoldsNothing_IsError91()
        => await AssertSetAsync(Program([
            "Public Sub Main()",
            "    Dim A As Adapter",
            "    Set A = New Adapter",
            "    Debug.Print A.Unbound",
            "End Sub"]), "Unbound", supportCalls: 0, expected: "91");

    // a Property Let of a record field's object (MS-VBAL §5.4.3.8), in a loop: called by compiled code each time, given the value.
    [TestMethod]
    public async Task APropertyLetOfARecordFieldsObject_IsCalledByCompiledCode()
        => await AssertSetAsync(Program([
            "Public Sub Main()",
            .. Views,
            "    Dim A As Adapter",
            "    Set A = Adapter.Create(First)",
            "    A.Resize 3",
            "End Sub"]), "Resize", supportCalls: 0, expected: "first size 10 ; first size 20 ; first size 30");
}
