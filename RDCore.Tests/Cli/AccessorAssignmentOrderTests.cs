using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// The order an assignment whose target is a call evaluates its parts in: a property, a public variable of an object, a default member given an index,
/// an element of an array a <c>Variant</c> holds. The assignment is the call of the accessor, the value its last argument, and its arguments are evaluated
/// as the arguments of a call statement are (<c>ArgumentOrder</c>), as <strong>MS-VBA</strong> does it (observed in Excel, 64-bit). MS-VBAL §5.4.3.8 and
/// §5.4.3.9 do not say. <c>Ix</c>, <c>Vl</c>, <c>Ob</c>, <c>Tg</c>, <c>Tg2</c>, <c>BagOf</c> and the accessors write their tag to <c>Trail</c>, which
/// shows the order.
/// </summary>
[TestClass]
public sealed class AccessorAssignmentOrderTests
{
    private static readonly (string Name, string Source) Box = ("Box", ModuleWorkspace.ClassModule("Box",
        "Option Explicit",
        "Public Tag As String",
        "Public Value As Long",
        "Private mItems(1 To 3) As Long",
        "Private mKids(1 To 3) As Box",
        "Private mChild As Box",
        "Public Property Get Item(ByVal K As Long) As Long", "    Item = mItems(K)", "End Property",
        "Public Property Let Item(ByVal K As Long, ByVal V As Long)", "    Trail = Trail & \"let(\" & K & \",\" & V & \") \"", "    mItems(K) = V",
        "End Property",
        "Public Property Get Kid(ByVal K As Long) As Box", "    Set Kid = mKids(K)", "End Property",
        "Public Property Set Kid(ByVal K As Long, ByVal V As Box)", "    Trail = Trail & \"set(\" & K & \",\" & V.Tag & \") \"", "    Set mKids(K) = V",
        "End Property",
        "Public Property Get Child() As Box", "    Set Child = mChild", "End Property",
        "Public Property Set Child(ByVal V As Box)", "    Trail = Trail & \"child(\" & V.Tag & \") \"", "    Set mChild = V", "End Property",
        "Public Property Get ChildTag() As String", "    If Not mChild Is Nothing Then ChildTag = mChild.Tag", "End Property",
        "Public Property Get KidL(ByVal K As Long) As Box", "    Trail = Trail & \"kid(\" & K & \") \"", "    Set KidL = mKids(K)", "End Property",
        "Public Property Let Amount(ByVal V As Long)", "    Trail = Trail & \"amount(\" & V & \") \"", "End Property"));

    private static readonly (string Name, string Source) Bag = ("Bag", ModuleWorkspace.ClassModule("Bag",
        "Option Explicit",
        "Private mItems(1 To 3) As Long",
        "Public Property Get Item(ByVal K As Long) As Long", "Attribute Item.VB_UserMemId = 0", "    Item = mItems(K)", "End Property",
        "Public Property Let Item(ByVal K As Long, ByVal V As Long)", "Attribute Item.VB_UserMemId = 0",
        "    Trail = Trail & \"bag(\" & K & \",\" & V & \") \"", "    mItems(K) = V", "End Property"));

    private static readonly string[] Procedures =
    [
        "Private Sub P(ByVal x As Variant)", "    acc = acc & CStr(x) & \"|\"", "End Sub",
        "Private Function Ix(ByVal Tag As String, ByVal I As Long) As Long", "    Trail = Trail & Tag & \" \"", "    Ix = I", "End Function",
        "Private Function Vl(ByVal Tag As String, ByVal V As Long) As Long", "    Trail = Trail & Tag & \" \"", "    Vl = V", "End Function",
        "Private Function Ob(ByVal Tag As String) As Box", "    Trail = Trail & Tag & \" \"", "    Set Ob = New Box", "    Ob.Tag = Tag", "End Function",
        "Private Function Tg(ByVal Tag As String) As Box", "    Trail = Trail & Tag & \" \"", "    Set Tg = New Box", "    Tg.Tag = Tag", "End Function",
        "Private Function Tg2(ByVal N As Long) As Box", "    Trail = Trail & \"t\" & N & \" \"", "    Set Tg2 = New Box", "End Function",
        "Private Function BagOf(ByVal Tag As String) As Bag", "    Trail = Trail & Tag & \" \"", "    Set BagOf = New Bag", "End Function",
        "Private Function BumpI(ByRef I As Long) As Long", "    I = I + 1", "    BumpI = I", "End Function",
        "Private Function Swap(ByRef X As Box) As Long", "    Set X = Ob(\"b\")", "    Swap = 5", "End Function",
        "Private Function SwapO(ByRef X As Box) As Box", "    Set X = Ob(\"b\")", "    Set SwapO = Ob(\"c\")", "End Function",
    ];

    // the body runs in a procedure of its own; Main prints what it collected.
    private static async Task<string> RunAsync(string body, VBExecutionEngine engine)
        => string.Join(" ", await ModuleWorkspace.RunAsync([Box, Bag], string.Join("\r\n",
            ["Attribute VB_Name = \"Program\"", "Option Explicit", "Public Trail As String", "Private acc As String", .. Procedures,
                "Private Sub Body()", .. body.Split(" ;; "), "End Sub",
                "Public Sub Main()", "    Body", "    Debug.Print acc", "End Sub", ""]), engine));

    private static async Task AssertBothEnginesAsync(string body, string expected)
    {
        Assert.AreEqual(expected, await RunAsync(body, VBExecutionEngine.Interpreter), "interpreter");
        Assert.AreEqual(expected, await RunAsync(body, VBExecutionEngine.Compiled), "compiled");
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.4.3.8 Let Statement")]
    [DataRow("Dim B As New Box ;; B.Item(Ix(\"i\", 1)) = Vl(\"v\", 5) ;; P Trail", "i v let(1,5) |", DisplayName = "a property of an object variable")]
    [DataRow("Tg(\"t\").Item(Ix(\"i\", 1)) = Vl(\"v\", 5) ;; P Trail", "i v t let(1,5) |", DisplayName = "a property of what a function returns")]
    [DataRow("Dim B As New Box, I As Long ;; I = 1 ;; B.Item(I) = BumpI(I) * 10 ;; P B.Item(1) & \",\" & B.Item(2)", "0,20|",
        DisplayName = "a property whose argument the value changes")]
    [DataRow("Dim G As New Bag ;; G(Ix(\"i\", 1)) = Vl(\"v\", 5) ;; P Trail", "i v bag(1,5) |", DisplayName = "a default member of an object variable")]
    [DataRow("BagOf(\"t\")(Ix(\"i\", 1)) = Vl(\"v\", 5) ;; P Trail", "i v t bag(1,5) |", DisplayName = "a default member of what a function returns")]
    [DataRow("Dim G As New Bag, I As Long ;; I = 1 ;; G(I) = BumpI(I) * 10 ;; P G(1) & \",\" & G(2)", "0,20|",
        DisplayName = "a default member whose argument the value changes")]
    [DataRow("Dim O(1 To 2) As Box ;; Set O(1) = New Box ;; O(Ix(\"i\", 1)).Item(Ix(\"j\", 2)) = Vl(\"v\", 5) ;; P Trail", "j v i let(2,5) |",
        DisplayName = "a property of an element of an array")]
    [DataRow("Dim B As New Box ;; Set B.Kid(1) = New Box ;; Trail = \"\" ;; B.Kid(Ix(\"i\", 1)).Item(Ix(\"j\", 2)) = Vl(\"v\", 5) ;; P Trail",
        "j i v let(2,5) |", DisplayName = "a property of a property")]
    [DataRow("Dim B As New Box ;; Set B.Kid(1) = New Box ;; Trail = \"\" ;; B.KidL(Ix(\"i\", 1)).Item(Ix(\"j\", 2)) = Vl(\"v\", 5) ;; P Trail",
        "j i v kid(1) let(2,5) |", DisplayName = "a property of a property that writes its tag")]
    [DataRow("Dim B As New Box ;; Set B.Kid(1) = New Box ;; Trail = \"\" ;; B.KidL(1).Item(Ix(\"j\", 2)) = Vl(\"v\", 5) ;; P Trail",
        "j v kid(1) let(2,5) |", DisplayName = "a property of a property given a literal")]
    [DataRow("Tg2(Ix(\"i\", 1)).Item(Ix(\"j\", 2)) = Vl(\"v\", 5) ;; P Trail", "j v i t1 let(2,5) |",
        DisplayName = "a property of what a function given a call returns")]
    [DataRow("Tg2(1).Item(Ix(\"j\", 2)) = Vl(\"v\", 5) ;; P Trail", "j v t1 let(2,5) |",
        DisplayName = "a property of what a function given a literal returns")]
    [DataRow("Dim V As Variant ;; V = Array(0, 0, 0) ;; V(Ix(\"i\", 1)) = Vl(\"v\", 5) ;; P Trail ;; P V(1)", "i v |5|",
        DisplayName = "an element of an array a Variant holds")]
    [DataRow("Dim V As Variant ;; V = Array(0, 0, 0) ;; V(Ix(\"i\", 1)) = Vl(\"v\", 5) + 0 ;; P Trail", "i v |",
        DisplayName = "an element of an array a Variant holds, given an expression")]
    [DataRow("Dim V As Variant, I As Long ;; V = Array(0, 0, 0) ;; I = 1 ;; V(I) = BumpI(I) * 10 ;; P V(1) & \",\" & V(2)", "0,20|",
        DisplayName = "an element of an array a Variant holds, its subscript changed by the value")]
    [DataRow("Dim V As Variant, I As Long ;; V = Array(0, 0, 0, 0) ;; I = 1 ;; V(BumpI(I)) = BumpI(I) * 10 ;; P V(2) & \",\" & V(3)", "30,0|",
        DisplayName = "an element of an array a Variant holds, a subscript and a value that change the same variable")]
    [DataRow("Dim V As Variant ;; ReDim V(1 To 3) ;; V(Ix(\"i\", 1)) = Vl(\"v\", 5) ;; P Trail", "i v |",
        DisplayName = "an element of a Variant redimensioned as an array")]
    public async Task AnIndexedTargetThatIsACall_IsCalledWithTheValueAsItsLastArgument(string body, string expected)
        => await AssertBothEnginesAsync(body, expected);

    [TestMethod]
    [TestCategory("MS-VBAL 5.4.3.8 Let Statement")]
    [DataRow("Tg(\"t\").Amount = Vl(\"v\", 5) ;; P Trail", "v t amount(5) |", DisplayName = "a property of what a function returns")]
    [DataRow("Tg(\"t\").Value = Vl(\"v\", 5) ;; P Trail", "v t |", DisplayName = "a public variable of what a function returns")]
    [DataRow("Dim O(1 To 2) As Box ;; Set O(1) = New Box ;; O(Ix(\"i\", 1)).Amount = Vl(\"v\", 5) ;; P Trail", "v i amount(5) |",
        DisplayName = "a property of an element of an array")]
    [DataRow("Dim B As New Box ;; Set B.Kid(1) = New Box ;; Trail = \"\" ;; B.KidL(Ix(\"i\", 1)).Amount = Vl(\"v\", 5) ;; P Trail",
        "i v kid(1) amount(5) |", DisplayName = "a property of a property given a call")]
    [DataRow("Dim B As New Box ;; Set B.Kid(1) = New Box ;; Trail = \"\" ;; B.KidL(Ix(\"i\", 1)).Value = Vl(\"v\", 5) ;; P Trail", "i v kid(1) |",
        DisplayName = "a public variable of a property given a call")]
    [DataRow("Dim B As Box, K As Box ;; Set B = Ob(\"a\") ;; Set K = B ;; B.Value = Swap(B) ;; P K.Value & \",\" & B.Value", "0,5|",
        DisplayName = "a public variable of an object variable the value sets")]
    public async Task AMemberTargetThatIsACall_IsCalledAfterTheValue(string body, string expected) => await AssertBothEnginesAsync(body, expected);

    [TestMethod]
    [TestCategory("MS-VBAL 5.4.3.9 Set Statement")]
    [DataRow("Dim B As New Box ;; Set B.Kid(Ix(\"i\", 1)) = Ob(\"v\") ;; P Trail", "i v set(1,v) |", DisplayName = "a property of an object variable")]
    [DataRow("Set Tg(\"t\").Kid(Ix(\"i\", 1)) = Ob(\"v\") ;; P Trail", "i v t set(1,v) |", DisplayName = "a property of what a function returns")]
    [DataRow("Set Tg(\"t\").Child = Ob(\"v\") ;; P Trail", "v t child(v) |", DisplayName = "a property with no argument of what a function returns")]
    [DataRow("Dim B As New Box ;; Set B.Kid(1) = New Box ;; Trail = \"\" ;; Set B.KidL(Ix(\"i\", 1)).Child = Ob(\"v\") ;; P Trail",
        "i v kid(1) child(v) |", DisplayName = "a property of a property given a call")]
    [DataRow("Dim B As Box, K As Box ;; Set B = Ob(\"a\") ;; Set K = B ;; Set B.Child = SwapO(B) ;; P K.ChildTag & \",\" & B.ChildTag", ",c|",
        DisplayName = "a property of an object variable the value sets")]
    public async Task ASetOfATargetThatIsACall_IsCalledWithTheValueAsItsLastArgument(string body, string expected)
        => await AssertBothEnginesAsync(body, expected);

    [TestMethod]
    [TestCategory("MS-VBAL 5.4.3.8 Let Statement")]
    [DataRow("Dim B As Box ;; On Error Resume Next ;; B.Item(Ix(\"i\", 1)) = Vl(\"v\", 5) ;; P Err.Number ;; P Trail", "91|i v |",
        DisplayName = "a property of Nothing, after the arguments")]
    public async Task AnErrorOfAnAccessorAssignment_IsRaisedOnceItsArgumentsAreEvaluated(string body, string expected)
        => await AssertBothEnginesAsync(body, expected);
}
