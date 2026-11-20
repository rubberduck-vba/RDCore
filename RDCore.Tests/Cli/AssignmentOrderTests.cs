using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// The order an assignment whose target is not a call evaluates its parts in: a variable, an element of an array, a field of a record, an element of
/// an array a record holds. The right side is evaluated first, then the target's index lists, from the last list back to the first, each left to
/// right, as <strong>MS-VBA</strong> does it (observed in Excel, 64-bit). MS-VBAL §5.4.3.8 and §5.4.3.9 do not say. <c>Ix</c>, <c>Vl</c> and
/// <c>Ob</c> write their tag to <c>Trail</c>, which shows the order.
/// </summary>
[TestClass]
public sealed class AssignmentOrderTests
{
    private static readonly (string Name, string Source) Box = ("Box", ModuleWorkspace.ClassModule("Box",
        "Public Tag As String"));

    private static readonly string[] Declarations =
    [
        "Public Trail As String",
        "Public Type RecA", "    Items(1 To 3) As Long", "End Type",
        "Public Type Inner", "    Items(1 To 3) As Long", "End Type",
        "Public Type Outer", "    Kids(1 To 2) As Inner", "    Count As Long", "    Thing As Box", "End Type",
        "Public Type Grid", "    Items(1 To 2, 1 To 2) As Long", "End Type",
        "Public Type Holder", "    Objs(1 To 2) As Box", "End Type",
        "Public Type Dyn", "    Items() As Long", "End Type",
        "Public MRS(1 To 2) As RecA",
        "Private acc As String",
    ];

    private static readonly string[] Procedures =
    [
        "Private Sub P(ByVal x As Variant)", "    acc = acc & CStr(x) & \"|\"", "End Sub",
        "Private Function Ix(ByVal Tag As String, ByVal I As Long) As Long", "    Trail = Trail & Tag & \" \"", "    Ix = I", "End Function",
        "Private Function Vl(ByVal Tag As String, ByVal V As Long) As Long", "    Trail = Trail & Tag & \" \"", "    Vl = V", "End Function",
        "Private Function Ob(ByVal Tag As String) As Box", "    Trail = Trail & Tag & \" \"", "    Set Ob = New Box", "    Ob.Tag = Tag", "End Function",
        "Private Function BumpI(ByRef I As Long) As Long", "    I = I + 1", "    BumpI = I", "End Function",
        "Private Function Bump(ByRef N As Long) As Long", "    N = N + 10", "    Bump = N", "End Function",
        "Private Function Fails(ByVal Tag As String) As Long", "    Trail = Trail & Tag & \" \"", "    Err.Raise 5", "End Function",
        "Private Sub ByRefRec(ByRef R As Outer)", "    R.Kids(Ix(\"k\", 1)).Items(Ix(\"j\", 2)) = Vl(\"v\", 5)", "End Sub",
    ];

    // the body runs in a procedure of its own; Main prints what it collected.
    private static async Task<string> RunAsync(string body, VBExecutionEngine engine)
        => string.Join(" ", await ModuleWorkspace.RunAsync([Box], string.Join("\r\n",
            ["Attribute VB_Name = \"Program\"", "Option Explicit", .. Declarations, .. Procedures,
                "Private Sub Body()", .. body.Split(" ;; "), "End Sub",
                "Public Sub Main()", "    Body", "    Debug.Print acc", "End Sub", ""]), engine));

    private static async Task AssertBothEnginesAsync(string body, string expected)
    {
        Assert.AreEqual(expected, await RunAsync(body, VBExecutionEngine.Interpreter), "interpreter");
        Assert.AreEqual(expected, await RunAsync(body, VBExecutionEngine.Compiled), "compiled");
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.4.3.8 Let Statement")]
    [DataRow("Dim A(1 To 3) As Long ;; A(Ix(\"i\", 1)) = Vl(\"v\", 5) ;; P Trail ;; P A(1)", "v i |5|", DisplayName = "an element of an array")]
    [DataRow("Dim A(1 To 2, 1 To 2) As Long ;; A(Ix(\"i\", 1), Ix(\"j\", 2)) = Vl(\"v\", 5) ;; P Trail ;; P A(1, 2)", "v i j |5|",
        DisplayName = "an element of an array of two dimensions")]
    [DataRow("Dim A(1 To 3) As Long ;; A(Ix(\"i\", Ix(\"j\", 1))) = Vl(\"v\", 5) ;; P Trail", "v j i |", DisplayName = "a subscript that is a call")]
    [DataRow("Dim A() As Long ;; ReDim A(1 To 3) ;; A(Ix(\"i\", 1)) = Vl(\"v\", 5) ;; P Trail", "v i |", DisplayName = "an element of a dynamic array")]
    [DataRow("Dim A(1 To 3) As Long ;; Let A(Ix(\"i\", 1)) = Vl(\"v\", 5) ;; P Trail", "v i |", DisplayName = "an explicit Let")]
    [DataRow("Dim A(1 To 30) As Long, I As Long ;; I = 1 ;; A(BumpI(I)) = BumpI(I) * 10 ;; P A(2) & \",\" & A(3)", "0,20|",
        DisplayName = "a subscript and a value that change the same variable")]
    [DataRow("Dim N As Long ;; N = 1 ;; N = Bump(N) + N ;; P N", "22|", DisplayName = "a variable that the value changes by reference")]
    [DataRow("Dim R As RecA ;; R.Items(Ix(\"i\", 1)) = Vl(\"v\", 5) ;; P Trail ;; P R.Items(1)", "v i |5|", DisplayName = "an element of a record's array")]
    [DataRow("Dim R As RecA, I As Long ;; I = 1 ;; R.Items(I) = BumpI(I) * 10 ;; P R.Items(1) & \",\" & R.Items(2)", "0,20|",
        DisplayName = "an element of a record's array, its subscript changed by the value")]
    [DataRow("Dim RS(1 To 2) As Outer ;; RS(Ix(\"i\", 1)).Count = Vl(\"v\", 5) ;; P Trail ;; P RS(1).Count", "v i |5|",
        DisplayName = "a field of an element of an array of records")]
    [DataRow("Dim RS(1 To 2) As RecA ;; With RS(Ix(\"i\", 1)) ;; .Items(Ix(\"j\", 2)) = Vl(\"v\", 5) ;; End With ;; P Trail ;; P RS(1).Items(2)",
        "i v j |5|", DisplayName = "an element of the array of a With block's record")]
    public async Task ATargetThatIsNotACall_IsEvaluatedAfterTheValue(string body, string expected) => await AssertBothEnginesAsync(body, expected);

    [TestMethod]
    [TestCategory("MS-VBAL 5.4.3.9 Set Statement")]
    [DataRow("Dim O(1 To 2) As Box ;; Set O(Ix(\"i\", 1)) = Ob(\"v\") ;; P Trail ;; P O(1).Tag", "v i |v|", DisplayName = "an element of an array")]
    [DataRow("Dim O(1 To 3) As Box, I As Long ;; I = 1 ;; Set O(I) = Ob(\"v\" & BumpI(I)) ;; P (O(1) Is Nothing) & \",\" & (O(2) Is Nothing)",
        "True,False|", DisplayName = "an element of an array, its subscript changed by the value")]
    [DataRow("Dim RS(1 To 2) As Outer ;; Set RS(Ix(\"i\", 1)).Thing = Ob(\"v\") ;; P Trail ;; P RS(1).Thing.Tag", "v i |v|",
        DisplayName = "a field of an element of an array of records")]
    public async Task ASetOfATargetThatIsNotACall_IsEvaluatedAfterTheValue(string body, string expected) => await AssertBothEnginesAsync(body, expected);

    [TestMethod]
    [TestCategory("MS-VBAL 5.4.3.8 Let Statement")]
    [DataRow("Dim RS(1 To 2) As RecA ;; RS(Ix(\"i\", 1)).Items(Ix(\"j\", 2)) = Vl(\"v\", 5) ;; P Trail ;; P RS(1).Items(2)", "v j i |5|",
        DisplayName = "an element of the array of an element of an array of records")]
    [DataRow("Dim RS(1 To 2) As Outer ;; RS(Ix(\"i\", 1)).Kids(Ix(\"k\", 1)).Items(Ix(\"j\", 2)) = Vl(\"v\", 5) ;; P Trail ;; P RS(1).Kids(1).Items(2)",
        "v j k i |5|", DisplayName = "three index lists")]
    [DataRow("Dim R As Outer ;; R.Kids(Ix(\"k\", 1)).Items(Ix(\"j\", 2)) = Vl(\"v\", 5) ;; P Trail ;; P R.Kids(1).Items(2)", "v j k |5|",
        DisplayName = "a record variable's array of records")]
    [DataRow("Dim RS(1 To 2, 1 To 2) As Grid ;; RS(Ix(\"a\", 1), Ix(\"b\", 2)).Items(Ix(\"c\", 1), Ix(\"d\", 2)) = Vl(\"v\", 5) ;; P Trail"
        + " ;; P RS(1, 2).Items(1, 2)",
        "v c d a b |5|", DisplayName = "index lists of two subscripts")]
    [DataRow("Dim RS(1 To 2) As RecA, I As Long ;; I = 1 ;; RS(BumpI(I)).Items(I) = 5 ;; P RS(1).Items(2) & \",\" & RS(2).Items(1) & \",\" & RS(2).Items(2)",
        "0,5,0|", DisplayName = "a subscript of the first list that changes a variable the last list reads")]
    [DataRow("Dim RS(1 To 2) As Outer, I As Long ;; I = 1 ;; RS(I).Kids(I).Items(I) = BumpI(I) * 10"
        + " ;; P RS(1).Kids(1).Items(1) & \",\" & RS(2).Kids(2).Items(2)",
        "0,20|", DisplayName = "a value that changes the variable every list reads")]
    [DataRow("Dim R As Outer ;; With R ;; .Kids(Ix(\"k\", 1)).Items(Ix(\"j\", 2)) = Vl(\"v\", 5) ;; End With ;; P Trail ;; P R.Kids(1).Items(2)", "v j k |5|",
        DisplayName = "a path from a With block's record")]
    [DataRow("Dim R As Outer ;; ByRefRec R ;; P Trail ;; P R.Kids(1).Items(2)", "v j k |5|", DisplayName = "a path from a record parameter")]
    [DataRow("MRS(Ix(\"i\", 1)).Items(Ix(\"j\", 2)) = Vl(\"v\", 5) ;; P Trail ;; P MRS(1).Items(2)", "v j i |5|",
        DisplayName = "a path from a variable of the module")]
    [DataRow("Program.MRS(Ix(\"i\", 1)).Items(Ix(\"j\", 2)) = Vl(\"v\", 5) ;; P Trail ;; P MRS(1).Items(2)", "v j i |5|",
        DisplayName = "a path from a variable named through its module")]
    [DataRow("Dim RS(1 To 2) As Dyn ;; ReDim RS(1).Items(1 To 3) ;; RS(Ix(\"i\", 1)).Items(Ix(\"j\", 2)) = Vl(\"v\", 5) ;; P Trail ;; P RS(1).Items(2)",
        "v j i |5|", DisplayName = "a dynamic array of a record")]
    public async Task APathThroughRecords_EvaluatesItsIndexListsFromTheLast(string body, string expected) => await AssertBothEnginesAsync(body, expected);

    [TestMethod]
    [TestCategory("MS-VBAL 5.4.3.9 Set Statement")]
    [DataRow("Dim HS(1 To 2) As Holder ;; Set HS(Ix(\"i\", 1)).Objs(Ix(\"j\", 2)) = Ob(\"v\") ;; P Trail ;; P HS(1).Objs(2).Tag", "v j i |v|",
        DisplayName = "an element of the array of an element of an array of records")]
    public async Task ASetOfAPathThroughRecords_EvaluatesItsIndexListsFromTheLast(string body, string expected)
        => await AssertBothEnginesAsync(body, expected);

    [TestMethod]
    [TestCategory("MS-VBAL 5.4.3.8 Let Statement")]
    [DataRow("Dim A(1 To 3) As Long ;; On Error Resume Next ;; A(Ix(\"i\", 1)) = Fails(\"v\") ;; P Err.Number ;; P Trail", "5|v |",
        DisplayName = "a value that raises an error, before the subscript")]
    [DataRow("Dim A(1 To 3) As Long ;; On Error Resume Next ;; A(Ix(\"i\", 9)) = Vl(\"v\", 5) ;; P Err.Number ;; P Trail", "9|v i |",
        DisplayName = "a subscript out of range, after the value")]
    [DataRow("Dim RS(1 To 2) As Outer ;; On Error Resume Next ;; RS(Ix(\"i\", 9)).Kids(Ix(\"k\", 1)).Items(Ix(\"j\", 2)) = Vl(\"v\", 5)"
        + " ;; P Err.Number ;; P Trail", "9|v j k i |", DisplayName = "the first list out of range, after every list")]
    [DataRow("Dim RS(1 To 2) As Outer ;; On Error Resume Next ;; RS(Ix(\"i\", 1)).Kids(Ix(\"k\", 1)).Items(Ix(\"j\", 9)) = Vl(\"v\", 5)"
        + " ;; P Err.Number ;; P Trail", "9|v j k i |", DisplayName = "the last list out of range, after every list")]
    [DataRow("Dim RS(1 To 2) As Outer ;; On Error Resume Next ;; RS(Ix(\"i\", 1)).Kids(Ix(\"k\", 1)).Items(Ix(\"j\", 2)) = Fails(\"v\")"
        + " ;; P Err.Number ;; P Trail", "5|v |", DisplayName = "a value that raises an error, before every list")]
    public async Task AnErrorOfAnAssignment_IsRaisedWhereItsPartIsEvaluated(string body, string expected) => await AssertBothEnginesAsync(body, expected);
}
