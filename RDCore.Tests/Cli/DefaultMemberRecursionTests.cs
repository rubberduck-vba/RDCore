using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// An argument list given to an object whose default member takes no parameters (<strong>MS-VBAL §5.6.13</strong>): the default member is called with
/// none, and the arguments apply to what it returns, the default member of the object it returns or an element of the array. Each row is what MS-VBA does
/// (recorded in Excel, 64-bit).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.13 Index Expressions")]
public sealed class DefaultMemberRecursionTests
{
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Outer", ClassModule("Outer", "Option Explicit", "Private c As New Collection",
            "Public Property Get Inner() As Collection", "Attribute Inner.VB_UserMemId = 0", "    If c.Count = 0 Then c.Add 7, \"k\"", "    Set Inner = c", "End Property")),
        ("Arr", ClassModule("Arr", "Option Explicit",
            "Public Property Get Items() As Variant", "Attribute Items.VB_UserMemId = 0", "    Items = Array(10, 20, 30)", "End Property")),
        ("Chain", ClassModule("Chain", "Option Explicit", "Private o As New Outer",
            "Public Property Get Inner() As Outer", "Attribute Inner.VB_UserMemId = 0", "    Set Inner = o", "End Property")),
        ("Fn", ClassModule("Fn", "Option Explicit", "Private c As New Collection",
            "Public Function Make() As Collection", "Attribute Make.VB_UserMemId = 0", "    If c.Count = 0 Then c.Add 7, \"k\"", "    Set Make = c", "End Function")),
    ];

    // o, a, ch and f are early-bound; x, xa and xc the same classes late-bound. The row's error, and the value it read.
    private static string Program(string statement)
        => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n" +
           "    Dim o As New Outer, x As Object, a As New Arr, xa As Object, ch As New Chain, xc As Object, f As New Fn, v As Variant\r\n" +
           "    Set x = New Outer: Set xa = New Arr: Set xc = New Chain\r\n" +
           "    On Error Resume Next\r\n" +
           $"    {statement}\r\n" +
           "    Debug.Print Err.Number & \" \" & v\r\n" +
           "End Sub\r\n";

    [TestMethod]
    [DataRow("v = o(\"k\")", "0 7", DisplayName = "early-bound, by key")]
    [DataRow("v = o(1)", "0 7", DisplayName = "early-bound, by index")]
    [DataRow("v = x(\"k\")", "0 7", DisplayName = "late-bound, by key")]
    [DataRow("v = a(1)", "0 20", DisplayName = "early-bound, the default member returns an array")]
    [DataRow("v = xa(1)", "451", DisplayName = "late-bound, the default member returns an array")]
    [DataRow("v = ch(\"k\")", "0 7", DisplayName = "early-bound, two levels")]
    [DataRow("v = xc(\"k\")", "0 7", DisplayName = "late-bound, two levels")]
    [DataRow("v = f(\"k\")", "0 7", DisplayName = "early-bound, a default function")]
    [DataRow("v = o(\"nope\")", "5", DisplayName = "early-bound, a key the collection does not have")]
    public async Task AnArgumentListGivenToAParameterlessDefaultMember_AppliesToWhatItReturns(string statement, string expected)
    {
        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            var output = await RunAsync(Classes, Program(statement), engine);
            Assert.AreEqual(expected, string.Join(" | ", output), engine.ToString());
        }
    }

    // Bag's default Item has a Property Get, a Property Let and a Property Set, and says which one was last called; Outer2's default member returns its Bag,
    // and Chain2's returns its Outer2.
    private static readonly (string Name, string Source)[] AssignedClasses =
    [
        Classes[0],
        ("Bag", ClassModule("Bag", "Option Explicit", "Public Got As String",
            "Public Property Get Item(ByVal k As Variant) As Variant", "Attribute Item.VB_UserMemId = 0", "    Item = Got", "End Property",
            "Public Property Let Item(ByVal k As Variant, ByVal v As Variant)", "    Got = \"let \" & k & \"=\" & v", "End Property",
            "Public Property Set Item(ByVal k As Variant, ByVal v As Object)", "    Got = \"set \" & k & \" \" & TypeName(v)", "End Property")),
        ("Outer2", ClassModule("Outer2", "Option Explicit", "Private b As New Bag",
            "Public Property Get Inner() As Bag", "Attribute Inner.VB_UserMemId = 0", "    Set Inner = b", "End Property",
            "Public Function Last() As String", "    Last = b.Got", "End Function")),
        ("Chain2", ClassModule("Chain2", "Option Explicit", "Private o As New Outer2",
            "Public Property Get Inner() As Outer2", "Attribute Inner.VB_UserMemId = 0", "    Set Inner = o", "End Property",
            "Public Function Last() As String", "    Last = o.Last", "End Function")),
    ];

    private static string AssignedProgram(string statement, string last)
        => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n" +
           "    Dim o As New Outer, o2 As New Outer2, x2 As Object, ch2 As New Chain2, xc2 As Object, col As New Collection\r\n" +
           "    Set x2 = New Outer2: Set xc2 = New Chain2\r\n" +
           "    On Error Resume Next\r\n" +
           $"    {statement}\r\n" +
           $"    Debug.Print Err.Number & \" \" & {last}\r\n" +
           "End Sub\r\n";

    [TestMethod]
    [DataRow("o2(\"k\") = 5", "o2.Last", "0 let k=5", DisplayName = "Let, early-bound")]
    [DataRow("Set o2(\"k\") = col", "o2.Last", "0 set k Collection", DisplayName = "Set, early-bound")]
    [DataRow("x2(\"k\") = 5", "x2.Last", "0 let k=5", DisplayName = "Let, late-bound")]
    [DataRow("Set x2(\"k\") = col", "x2.Last", "450", DisplayName = "Set, late-bound")]
    [DataRow("ch2(\"k\") = 5", "ch2.Last", "0 let k=5", DisplayName = "Let, early-bound, two levels")]
    [DataRow("xc2(\"k\") = 5", "xc2.Last", "0 let k=5", DisplayName = "Let, late-bound, two levels")]
    [DataRow("Set xc2(\"k\") = col", "xc2.Last", "450", DisplayName = "Set, late-bound, two levels")]
    [DataRow("o(\"k\") = 9", "o(\"k\")", "424 7", DisplayName = "Let, the default member returns a Collection")]
    public async Task AnAssignmentThroughAParameterlessDefaultMember_AssignsWhatItReturns(string statement, string last, string expected)
    {
        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            var output = await RunAsync(AssignedClasses, AssignedProgram(statement, last), engine);
            Assert.AreEqual(expected, string.Join(" | ", output), engine.ToString());
        }
    }
}
