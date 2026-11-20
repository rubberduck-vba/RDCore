using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// The order the arguments of a call are evaluated in, when a call among them makes an object, lets go of one, or changes a variable another argument
/// reads: the call is evaluated before the arguments to its right, and the objects are made and let go of as <strong>MS-VBA</strong> does it
/// (observed in Excel, 64-bit). MS-VBAL §5.3.1.11 binds the parameters left to right and does not say when an argument's expression is evaluated.
/// A class's <c>Class_Initialize</c> and <c>Class_Terminate</c> (MS-VBAL §5.3.1.10) and a function that makes an object write to <c>Trail</c>,
/// which shows the order.
/// </summary>
[TestClass]
public sealed class ObjectArgumentOrderTests
{
    internal static readonly (string Name, string Source) Box = ("Box", ClassModule("Box",
        "Public Id As Long", "Public Value As Long",
        "Private Sub Class_Initialize()", "    Made = Made + 1", "    Id = Made", "    Trail = Trail & \"i\" & Id & \" \"", "End Sub",
        "Private Sub Class_Terminate()", "    Trail = Trail & \"t\" & Id & \" \"", "End Sub",
        "Public Function Addr() As LongPtr", "    Addr = VarPtr(Value)", "End Function",
        "Public Sub Touch()", "    Value = Value + 1", "End Sub",
        "Public Function Stable() As Boolean", "    Dim First As LongPtr, I As Long", "    First = VarPtr(Value)",
        "    For I = 1 To 3", "        Touch", "    Next", "    Stable = (VarPtr(Value) = First)", "End Function"));

    internal static readonly (string Name, string Source) Log = ("Log", StandardModule("Log",
        "Public Trail As String", "Public Made As Long",
        "Public Function Tagged(ByVal Tag As String) As Box", "    Trail = Trail & Tag & \" \"", "    Set Tagged = New Box", "End Function",
        "Public Function Pair(ByVal A As Box, ByVal B As Box) As String", "    Pair = A.Id & \",\" & B.Id & \":\" & Trail", "End Function",
        "Public Function Cleared(ByRef X As Box) As Long", "    Set X = Nothing", "    Cleared = 1", "End Function",
        "Public Function IdOf(ByVal X As Box) As Long", "    If Not X Is Nothing Then IdOf = X.Id", "End Function",
        "Public Function Sum2(ByVal A As Long, ByVal B As Long) As String", "    Sum2 = A & \",\" & B", "End Function",
        "Public Function Pick(ByVal A As Box, ByVal B As Box) As Box", "    Set Pick = A", "End Function",
        "Public Function AddrOf(ByVal X As Box) As LongPtr", "    AddrOf = X.Addr", "End Function",
        "Public Function AddrRef(ByRef X As Box) As LongPtr", "    AddrRef = X.Addr", "End Function",
        "Public Function Bump(ByRef N As Long) As Long", "    N = N + 10", "    Bump = N", "End Function",
        "Public Function Swapped(ByRef X As Box) As Box", "    Set Swapped = X", "    Set X = New Box", "End Function"));

    // the body runs in a procedure of its own, which lets go of its locals when it returns: Main prints what it collected, then the trail its end left.
    internal static async Task<string> RunAsync(string body)
        => string.Join(" ", await ModuleWorkspace.RunAsync([Box], [Log], "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPrivate acc As String\r\n"
            + "Private Sub P(ByVal x As Variant)\r\n    acc = acc & CStr(x) & \"|\"\r\nEnd Sub\r\n"
            + $"Private Sub Body()\r\n{string.Join("\r\n", body.Split(" ;; "))}\r\nEnd Sub\r\n"
            + "Public Sub Main()\r\n    Body\r\n    Debug.Print acc\r\nEnd Sub\r\n"));

    [TestMethod]
    [TestCategory("MS-VBAL 5.3.1.11 Procedure Invocation Argument Processing")]
    [DataRow("Dim X As Box ;; Set X = New Box ;; P Sum2(Cleared(X), IdOf(X)) ;; P Trail", "1,0|i1 t1 |",
        DisplayName = "a call that lets go of a variable by reference, left of a call that reads it")]
    [DataRow("Dim X As Box ;; Set X = New Box ;; P Sum2(IdOf(X), Cleared(X)) ;; P Trail", "1,1|i1 t1 |",
        DisplayName = "a call that reads a variable, left of a call that lets go of it by reference")]
    [DataRow("Dim X As Box ;; Set X = New Box ;; P Pair(Swapped(X), X) ;; P Trail", "1,2:i1 i2 |i1 i2 t1 |",
        DisplayName = "a call that gives a variable another object by reference, left of the variable")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(Bump(N), N)", "11,11|", DisplayName = "a call that adds to a variable by reference, left of the variable")]
    [DataRow("Dim X As Box ;; Set X = Tagged(\"x\") ;; Set X = Pick(Tagged(\"a\"), X) ;; P X.Id ;; P Trail", "2|x i1 a i2 t1 |",
        DisplayName = "a Set of what a call returns, given the variable it replaces")]
    public async Task ACallAmongTheArguments_IsEvaluatedBeforeTheArgumentsRightOfIt(string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(body));
}
