using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>RD-VBAL §5.6.13.1</strong>: an argument written <c>ByVal</c> is valid where <strong>MS-VBA</strong> compiles it - before an argument of a <c>ByVal</c>
/// parameter, where it changes nothing, and of an <c>As Any</c> or a <c>Declare</c>'s parameter - and is the compile error MS-VBA reports anywhere else, not only
/// valid for an external procedure as <strong>MS-VBAL §5.6.13.1</strong> says. Observed in Excel.
/// </summary>
[TestClass]
public sealed class CallSiteByValTests
{
    private static readonly (string Name, string Source) Counter = ("Counter", ClassModule("Counter",
        "Option Explicit", "Public Value As Long",
        "Public Sub Add(ByRef n As Long)", "    n = n + 100", "    Value = Value + n", "End Sub",
        "Public Sub AddV(ByVal n As Long)", "    Value = Value + n", "End Sub",
        "Public Function Twice(ByVal n As Long) As Long", "    Twice = n * 2", "End Function",
        "Public Property Let Item(ByVal i As Long, ByVal v As Long)", "    Value = i * 10 + v", "End Property"));

    private static readonly (string Name, string Source) Globals = ("Globals", StandardModule("Globals",
        "Option Explicit",
        "Public Function Twice(ByVal n As Long) As Long", "    Twice = n * 2", "End Function",
        "Public Sub Bump(ByRef n As Long)", "    n = n + 1", "End Sub",
        "Public Function SeenLong(ByRef n As Long) As String", "    SeenLong = TypeName(n) & \":\" & CStr(n)", "End Function",
        "Public Function SeenVariant(ByRef v As Variant) As String", "    SeenVariant = TypeName(v) & \":\" & VarType(v)", "End Function",
        "Public Function SeenVariantByVal(ByVal v As Variant) As String", "    SeenVariantByVal = TypeName(v) & \":\" & VarType(v)", "End Function",
        "Public Sub Grow(ByRef s As String)", "    s = s & \"!\"", "End Sub",
        "Public Sub Renew(ByRef o As Object)", "    Set o = New Collection", "End Sub",
        "Public Sub BumpArray(ByRef a() As Long)", "    a(0) = a(0) + 1", "End Sub",
        "Public Function Optional1(Optional ByRef n As Long = 5) As Long", "    Optional1 = n", "End Function",
        "Public Function Many(ParamArray xs() As Variant) As Long", "    Many = UBound(xs) + 1", "End Function"));

    private static string Program(string body)
        => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPrivate acc As String\r\nPrivate Sub P(ByVal v As Variant)\r\n    acc = acc & CStr(v) & \"|\"\r\nEnd Sub\r\n"
            + $"Public Sub Main()\r\nOn Error Resume Next\r\n{string.Join("\r\n", body.Split(" ;; "))}\r\nDebug.Print acc\r\nEnd Sub\r\n";

    private static Task<string[]> ErrorsOf(string body) => LoadErrorsAsync([Counter], [Globals], Program(body));

    private static async Task<string> RunAsync(string body)
        => string.Join(" ", await ModuleWorkspace.RunAsync([Counter], [Globals], Program(body)));

    [TestMethod]
    [DataRow("Dim x As Long ;; Bump ByVal x", "Type mismatch", DisplayName = "a ByRef parameter")]
    [DataRow("Dim s As String ;; Grow ByVal s", "Type mismatch", DisplayName = "a ByRef String")]
    [DataRow("Dim o As Object ;; Renew ByVal o", "Type mismatch", DisplayName = "a ByRef Object")]
    [DataRow("Dim a(0) As Long ;; BumpArray ByVal a", "Array argument must be ByRef", DisplayName = "an array parameter")]
    [DataRow("Dim d As Double ;; P SeenLong(ByVal d)", "Type mismatch", DisplayName = "a ByRef Long given a Double")]
    [DataRow("Dim l As Long ;; P SeenVariant(ByVal l)", "Type mismatch", DisplayName = "a ByRef Variant")]
    [DataRow("Dim c As New Collection ;; P SeenVariant(ByVal c)", "Type mismatch", DisplayName = "a ByRef Variant given an object")]
    [DataRow("Dim k As New Counter, x As Long ;; k.Add ByVal x", "Type mismatch", DisplayName = "a class's ByRef parameter")]
    [DataRow("P Optional1(ByVal 9)", "Type mismatch", DisplayName = "an optional ByRef parameter")]
    [DataRow("P Many(ByVal 1, ByVal 2)", "Type mismatch", DisplayName = "a ParamArray")]
    [DataRow("P Array(ByVal 1)(0)", "Type mismatch", DisplayName = "the Array keyword")]
    [DataRow("Dim x As Long ;; Call Bump(ByVal x)", "Type mismatch", DisplayName = "with Call")]
    [DataRow("P Left(ByVal \"abc\", 1)", "Type mismatch", DisplayName = "a library Variant parameter")]
    [DataRow("P UCase(ByVal \"a\")", "Type mismatch", DisplayName = "UCase's Variant")]
    [DataRow("P TypeName(ByVal 1)", "Type mismatch", DisplayName = "TypeName")]
    [DataRow("Dim v As Variant ;; P IsMissing(ByVal v)", "Type mismatch", DisplayName = "IsMissing")]
    [DataRow("Dim c As New Collection ;; c.Add ByVal 5", "Type mismatch", DisplayName = "Collection.Add")]
    [DataRow("P Hex(ByVal 255)", "Type mismatch", DisplayName = "Hex")]
    [DataRow("P Round(ByVal 1.5)", "Type mismatch", DisplayName = "Round")]
    [DataRow("P Replace(ByVal \"abc\", \"b\", \"x\")", "Type mismatch", DisplayName = "Replace's Expression")]
    [DataRow("P Replace(\"abc\", ByVal \"b\", \"x\")", "Type mismatch", DisplayName = "Replace's Find")]
    [DataRow("P Replace(\"abc\", \"b\", \"x\", ByVal 1)", "Type mismatch", DisplayName = "Replace's Start, a Long")]
    [DataRow("P Format$(ByVal 1)", "Type mismatch", DisplayName = "Format$")]
    [DataRow("P StrConv(ByVal \"a\", 1)", "Type mismatch", DisplayName = "StrConv")]
    [DataRow("Dim c As New Collection ;; c.Add 1 ;; P c.Item(ByVal 1)", "Type mismatch", DisplayName = "Collection.Item")]
    [DataRow("P CDec(ByVal 1)", "Type mismatch", DisplayName = "CDec, no reserved name")]
    [DataRow("P Trim(ByVal \" a \")", "Type mismatch", DisplayName = "Trim")]
    [DataRow("P Str(ByVal 12)", "Type mismatch", DisplayName = "Str")]
    [DataRow("P Hex$(ByVal 255)", "Type mismatch", DisplayName = "Hex$")]
    [DataRow("Dim o As Object ;; Set o = New Collection ;; o.Add ByVal 1", "Type mismatch", DisplayName = "a late-bound call")]
    [DataRow("Dim k As Object ;; Set k = New Counter ;; P k.Twice(ByVal 4)", "Type mismatch", DisplayName = "a late-bound ByVal parameter")]
    [DataRow("P InStr(ByVal \"abc\", \"b\")", "Syntax error", DisplayName = "InStr")]
    [DataRow("P StrComp(ByVal \"a\", \"b\")", "Syntax error", DisplayName = "StrComp")]
    [DataRow("P StrComp(\"a\", \"b\", ByVal 1)", "Syntax error", DisplayName = "StrComp's Compare")]
    [DataRow("P InStrB(ByVal \"ab\", \"b\")", "Syntax error", DisplayName = "InStrB")]
    [DataRow("P Int(ByVal 1.5)", "Syntax error", DisplayName = "Int")]
    [DataRow("P CStr(ByVal 1)", "Syntax error", DisplayName = "CStr")]
    [DataRow("P Len(ByVal \"abc\")", "Syntax error", DisplayName = "Len")]
    [DataRow("P Abs(ByVal -3)", "Syntax error", DisplayName = "Abs")]
    [DataRow("P Fix(ByVal 1.5)", "Syntax error", DisplayName = "Fix")]
    [DataRow("P CInt(ByVal 1)", "Syntax error", DisplayName = "CInt")]
    [DataRow("P LenB(ByVal \"ab\")", "Syntax error", DisplayName = "LenB")]
    [DataRow("P String(ByVal 2, \"a\")", "Syntax error", DisplayName = "String")]
    [DataRow("Dim a(0) As Long ;; P a(ByVal 0)", "Syntax error", DisplayName = "an index of an array")]
    [DataRow("Dim a(0) As Long ;; P LBound(ByVal a)", "Syntax error", DisplayName = "LBound")]
    public async Task WhereMsVbaRefusesByVal_ItIsTheCompileErrorMsVbaReports(string body, string message)
        => StringAssert.Contains(string.Join(" / ", await ErrorsOf(body)), message);

    [TestMethod]
    [DataRow("P Twice(ByVal 2.7) ;; P Twice((2.7))", "6|6|", DisplayName = "a ByVal parameter, let-coerced as without it")]
    [DataRow("Dim c As New Collection ;; c.Add 5 ;; P SeenVariantByVal(ByVal c)", "Collection:9|", DisplayName = "an object, not its default member's value")]
    [DataRow("Dim k As New Counter ;; k.Item(ByVal 2) = 3 ;; P k.Value", "23|", DisplayName = "a Property Let's index")]
    [DataRow("P Twice(n:=ByVal 2)", "4|", DisplayName = "a named argument")]
    [DataRow("P Twice(ByVal (2))", "4|", DisplayName = "a parenthesized value")]
    [DataRow("Call Twice(ByVal 2) ;; P \"ok\"", "ok|", DisplayName = "with Call")]
    [DataRow("Dim k As New Counter ;; k.AddV ByVal 3 ;; P k.Value", "3|", DisplayName = "a class's ByVal parameter")]
    [DataRow("Dim k As New Counter ;; k.AddV n:=ByVal 3 ;; P k.Value", "3|", DisplayName = "named")]
    [DataRow("Dim v As Variant ;; v = 2.5 ;; P SeenVariantByVal(ByVal v)", "Double:5|", DisplayName = "a Variant")]
    [DataRow("P SeenVariantByVal(ByVal Nothing)", "Nothing:9|", DisplayName = "Nothing")]
    [DataRow("Dim x As Long ;; x = 3 ;; P Twice(ByVal x) ;; P x", "6|3|", DisplayName = "a variable")]
    [DataRow("Dim k As New Counter ;; P k.Twice(ByVal 4)", "8|", DisplayName = "a class's function, early-bound")]
    [DataRow("P Left(\"abc\", ByVal 1)", "a|", DisplayName = "a library Long")]
    [DataRow("P Left$(ByVal \"abc\", 1)", "a|", DisplayName = "a library String")]
    [DataRow("P Chr(ByVal 65)", "A|", DisplayName = "Chr")]
    [DataRow("P Sqr(ByVal 4)", "2|", DisplayName = "Sqr")]
    [DataRow("P UCase$(ByVal \"a\")", "A|", DisplayName = "UCase$")]
    [DataRow("P Asc(ByVal \"A\")", "65|", DisplayName = "Asc")]
    [DataRow("Err.Raise ByVal 5 ;; P Err.Number", "5|", DisplayName = "Err.Raise")]
    [DataRow("P Mid(\"abcd\", ByVal 2, 1)", "b|", DisplayName = "Mid's Start")]
    [DataRow("P Len(Space(ByVal 2))", "2|", DisplayName = "Space")]
    [DataRow("P Split(ByVal \"a b\")(0)", "a|", DisplayName = "Split")]
    [DataRow("P Year(DateSerial(ByVal 2000, 1, 1))", "2000|", DisplayName = "DateSerial")]
    [DataRow("P InStrRev(ByVal \"abc\", \"b\")", "2|", DisplayName = "InStrRev")]
    [DataRow("P Mid$(ByVal \"abcd\", 2, 1)", "b|", DisplayName = "Mid$")]
    [DataRow("P Mid$(\"abc\", ByVal 2)", "bc|", DisplayName = "Mid$'s Start")]
    [DataRow("P Trim$(ByVal \" a \")", "a|", DisplayName = "Trim$")]
    [DataRow("P Val(ByVal \"12\")", "12|", DisplayName = "Val")]
    public async Task WhereTheParameterIsByVal_ByValChangesNothing(string body, string expected)
    {
        Assert.IsEmpty(await ErrorsOf(body));
        Assert.AreEqual(expected, await RunAsync(body));
    }
}
