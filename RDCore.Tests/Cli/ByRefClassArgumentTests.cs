using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// A variable of one class given to a <c>ByRef</c> parameter declared as another, unrelated class compiles, as it does in <strong>MS-VBA</strong> (observed
/// in Excel, 64-bit). When the call is made, the object the variable holds is not of the parameter's class: error 13, raised before the callee runs, and
/// the variable keeps its object. A variable that holds <c>Nothing</c> is passed. MS-VBAL §5.3.1.11 binds a <c>ByRef</c> argument to its parameter, and
/// §5.5.2.2 is the Set-coercion of an object to a class.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.3.1.11 Procedure Invocation Argument Processing")]
public sealed class ByRefClassArgumentTests
{
    private static readonly (string Name, string Source) Animal = ("Animal", ModuleWorkspace.ClassModule("Animal",
        "Option Explicit", "Public Name As String"));

    private static readonly (string Name, string Source) Dog = ("Dog", ModuleWorkspace.ClassModule("Dog", "Option Explicit", "Public Name As String"));

    private static readonly (string Name, string Source) Pup = ("Pup", ModuleWorkspace.ClassModule("Pup", "Option Explicit", "Implements Animal",
        "Private Property Let Animal_Name(ByVal RHS As String)", "End Property", "Private Property Get Animal_Name() As String", "End Property"));

    private static readonly string[] Procedures =
    [
        "Private Sub TakesRef(ByRef A As Animal)", "    Trail = Trail & \"in:\" & TypeName(A) & \" \"", "End Sub",
        "Private Sub TakesRefRead(ByRef A As Animal)", "    Trail = Trail & \"name:\" & A.Name & \" \"", "End Sub",
        "Private Sub TakesRefSet(ByRef A As Animal)", "    Set A = New Animal", "End Sub",
        "Private Sub TakesVal(ByVal A As Animal)", "End Sub",
        "Private Sub TakesObj(ByRef O As Object)", "End Sub",
        "Private Sub P(ByVal x As Variant)", "    acc = acc & CStr(x) & \"|\"", "End Sub",
    ];

    private static string ProgramOf(string body) => string.Join("\r\n",
        ["Attribute VB_Name = \"Program\"", "Option Explicit", "Private Trail As String", "Private acc As String", .. Procedures,
            "Private Sub Body()", .. body.Split(" ;; "), "End Sub",
            "Public Sub Main()", "    Body", "    Debug.Print acc", "End Sub", ""]);

    [TestMethod]
    [DataRow("Dim D As Dog ;; Set D = New Dog ;; TakesRef D", DisplayName = "a variable of another class")]
    [DataRow("Dim D As Animal ;; Set D = New Animal ;; TakesRef D", DisplayName = "a variable of the parameter's class")]
    [DataRow("Dim D As Dog ;; Set D = New Dog ;; TakesVal D", DisplayName = "a variable of another class, to a ByVal parameter")]
    [DataRow("Dim D As Pup ;; Set D = New Pup ;; TakesRef D", DisplayName = "a variable of a class that implements the parameter's")]
    [DataRow("Dim D As Dog ;; Set D = New Dog ;; TakesObj D", DisplayName = "a variable of a class, to a ByRef parameter declared Object")]
    [DataRow("Dim D As Object ;; Set D = New Dog ;; TakesRef D", DisplayName = "a variable declared Object")]
    [DataRow("Dim D As Dog ;; Set D = New Dog ;; TakesRef (D)", DisplayName = "a variable of another class, in parentheses")]
    public async Task AClassVariableGivenToAByRefParameter_Compiles(string body)
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([Animal, Dog, Pup], ProgramOf(body));

        Assert.IsEmpty(errors, string.Join(" | ", errors));
    }

    [TestMethod]
    [DataRow("Dim D As Dog ;; Set D = New Dog ;; D.Name = \"rex\" ;; On Error Resume Next ;; TakesRef D ;; P Err.Number ;; P Trail", "13||",
        DisplayName = "an object of another class")]
    [DataRow("Dim D As Dog ;; Set D = New Dog ;; D.Name = \"rex\" ;; On Error Resume Next ;; TakesRefRead D ;; P Err.Number ;; P Trail", "13||",
        DisplayName = "an object of another class, to a callee that reads it")]
    [DataRow("Dim D As Dog ;; Set D = New Dog ;; On Error Resume Next ;; TakesRefSet D ;; P Err.Number ;; P TypeName(D)", "13|Dog|",
        DisplayName = "an object of another class, to a callee that sets it")]
    [DataRow("Dim D As Dog ;; On Error Resume Next ;; TakesRef D ;; P Err.Number ;; P Trail", "0|in:Nothing |",
        DisplayName = "Nothing, in a variable of another class")]
    public async Task AnObjectOfAnotherClassGivenToAByRefParameter_IsATypeMismatchAtTheCall(string body, string expected)
    {
        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            var printed = await ModuleWorkspace.RunAsync([Animal, Dog, Pup], ProgramOf(body), engine);
            Assert.AreEqual(expected, string.Join(" ", printed), engine.ToString());
        }
    }
}
