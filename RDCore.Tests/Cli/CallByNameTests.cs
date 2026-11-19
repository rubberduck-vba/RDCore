using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>CallByName</c> (<strong>MS-VBAL §6.1.2.8.1.1</strong>) calls a member of an object by its name, late-bound: the member is found on the default interface of the
/// object's class, and invoked as a call written in source invokes it.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.8.1.1 CallByName")]
public sealed class CallByNameTests
{
    private static readonly (string Name, string Source)[] Calculator =
    [
        ("Calculator", ClassModule("Calculator",
            "Option Explicit",
            "Public Total As Long",
            "Private mName As String",
            "Private mFriend As Object",
            "Public Function Add(ByVal a As Long, ByVal b As Long) As Long",
            "    Add = a + b",
            "End Function",
            "Public Function Greet(Optional ByVal who As String) As String",
            "    Greet = \"Hello \" & who",
            "End Function",
            "Public Sub Accumulate(ByVal amount As Long)",
            "    Total = Total + amount",
            "End Sub",
            "Public Property Get Name() As String",
            "    Name = mName",
            "End Property",
            "Public Property Let Name(ByVal value As String)",
            "    mName = value",
            "End Property",
            "Public Property Get Buddy() As Object",
            "    Set Buddy = mFriend",
            "End Property",
            "Public Property Set Buddy(ByVal value As Object)",
            "    Set mFriend = value",
            "End Property")),
    ];

    private static Task<string[]> Run(params string[] lines)
        => RunAsync(Calculator, $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nDim c As Calculator\r\nSet c = New Calculator\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

    [TestMethod]
    public async Task AFunction_IsCalledAsAMethod_WithItsArgumentsCoercedToItsParameters()
        => CollectionAssert.AreEqual(new[] { "5", "42" }, await Run(
            "Debug.Print CallByName(c, \"Add\", VbMethod, 2, 3)",
            "Debug.Print CallByName(c, \"add\", VbMethod, \"40\", 2)"));

    [TestMethod]
    public async Task ASub_IsCalledAsAMethod_ForWhatItDoes()
        => CollectionAssert.AreEqual(new[] { "10" }, await Run(
            "CallByName c, \"Accumulate\", VbMethod, 4",
            "CallByName c, \"Accumulate\", VbMethod, 6",
            "Debug.Print c.Total"));

    [TestMethod]
    public async Task AProperty_IsLetAndGot_ByName()
        => CollectionAssert.AreEqual(new[] { "Duck", "Duck" }, await Run(
            "CallByName c, \"Name\", VbLet, \"Duck\"",
            "Debug.Print CallByName(c, \"Name\", VbGet)",
            "Debug.Print c.Name"));

    // the value assigned is the last argument, and the property's last parameter, whatever Optional index parameters come before it - Microsoft Forms' List(Optional
    // pvargIndex, Optional pvargColumn), which MVVM's bindings assign by name.
    [TestMethod]
    [DataRow("CallByName t, \"Item\", VbLet, \"v\"", "7 v", DisplayName = "the index omitted")]
    [DataRow("CallByName t, \"Item\", VbLet, 2, \"w\"", "2 w", DisplayName = "the index given")]
    [DataRow("Set o = New Collection: CallByName t, \"Thing\", VbSet, o", "7 Collection", DisplayName = "Set, the index omitted")]
    public async Task APropertyWithAnOptionalIndex_IsAssignedTheLastArgument_ByName(string call, string printed)
        => CollectionAssert.AreEqual(new[] { printed }, await RunAsync(
            [("Table", ClassModule("Table", "Option Explicit",
                "Public Property Let Item(Optional ByVal Index As Long = 7, ByVal RHS As Variant)", "    Debug.Print Index & \" \" & RHS", "End Property",
                "Public Property Set Thing(Optional ByVal Index As Long = 7, ByVal RHS As Object)", "    Debug.Print Index & \" \" & TypeName(RHS)", "End Property"))],
            $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nDim t As New Table\r\nDim o As Object\r\n{call}\r\nEnd Sub\r\n"));

    [TestMethod]
    public async Task AnObjectProperty_IsSet_ByName()
        => CollectionAssert.AreEqual(new[] { "set", "True" }, await Run(
            "Dim other As Calculator",
            "Set other = New Calculator",
            "CallByName c, \"Buddy\", VbSet, other",
            "Debug.Print \"set\"",
            "Dim same As Boolean",
            "same = (c.Buddy Is other)",
            "Debug.Print same"));

    [TestMethod]
    public async Task APublicVariable_IsGot_ByName()
        => CollectionAssert.AreEqual(new[] { "7" }, await Run(
            "c.Total = 7",
            "Debug.Print CallByName(c, \"Total\", VbGet)"));

    [TestMethod]
    public async Task AnOmittedOptionalArgument_ThatDeclaresNoDefault_IsItsTypesDefault()
        => CollectionAssert.AreEqual(new[] { "Hello", "Hello you" }, await Run(
            "Debug.Print CallByName(c, \"Greet\", VbMethod)",
            "Debug.Print CallByName(c, \"Greet\", VbMethod, \"you\")"));

    [TestMethod]
    public async Task AnObject_PassedToAParamArray_IsTheObject_NotItsDefaultValue()
        => CollectionAssert.AreEqual(new[] { "9" }, await RunAsync(Calculator,
            "Attribute VB_Name = \"Program\"\r\n" +
            "Private Function First(ParamArray items() As Variant) As Long\r\nDim item As Calculator\r\nSet item = items(0)\r\nFirst = item.Total\r\nEnd Function\r\n" +
            "Public Sub Main()\r\nDim c As Calculator\r\nSet c = New Calculator\r\nc.Total = 9\r\nDebug.Print First(c)\r\nEnd Sub\r\n"));

    private static async Task<string> FailsWith(params string[] lines)
    {
        string? error = null;
        await InspectAsync(Calculator,
            $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nDim c As Calculator\r\nSet c = New Calculator\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n",
            (_, result, _) =>
            {
                Assert.AreEqual(RDCore.SDK.Platform.Protocol.ExecutionOutcome.RuntimeError, result.Outcome, result.ErrorMessage);
                error = result.ErrorCode;
                return Task.CompletedTask;
            });
        return error!;
    }

    [TestMethod]
    public async Task AMemberTheObjectDoesNotHave_IsError438()
        => StringAssert.Contains(await FailsWith("Debug.Print CallByName(c, \"Subtract\", VbMethod, 1, 2)"), "438");

    // MS-VBA, observed in Excel.
    [TestMethod]
    public async Task AFunction_CalledAsAProperty_IsError450()
        => StringAssert.Contains(await FailsWith("CallByName c, \"Add\", VbLet, 1"), "450");

    // MS-VBA, observed in Excel, where a call written in source on Nothing is 91.
    [TestMethod]
    public async Task Nothing_IsError5()
        => Assert.AreEqual("VBR00005", await FailsWith("Set c = Nothing", "Debug.Print CallByName(c, \"Add\", VbMethod, 1, 2)"));

    [TestMethod]
    public async Task TooManyArguments_IsError450()
        => StringAssert.Contains(await FailsWith("Debug.Print CallByName(c, \"Add\", VbMethod, 1, 2, 3)"), "450");

    [TestMethod]
    public async Task AMissingArgument_IsError449()
        => StringAssert.Contains(await FailsWith("Debug.Print CallByName(c, \"Add\", VbMethod, 1)"), "449");

    #region what MS-VBA does, recorded in Excel (oracle\s40 B01-B34)

    private static readonly (string Name, string Source)[] Classes =
    [
        ("Point", ClassModule("Point", "Public X As Long", "Public Inner As Point")),
        ("Opt", ClassModule("Opt",
            "Public Items As Collection",
            "Public Tag As Variant",
            "Public Function F(Optional ByVal n As Long = 7) As Long", "    F = n", "End Function",
            "Public Function G(Optional v As Variant) As Boolean", "    G = IsMissing(v)", "End Function",
            "Public Function K(Optional ByVal d As Double = 2.5, Optional ByVal t As String = \"x\") As String", "    K = d & t", "End Function",
            "Public Function M(ByVal a As Long, Optional ByVal b As Long = 10) As Long", "    M = a + b", "End Function",
            "Public Function V(Optional ByVal x As Variant = 3) As Variant", "    V = x", "End Function")),
    ];

    // runs the statements with `pt` a new Point and `o` a new Opt, an error resumed and printed.
    private static Task<string[]> Probe(params string[] lines)
        => RunAsync(Classes, string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            "Public Sub Main()",
            "    Dim pt As New Point, o As New Opt",
            "    On Error Resume Next",
            "    " + string.Join("\r\n    ", lines),
            "End Sub",
            string.Empty));

    [TestMethod]
    [DataRow("CallByName pt, \"X\", VbLet, 5", "Debug.Print pt.X", "5")]
    [DataRow("CallByName pt, \"X\", VbLet, \"12\"", "Debug.Print pt.X", "12")]
    [DataRow("CallByName o, \"Tag\", VbLet, \"t\"", "Debug.Print o.Tag", "t")]
    [DataRow("Dim q As New Point: q.X = 9: CallByName pt, \"Inner\", VbSet, q", "Debug.Print pt.Inner.X", "9")]
    [DataRow("CallByName o, \"Items\", VbSet, New Collection", "Debug.Print TypeName(o.Items)", "Collection")]
    [DataRow("CallByName o, \"Tag\", VbSet, New Collection", "Debug.Print TypeName(o.Tag)", "Collection")]
    public async Task APublicVariable_IsAssigned_ByName(string assign, string print, string expected)
        => CollectionAssert.AreEqual(new[] { expected }, await Probe(assign, print));

    [TestMethod]
    [DataRow("CallByName pt, \"X\", VbLet", "446", DisplayName = "no value")]
    [DataRow("CallByName pt, \"X\", VbLet, 1, 2", "451", DisplayName = "two values")]
    [DataRow("CallByName pt, \"X\", VbLet, \"abc\"", "13", DisplayName = "a value of no type the variable can hold")]
    [DataRow("CallByName pt, \"X\", VbSet, New Point", "451", DisplayName = "a Set into a variable that holds no object")]
    [DataRow("CallByName pt, \"X\", VbSet, 1", "451", DisplayName = "a Set of what is no object into a variable that holds no object")]
    [DataRow("CallByName o, \"Items\", VbSet, 5", "13", DisplayName = "a Set of what is no object")]
    [DataRow("CallByName pt, \"Inner\", VbSet, New Collection", "13", DisplayName = "a Set of an object of another class")]
    [DataRow("CallByName pt, \"Inner\", VbLet, 1", "91", DisplayName = "a Let into a variable of an object that is Nothing")]
    [DataRow("Set pt.Inner = New Point: CallByName pt, \"Inner\", VbLet, 1", "438", DisplayName = "a Let into a variable of an object with no default member")]
    [DataRow("CallByName o, \"Tag\", VbLet, New Collection", "450", DisplayName = "a Let of an object whose default member takes arguments")]
    [DataRow("CallByName pt, \"X\", VbMethod", "438", DisplayName = "a variable called as a method")]
    [DataRow("Debug.Print CallByName(o, \"F\", VbGet)", "450", DisplayName = "a function got as a property")]
    [DataRow("Debug.Print CallByName(o, \"M\", VbGet, 1)", "450", DisplayName = "a function got as a property, with an argument")]
    public async Task AnAssignmentOrACallOfTheWrongKind_IsTheErrorMSVBARaises(string statement, string expected)
        => CollectionAssert.AreEqual(new[] { expected }, await Probe(statement, "Debug.Print Err.Number"));

    [TestMethod]
    [DataRow("Debug.Print CallByName(o, \"F\", VbMethod)", "7", DisplayName = "a declared default")]
    [DataRow("Debug.Print CallByName(o, \"G\", VbMethod)", "True", DisplayName = "a Variant that declares none is Missing")]
    [DataRow("Debug.Print CallByName(o, \"K\", VbMethod)", "2.5x", DisplayName = "two declared defaults")]
    [DataRow("Debug.Print CallByName(o, \"K\", VbMethod, 1)", "1x", DisplayName = "the first given, the second its default")]
    [DataRow("Debug.Print CallByName(o, \"M\", VbMethod, 1)", "11", DisplayName = "a required argument and a declared default")]
    [DataRow("Debug.Print CallByName(o, \"V\", VbMethod)", "3", DisplayName = "a Variant's declared default")]
    public async Task AnOmittedOptionalArgument_IsWhatACallWrittenInSourceGivesIt(string statement, string expected)
        => CollectionAssert.AreEqual(new[] { expected }, await Probe(statement));

    #endregion

    #region a variable given by reference, recorded in Excel (oracle\s56 G03, s57 C01-C07, D05)

    private static readonly (string Name, string Source)[] Boxes =
    [
        ("Box", ClassModule("Box",
            "Option Explicit",
            "Public Field As Long",
            "Public Sub Twice(ByRef n As Long)", "    n = n * 2", "End Sub",
            "Public Function Half(ByVal n As Long) As Long", "    Half = n \\ 2", "End Function",
            "Public Sub BumpV(n)", "    n = n + 1", "End Sub",
            "Public Sub SetV(n, ByVal w)", "    n = w", "End Sub",
            "Public Function TN(n) As String", "    TN = TypeName(n) & VarType(n)", "End Function")),
    ];

    // runs the statements with `b` a new Box, an error resumed, and prints what each P is given.
    private static Task<string[]> ProbeBox(params string[] lines)
        => RunAsync(Boxes, string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            "Private acc As String",
            "Private Sub P(ByVal x As Variant)", "    acc = acc & CStr(x) & \"|\"", "End Sub",
            "Public Sub Main()",
            "    Dim b As New Box",
            "    On Error Resume Next",
            "    " + string.Join("\r\n    ", lines),
            "    Debug.Print acc",
            "End Sub",
            string.Empty));

    [TestMethod]
    [DataRow("Dim x As Long: x = 3: CallByName b, \"Twice\", VbMethod, x: P x", "6|", DisplayName = "C01 a variable of the parameter's type")]
    [DataRow("Dim a(1) As Long: a(1) = 5: CallByName b, \"Twice\", VbMethod, a(1): P a(1)", "10|", DisplayName = "C02 an element of an array")]
    [DataRow("Dim x As Long: x = 4: CallByName b, \"Twice\", VbMethod, (x): P x", "4|", DisplayName = "C02 a variable in parentheses is a value")]
    [DataRow("b.Field = 3: CallByName b, \"Twice\", VbMethod, b.Field: P b.Field", "3|", DisplayName = "C02 a public variable of an object is a value")]
    [DataRow("Dim v As Variant: v = 3: CallByName b, \"BumpV\", VbMethod, v: P v: P TypeName(v)", "4|Integer|", DisplayName = "C06 a Variant, to a Variant")]
    [DataRow("Dim x As Long: x = 3: CallByName b, \"BumpV\", VbMethod, x: P x: P TypeName(x)", "4|Long|", DisplayName = "C05 a Long, to a Variant")]
    [DataRow("Dim x As Long: x = 3: P CallByName(b, \"TN\", VbMethod, x)", "Long3|", DisplayName = "D05 a Long, read through a Variant")]
    [DataRow("Dim x As Long: x = 1: CallByName b, \"SetV\", VbMethod, x, 2.7: P x: P TypeName(x)", "3|Long|", DisplayName = "D05 a Double written into a Long")]
    [DataRow("Dim s As String: s = \"a\": CallByName b, \"SetV\", VbMethod, s, 5: P s: P TypeName(s)", "5|String|", DisplayName = "D05 a number written into a String")]
    [DataRow("Dim x As Long: x = 3: CallByName b, \"SetV\", VbMethod, x, \"40000\": P x", "40000|", DisplayName = "D05 a String written into a Long")]
    [DataRow("Dim c As New Collection: c.Add 3: CallByName b, \"Twice\", VbMethod, c(1): P c(1)", "3|", DisplayName = "C06 an item of a Collection is a value")]
    public async Task AVariable_IsGivenByReference_AsMSVBAGivesIt(string statements, string printed)
        => CollectionAssert.AreEqual(new[] { printed }, await ProbeBox(statements.Split(": ")));

    [TestMethod]
    [DataRow("Dim i As Integer: i = 3: CallByName b, \"Twice\", VbMethod, i: P i", "3|e13|", DisplayName = "C05 an Integer, to a Long")]
    [DataRow("Dim s As String: s = \"3\": CallByName b, \"Twice\", VbMethod, s: P s", "3|e13|", DisplayName = "C05 a String, to a Long")]
    [DataRow("Dim v As Variant: v = CLng(3): CallByName b, \"Twice\", VbMethod, v: P v", "3|e13|", DisplayName = "C05 a Variant holding a Long, to a Long")]
    [DataRow("Dim x As Long: x = 3: CallByName b, \"SetV\", VbMethod, x, \"x\": P x", "3|e13|", DisplayName = "D05 what a Long cannot hold, written into one")]
    [DataRow("Dim x As Long: x = 3: CallByName b, \"SetV\", VbMethod, x, Null: P x", "3|e94|", DisplayName = "D05 Null written into a Long")]
    [DataRow("Dim i As Integer: CallByName b, \"SetV\", VbMethod, i, 40000: P i", "0|e6|", DisplayName = "D05 what overflows an Integer, written into one")]
    public async Task AVariableOfAnotherType_IsTheErrorMSVBARaises_AndLeftAsItWas(string statements, string printed)
        => CollectionAssert.AreEqual(new[] { printed }, await ProbeBox([.. statements.Split(": "), "P \"e\" & Err.Number"]));

    [TestMethod]
    [DataRow("P CallByName(b, \"Twice\", VbGet, 1)", "450", DisplayName = "a Sub got")]
    [DataRow("CallByName b, \"Twice\", VbLet, 1", "450", DisplayName = "a Sub let")]
    [DataRow("CallByName b, \"Twice\", VbSet, 1", "450", DisplayName = "a Sub set")]
    [DataRow("CallByName b, \"Half\", VbSet, 1", "450", DisplayName = "a Function set")]
    [DataRow("CallByName b, \"Half\", VbLet, 4", "450", DisplayName = "C04 a Function let")]
    [DataRow("CallByName b, \"Field\", VbMethod, 1", "438", DisplayName = "a variable called")]
    [DataRow("CallByName b, \"Nope\", VbGet", "438", DisplayName = "C04 no such member")]
    public async Task AMember_CalledAsWhatItIsNot_IsTheErrorMSVBARaises(string statement, string expected)
        => CollectionAssert.AreEqual(new[] { $"e{expected}|" }, await ProbeBox(statement, "P \"e\" & Err.Number"));

    #endregion
}
