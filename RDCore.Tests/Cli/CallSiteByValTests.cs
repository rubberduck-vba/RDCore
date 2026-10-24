using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>RD-VBAL §5.6.13.1</strong>: an argument written <c>ByVal</c> is valid where <strong>MS-VBA</strong> compiles it - before an argument of a <c>ByVal</c>
/// parameter, where it changes nothing, and of an <c>As Any</c> or a <c>Declare</c>'s parameter - and is the compile error MS-VBA reports anywhere else, not only
/// valid for an external procedure as <strong>MS-VBAL §5.6.13.1</strong> says. Recorded in Excel (oracle\s22: grid, grid2, grid3).
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
    [DataRow("Dim x As Long ;; Bump ByVal x", "Type mismatch", DisplayName = "C01 a ByRef parameter")]
    [DataRow("Dim s As String ;; Grow ByVal s", "Type mismatch", DisplayName = "C02 a ByRef String")]
    [DataRow("Dim o As Object ;; Renew ByVal o", "Type mismatch", DisplayName = "C03 a ByRef Object")]
    [DataRow("Dim a(0) As Long ;; BumpArray ByVal a", "Array argument must be ByRef", DisplayName = "C04 an array parameter")]
    [DataRow("Dim d As Double ;; P SeenLong(ByVal d)", "Type mismatch", DisplayName = "C06 a ByRef Long given a Double")]
    [DataRow("Dim l As Long ;; P SeenVariant(ByVal l)", "Type mismatch", DisplayName = "C10 a ByRef Variant")]
    [DataRow("Dim c As New Collection ;; P SeenVariant(ByVal c)", "Type mismatch", DisplayName = "C11 a ByRef Variant given an object")]
    [DataRow("Dim k As New Counter, x As Long ;; k.Add ByVal x", "Type mismatch", DisplayName = "C15 a class's ByRef parameter")]
    [DataRow("P Optional1(ByVal 9)", "Type mismatch", DisplayName = "C17 an optional ByRef parameter")]
    [DataRow("P Many(ByVal 1, ByVal 2)", "Type mismatch", DisplayName = "C18 a ParamArray")]
    [DataRow("P Array(ByVal 1)(0)", "Type mismatch", DisplayName = "C29 the Array keyword")]
    [DataRow("Dim x As Long ;; Call Bump(ByVal x)", "Type mismatch", DisplayName = "C30 with Call")]
    [DataRow("P Left(ByVal \"abc\", 1)", "Type mismatch", DisplayName = "C20 a library Variant parameter")]
    [DataRow("P UCase(ByVal \"a\")", "Type mismatch", DisplayName = "C21 UCase's Variant")]
    [DataRow("P TypeName(ByVal 1)", "Type mismatch", DisplayName = "C23 TypeName")]
    [DataRow("Dim v As Variant ;; P IsMissing(ByVal v)", "Type mismatch", DisplayName = "C24 IsMissing")]
    [DataRow("Dim c As New Collection ;; c.Add ByVal 5", "Type mismatch", DisplayName = "C25 Collection.Add")]
    [DataRow("P Hex(ByVal 255)", "Type mismatch", DisplayName = "L04 Hex")]
    [DataRow("P Round(ByVal 1.5)", "Type mismatch", DisplayName = "L06 Round")]
    [DataRow("P Replace(ByVal \"abc\", \"b\", \"x\")", "Type mismatch", DisplayName = "L13 Replace's Expression")]
    [DataRow("P Replace(\"abc\", ByVal \"b\", \"x\")", "Type mismatch", DisplayName = "M16 Replace's Find")]
    [DataRow("P Replace(\"abc\", \"b\", \"x\", ByVal 1)", "Type mismatch", DisplayName = "M17 Replace's Start, a Long")]
    [DataRow("P Format$(ByVal 1)", "Type mismatch", DisplayName = "L15 Format$")]
    [DataRow("P StrConv(ByVal \"a\", 1)", "Type mismatch", DisplayName = "L19 StrConv")]
    [DataRow("Dim c As New Collection ;; c.Add 1 ;; P c.Item(ByVal 1)", "Type mismatch", DisplayName = "L20 Collection.Item")]
    [DataRow("P CDec(ByVal 1)", "Type mismatch", DisplayName = "M10 CDec, no reserved name")]
    [DataRow("P Trim(ByVal \" a \")", "Type mismatch", DisplayName = "M21 Trim")]
    [DataRow("P Str(ByVal 12)", "Type mismatch", DisplayName = "M24 Str")]
    [DataRow("P Hex$(ByVal 255)", "Type mismatch", DisplayName = "M27 Hex$")]
    [DataRow("Dim o As Object ;; Set o = New Collection ;; o.Add ByVal 1", "Type mismatch", DisplayName = "L28 a late-bound call")]
    [DataRow("Dim k As Object ;; Set k = New Counter ;; P k.Twice(ByVal 4)", "Type mismatch", DisplayName = "L35 a late-bound ByVal parameter")]
    [DataRow("P InStr(ByVal \"abc\", \"b\")", "Syntax error", DisplayName = "C22 InStr")]
    [DataRow("P StrComp(ByVal \"a\", \"b\")", "Syntax error", DisplayName = "L12 StrComp")]
    [DataRow("P StrComp(\"a\", \"b\", ByVal 1)", "Syntax error", DisplayName = "M18 StrComp's Compare")]
    [DataRow("P InStrB(ByVal \"ab\", \"b\")", "Syntax error", DisplayName = "M15 InStrB")]
    [DataRow("P Int(ByVal 1.5)", "Syntax error", DisplayName = "L17 Int")]
    [DataRow("P CStr(ByVal 1)", "Syntax error", DisplayName = "L18 CStr")]
    [DataRow("P Len(ByVal \"abc\")", "Syntax error", DisplayName = "L23 Len")]
    [DataRow("P Abs(ByVal -3)", "Syntax error", DisplayName = "B01 Abs")]
    [DataRow("P Fix(ByVal 1.5)", "Syntax error", DisplayName = "M01 Fix")]
    [DataRow("P CInt(ByVal 1)", "Syntax error", DisplayName = "M04 CInt")]
    [DataRow("P LenB(ByVal \"ab\")", "Syntax error", DisplayName = "M03 LenB")]
    [DataRow("P String(ByVal 2, \"a\")", "Syntax error", DisplayName = "M28 String")]
    [DataRow("Dim a(0) As Long ;; P a(ByVal 0)", "Syntax error", DisplayName = "M26 an index of an array")]
    [DataRow("Dim a(0) As Long ;; P LBound(ByVal a)", "Syntax error", DisplayName = "M29 LBound")]
    public async Task WhereMsVbaRefusesByVal_ItIsTheCompileErrorMsVbaReports(string body, string message)
        => StringAssert.Contains(string.Join(" / ", await ErrorsOf(body)), message);

    [TestMethod]
    [DataRow("P Twice(ByVal 2.7) ;; P Twice((2.7))", "6|6|", DisplayName = "C05 a ByVal parameter, let-coerced as without it")]
    [DataRow("Dim c As New Collection ;; c.Add 5 ;; P SeenVariantByVal(ByVal c)", "Collection:9|", DisplayName = "C13 an object, not its default member's value")]
    [DataRow("Dim k As New Counter ;; k.Item(ByVal 2) = 3 ;; P k.Value", "23|", DisplayName = "C16 a Property Let's index")]
    [DataRow("P Twice(n:=ByVal 2)", "4|", DisplayName = "C19 a named argument")]
    [DataRow("P Twice(ByVal (2))", "4|", DisplayName = "C26 a parenthesized value")]
    [DataRow("Call Twice(ByVal 2) ;; P \"ok\"", "ok|", DisplayName = "L27 with Call")]
    [DataRow("Dim k As New Counter ;; k.AddV ByVal 3 ;; P k.Value", "3|", DisplayName = "L29 a class's ByVal parameter")]
    [DataRow("Dim k As New Counter ;; k.AddV n:=ByVal 3 ;; P k.Value", "3|", DisplayName = "L30 named")]
    [DataRow("Dim v As Variant ;; v = 2.5 ;; P SeenVariantByVal(ByVal v)", "Double:5|", DisplayName = "L31 a Variant")]
    [DataRow("P SeenVariantByVal(ByVal Nothing)", "Nothing:9|", DisplayName = "L32 Nothing")]
    [DataRow("Dim x As Long ;; x = 3 ;; P Twice(ByVal x) ;; P x", "6|3|", DisplayName = "L33 a variable")]
    [DataRow("Dim k As New Counter ;; P k.Twice(ByVal 4)", "8|", DisplayName = "L34 a class's function, early-bound")]
    [DataRow("P Left(\"abc\", ByVal 1)", "a|", DisplayName = "L01 a library Long")]
    [DataRow("P Left$(ByVal \"abc\", 1)", "a|", DisplayName = "L02 a library String")]
    [DataRow("P Chr(ByVal 65)", "A|", DisplayName = "L03 Chr")]
    [DataRow("P Sqr(ByVal 4)", "2|", DisplayName = "L05 Sqr")]
    [DataRow("P UCase$(ByVal \"a\")", "A|", DisplayName = "L07 UCase$")]
    [DataRow("P Asc(ByVal \"A\")", "65|", DisplayName = "L08 Asc")]
    [DataRow("Err.Raise ByVal 5 ;; P Err.Number", "5|", DisplayName = "L09 Err.Raise")]
    [DataRow("P Mid(\"abcd\", ByVal 2, 1)", "b|", DisplayName = "L10 Mid's Start")]
    [DataRow("P Len(Space(ByVal 2))", "2|", DisplayName = "L11 Space")]
    [DataRow("P Split(ByVal \"a b\")(0)", "a|", DisplayName = "L14 Split")]
    [DataRow("P Year(DateSerial(ByVal 2000, 1, 1))", "2000|", DisplayName = "L16 DateSerial")]
    [DataRow("P InStrRev(ByVal \"abc\", \"b\")", "2|", DisplayName = "L21 InStrRev")]
    [DataRow("P Mid$(ByVal \"abcd\", 2, 1)", "b|", DisplayName = "L22 Mid$")]
    [DataRow("P Mid$(\"abc\", ByVal 2)", "bc|", DisplayName = "M19 Mid$'s Start")]
    [DataRow("P Trim$(ByVal \" a \")", "a|", DisplayName = "M22 Trim$")]
    [DataRow("P Val(ByVal \"12\")", "12|", DisplayName = "M23 Val")]
    public async Task WhereTheParameterIsByVal_ByValChangesNothing(string body, string expected)
    {
        Assert.IsEmpty(await ErrorsOf(body));
        Assert.AreEqual(expected, await RunAsync(body));
    }
}
