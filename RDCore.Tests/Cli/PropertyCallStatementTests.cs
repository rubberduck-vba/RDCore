using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Tests.Cli;

/// <summary>
/// A call statement calls a subroutine or a function (<strong>MS-VBAL §5.4.2.1</strong>): a property named as one is "Invalid use of property" where it is
/// bound early, and run-time error 450 where it is bound late, without calling its Property Get, as MS-VBA has it (recorded in Excel).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.2.1 Call Statement")]
public sealed class PropertyCallStatementTests
{
    private static readonly (string, string)[] Classes =
    [
        ("Thing", ModuleWorkspace.ClassModule("Thing",
            "Public Count As Long",
            "Public Property Get Value() As Long", "Count = Count + 1", "Value = Count", "End Property",
            "Public Property Get Item(ByVal i As Long) As Long", "Count = Count + 10", "Item = i", "End Property",
            "Public Property Let Total(ByVal n As Long)", "Count = n", "End Property",
            "Public Property Get Bags() As Bag", "Set Bags = New Bag", "End Property",
            "Public Function Work() As Long", "Count = Count + 100", "Work = 1", "End Function")),
        ("Bag", ModuleWorkspace.ClassModule("Bag",
            "Public Function Item(ByVal key As Variant) As String", "Attribute Item.VB_UserMemId = 0", "Item = \"<\" & key & \">\"", "End Function")),
    ];

    private static string Program(string body, string members = "")
        => $"Attribute VB_Name = \"Program\"\r\n{members.Replace(" ;; ", "\r\n")}\r\nPublic Sub Main()\r\n{body.Replace(" ;; ", "\r\n")}\r\nEnd Sub\r\n";

    private const string Here = "Public Property Get Here() As Long ;; Here = 7 ;; End Property";

    [TestMethod]
    [DataRow("Dim t As New Thing ;; t.Value", "", DisplayName = "a Property Get")]
    [DataRow("Dim t As New Thing ;; Call t.Value", "", DisplayName = "a Property Get, with Call")]
    [DataRow("Dim t As New Thing ;; Call t.Value()", "", DisplayName = "a Property Get, with Call and an empty argument list")]
    [DataRow("Dim t As New Thing ;; t.Item 1", "", DisplayName = "a Property Get that takes an argument")]
    [DataRow("Dim t As New Thing ;; Call t.Item(1)", "", DisplayName = "a Property Get that takes an argument, with Call")]
    [DataRow("Here", Here, DisplayName = "a Property Get of the module")]
    [DataRow("Call Here", Here, DisplayName = "a Property Get of the module, with Call")]
    [DataRow("Dim t As New Thing ;; With t ;; .Value ;; End With", "", DisplayName = "a Property Get of the target of a With block")]
    [DataRow("Dim t As New Thing ;; t.Total 5", "", DisplayName = "a Property Let")]
    [DataRow("Dim t As New Thing ;; t.Bags", "", DisplayName = "a Property Get that returns an object")]
    public async Task APropertyBoundEarly_IsAnInvalidUseOfProperty(string body, string members)
    {
        var model = await ModuleWorkspace.ModelAsync(Classes, Program(body, members));

        CollectionAssert.AreEqual(new[] { (int)VBCompileErrorId.InvalidUseOfProperty }, model.CompileErrors.Select(error => error.ErrorId).ToArray());
    }

    // MS-VBAL §5.6.13: the argument list of `Call t.Bags(1)` is not one Bags can take, so it indexes the object Bags returns, on its default member: what
    // the statement calls is that function.
    [TestMethod]
    [DataRow("Dim t As New Thing ;; Call t.Bags(1)", DisplayName = "an argument list for the default member of what a Property Get returns")]
    [DataRow("Dim t As New Thing ;; t.Work", DisplayName = "a Function")]
    [DataRow("Dim t As New Thing ;; Call t.Work()", DisplayName = "a Function, with Call")]
    public async Task ACallStatementThatCallsAFunction_IsValid(string body)
        => Assert.IsEmpty((await ModuleWorkspace.ModelAsync(Classes, Program(body))).CompileErrors);

    [TestMethod]
    [DataRow("t.Work", DisplayName = "without Call")]
    [DataRow("Call t.Work", DisplayName = "with Call")]
    [DataRow("Call t.Work()", DisplayName = "with Call and an empty argument list")]
    public async Task AFunctionCalledAsAStatement_IsCalled(string call)
        => CollectionAssert.AreEqual(new[] { "100" }, await ModuleWorkspace.RunAsync(Classes, Program($"Dim t As New Thing ;; {call} ;; Debug.Print t.Count")));

    [TestMethod]
    [DataRow("o.Value", DisplayName = "a Property Get")]
    [DataRow("Call o.Value", DisplayName = "a Property Get, with Call")]
    [DataRow("o.Item 1", DisplayName = "a Property Get that takes an argument")]
    public async Task APropertyBoundLate_IsRunTimeError450_AndItsPropertyGetIsNotCalled(string call)
        => Assert.AreEqual("e450|0", string.Join("|", await ModuleWorkspace.RunAsync(Classes, Program(
            $"On Error Resume Next ;; Dim o As Object ;; Set o = New Thing ;; {call} ;; Debug.Print \"e\" & Err.Number ;; Debug.Print o.Count"))));
}
