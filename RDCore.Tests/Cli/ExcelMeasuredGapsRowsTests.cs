using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// The rows of the gaps card measured in Excel (oracle\s57), each run whole as it was run there: what <c>P</c> prints of each statement under
/// <c>On Error Resume Next</c> is what MS-VBA printed. The class <c>Box</c> is the oracle's, with what a row adds to it.
/// </summary>
[TestClass]
public sealed class ExcelMeasuredGapsRowsTests
{
    private static async Task<string> Run(string boxExtras, string module, string probe)
    {
        string[] Lines(string text) => text.Length == 0 ? [] : text.Split(" ;; ");
        var box = ClassModule("Box",
            ["Option Explicit", "Public Field As Long", .. Lines(boxExtras),
             "Public Sub Twice(ByRef n As Long)", "    n = n * 2", "End Sub",
             "Public Function Half(ByVal n As Long) As Long", "    Half = n \\ 2", "End Function"]);
        var program = StandardModule("Program",
            ["Option Explicit", "Private acc As String", "Private Sub P(ByVal x As Variant)", "    acc = acc & CStr(x) & \"|\"", "End Sub", .. Lines(module),
             "Public Sub Main()", "    On Error Resume Next", .. Lines(probe), "    Debug.Print acc", "End Sub"]);

        return (await RunAsync([("Box", box)], program)).Last().Trim();
    }

    [TestMethod]
    [DataRow("V02", "", "", """P Val("&O177777") ;; P Val("&O100000") ;; P Val("&O77777") ;; P Val("&O37777777777") ;; P Val("&HFFFF.5") ;; P Val(" &H FF") ;; P Val("&hffff") ;; P Val("-&HFFFF") ;; P "e" & Err.Number""",
        "-1|-32768|32767|-1|-1|255|-1|0|e0|")]
    [DataRow("V03", "", "", """P Val("&HFFFFFFFFF") ;; P "e" & Err.Number ;; Err.Clear ;; P Val("&H80000000") ;; P Val("&HFFFF^") ;; P "e" & Err.Number ;; Err.Clear ;; P Val("&HFFFFFFFFFFFFFFFF") ;; P "e" & Err.Number ;; Err.Clear ;; P Val("&HFFFFx") ;; P Val("&H")""",
        "-1|e0|-2147483648|65535|e0|-1|e0|-1|0|")]
    [DataRow("D03", "", "", """P Val("&HFFFF%1") ;; P Val("&HFFFF&H") ;; P Val("&H") ;; P Val("&HG") ;; P Val("&H-1") ;; P Val("  &H FF") ;; P Val("&O8") ;; P Val("&O78") ;; P Val("&H8000%") ;; P Val("&HFFFF^") ;; P "e" & Err.Number""",
        "-1|65535|0|0|0|255|0|7|-32768|65535|e0|")]
    [DataRow("C01", "Public Sub BumpV(n) ;;     n = n + 1 ;; End Sub ;; Public Function Thrice(ByRef n As Long) As Long ;;     n = n * 3 ;;     Thrice = n ;; End Function", "",
        """Dim b As New Box ;; Dim x As Long ;; x = 3 ;; CallByName b, "Twice", VbMethod, x ;; P x ;; P "e" & Err.Number ;; Err.Clear ;; Dim y As Variant ;; y = 3 ;; CallByName b, "BumpV", VbMethod, y ;; P y ;; Dim z As Long ;; z = 2 ;; P CallByName(b, "Thrice", VbMethod, z) ;; P z ;; P "e" & Err.Number""",
        "6|e0|4|6|6|e0|")]
    [DataRow("C03", "", "",
        """Dim o As Object ;; CallByName o, "Twice", VbMethod, 1 ;; P "e" & Err.Number ;; Err.Clear ;; Dim c As Box ;; CallByName c, "Half", VbMethod, 4 ;; P "e" & Err.Number ;; Err.Clear ;; Dim v As Variant ;; CallByName v, "Half", VbMethod, 4 ;; P "e" & Err.Number ;; Err.Clear ;; P CallByName(Nothing, "Half", VbMethod, 4) ;; P "e" & Err.Number ;; Err.Clear ;; v = 5 ;; CallByName v, "Half", VbMethod, 4 ;; P "e" & Err.Number""",
        "e5|e5|e424|e5|e424|")]
    [DataRow("C04", "", "",
        """Dim b As New Box ;; CallByName b, "Nope", VbMethod ;; P "e" & Err.Number ;; Err.Clear ;; P CallByName(b, "Nope", VbGet) ;; P "e" & Err.Number ;; Err.Clear ;; CallByName b, "Nope", VbLet, 1 ;; P "e" & Err.Number ;; Err.Clear ;; P CallByName(b, "Half", VbGet, 4) ;; P "e" & Err.Number ;; Err.Clear ;; CallByName b, "Half", VbMethod ;; P "e" & Err.Number ;; Err.Clear ;; CallByName b, "Field", VbMethod ;; P "e" & Err.Number ;; Err.Clear ;; CallByName b, "Half", VbLet, 4 ;; P "e" & Err.Number ;; Err.Clear ;; CallByName b, "Half", VbMethod, 1, 2 ;; P "e" & Err.Number""",
        "e438|e438|e438|e450|e449|e438|e450|e450|")]
    [DataRow("C06", "Public Sub BumpV(n) ;;     n = n + 1 ;; End Sub ;; Public Sub SetS(ByRef t As String) ;;     t = t & \"!\" ;; End Sub", "",
        """Dim b As New Box ;; Dim v As Variant ;; v = 3 ;; CallByName b, "BumpV", VbMethod, v ;; P v ;; P TypeName(v) ;; Dim t As String ;; t = "a" ;; CallByName b, "SetS", VbMethod, t ;; P t ;; Dim w As Variant ;; w = "a" ;; CallByName b, "SetS", VbMethod, w ;; P w ;; P "e" & Err.Number ;; Err.Clear ;; Dim c As New Collection ;; c.Add 3 ;; CallByName b, "Twice", VbMethod, c(1) ;; P c(1) ;; P "e" & Err.Number""",
        "4|Integer|a!|a|e13|3|e0|")]
    [DataRow("C07", "", "",
        """Dim b As New Box ;; P CallByName(b, "Twice", VbGet, 1) ;; P "e" & Err.Number ;; Err.Clear ;; CallByName b, "Twice", VbLet, 1 ;; P "e" & Err.Number ;; Err.Clear ;; CallByName b, "Half", VbSet, 1 ;; P "e" & Err.Number ;; Err.Clear ;; CallByName b, "Twice", VbSet, 1 ;; P "e" & Err.Number ;; Err.Clear ;; CallByName b, "Field", VbMethod, 1 ;; P "e" & Err.Number ;; Err.Clear ;; P CallByName(b, "Field", VbGet) ;; P "e" & Err.Number""",
        "e450|e450|e450|e450|e438|0|e0|")]
    [DataRow("E01", "", "",
        """Dim e As Variant ;; e = CVErr(5) ;; P TypeName(e) ;; P VarType(e) ;; P Len(e) ;; P "e" & Err.Number ;; Err.Clear ;; P Str(e) ;; P "e" & Err.Number ;; Err.Clear ;; P LenB(e) ;; P "e" & Err.Number ;; Err.Clear ;; P IsError(e) ;; P Len(CVErr(2042)) ;; P "e" & Err.Number""",
        "Error|10|e13|e13|e13|True|e13|")]
    [DataRow("E02", "", "",
        """Dim e As Variant ;; e = CVErr(5) ;; P (e = CVErr(5)) ;; P "e" & Err.Number ;; Err.Clear ;; P (e = 5) ;; P "e" & Err.Number ;; Err.Clear ;; P (e < CVErr(6)) ;; P "e" & Err.Number ;; Err.Clear ;; P (e = "Error 5") ;; P "e" & Err.Number ;; Err.Clear ;; P (e <> CVErr(5)) ;; P "e" & Err.Number ;; Err.Clear ;; P (e = Empty) ;; P "e" & Err.Number ;; Err.Clear ;; P (e Is Nothing) ;; P "e" & Err.Number""",
        "True|e0|e13|True|e0|e13|False|e0|e13|e424|")]
    [DataRow("E03", "", "",
        """Dim e As Variant ;; e = CVErr(5) ;; P CStr(e) ;; P "e" & Err.Number ;; Err.Clear ;; P e & "" ;; P "e" & Err.Number ;; Err.Clear ;; P CLng(e) ;; P "e" & Err.Number ;; Err.Clear ;; P Format(e) ;; P "e" & Err.Number ;; Err.Clear ;; P Trim(e) ;; P "e" & Err.Number ;; Err.Clear ;; Dim s As String ;; s = e ;; P "e" & Err.Number ;; Err.Clear ;; P (e + 1) ;; P "e" & Err.Number ;; Err.Clear ;; P IsNumeric(e) ;; P CInt(IsNull(e))""",
        "Error 5|e0|e13|5|e0|e13|e13|e13|e13|False|0|")]
    [DataRow("E04", "", "",
        """Dim e As Variant ;; e = CVErr(5) ;; Select Case e ;; Case CVErr(5) ;; P "match" ;; Case Else ;; P "else" ;; End Select ;; P "e" & Err.Number ;; Err.Clear ;; Dim f As Variant ;; f = CVErr(5) ;; P (e = f) ;; P "e" & Err.Number ;; Err.Clear ;; P StrComp(e, f) ;; P "e" & Err.Number ;; Err.Clear ;; P InStr(e, "5") ;; P "e" & Err.Number""",
        "match|e0|True|e0|e13|e13|")]
    [DataRow("S02", "", "",
        """P StrComp("", "a", 99) ;; P "e" & Err.Number ;; Err.Clear ;; P StrComp("a", "b", 99) ;; P "e" & Err.Number ;; Err.Clear ;; P StrComp("", "", 99) ;; P "e" & Err.Number ;; Err.Clear ;; P StrComp("a", "", 99) ;; P "e" & Err.Number ;; Err.Clear ;; P StrComp("", "a", 2) ;; P "e" & Err.Number ;; Err.Clear ;; P StrComp("a", "b", 2) ;; P "e" & Err.Number ;; Err.Clear ;; P StrComp(Null, "a", 99) ;; P "e" & Err.Number""",
        "-1|e0|-1|e0|0|e0|1|e0|e5|e5|e94|")]
    [DataRow("S03", "", "",
        """P InStrRev("", "a", -1, 99) ;; P "e" & Err.Number ;; Err.Clear ;; P InStrRev("abc", "b", -1, 99) ;; P "e" & Err.Number ;; Err.Clear ;; P InStrRev("abc", "", -1, 99) ;; P "e" & Err.Number ;; Err.Clear ;; P "[" & Replace("", "a", "b", 1, -1, 99) & "]" ;; P "e" & Err.Number ;; Err.Clear ;; P Replace("abc", "b", "x", 1, -1, 99) ;; P "e" & Err.Number ;; Err.Clear ;; P UBound(Split("", ",", -1, 99)) ;; P "e" & Err.Number ;; Err.Clear ;; P UBound(Split("a,b", ",", -1, 99)) ;; P "e" & Err.Number ;; Err.Clear ;; P UBound(Filter(Array("a"), "a", True, 99)) ;; P "e" & Err.Number ;; Err.Clear ;; P UBound(Filter(Array(""), "", True, 99)) ;; P "e" & Err.Number""",
        "0|e0|2|e0|3|e0|[]|e0|axc|e0|-1|e0|1|e0|0|e0|0|e0|")]
    [DataRow("S04", "", "",
        """P InStr(1, "aBc", "b", 99) ;; P InStr(1, "aBc", "b", 1) ;; P InStr(1, "aBc", "b", 3) ;; P InStr(1, "aBc", "b", 100) ;; P InStr(1, "aBc", "b", -1) ;; P StrComp("a", "B", 99) ;; P StrComp("a", "B", 3) ;; P "e" & Err.Number""",
        "2|2|2|2|-1|-1|e5|")]
    [DataRow("S05", "", "",
        """P InStr(1, "", "", 2) ;; P "e" & Err.Number ;; Err.Clear ;; P InStr(1, "a", "", 2) ;; P "e" & Err.Number ;; Err.Clear ;; P InStr(1, "", "a", -2) ;; P "e" & Err.Number ;; Err.Clear ;; P InStr(3, "", "a", 2) ;; P "e" & Err.Number ;; Err.Clear ;; P InStrRev("", "a", -1, 2) ;; P "e" & Err.Number ;; Err.Clear ;; P InStrRev("abc", "", -1, 2) ;; P "e" & Err.Number ;; Err.Clear ;; P "[" & Replace("", "a", "b", 1, -1, 2) & "]" ;; P "e" & Err.Number ;; Err.Clear ;; P UBound(Split("", ",", -1, 2)) ;; P "e" & Err.Number ;; Err.Clear ;; P UBound(Filter(Array(""), "a", True, 2)) ;; P "e" & Err.Number""",
        "0|e0|1|e0|0|e0|0|e0|0|e0|3|e0|[]|e0|-1|e0|-1|e0|")]
    [DataRow("A01", "", "",
        """Dim c(1) As Collection ;; Set c(0) = New Collection ;; Dim v As Variant ;; v = c ;; P TypeName(v) ;; P UBound(v) ;; P TypeName(v(0)) ;; P TypeName(v(1)) ;; P "e" & Err.Number ;; Err.Clear ;; Dim w() As Collection ;; w = v ;; P "e" & Err.Number ;; Err.Clear ;; P UBound(w) ;; Dim o() As Object ;; o = v ;; P "e" & Err.Number ;; Err.Clear ;; P UBound(o)""",
        "Object()|1|Collection|Nothing|e0|e0|1|e0|1|")]
    [DataRow("A02", "",
        "Function MakeArr() As Collection() ;;     Dim r(1) As Collection ;;     Set r(1) = New Collection ;;     MakeArr = r ;; End Function ;; Function TakeV(ByVal v As Variant) As Collection() ;;     TakeV = v ;; End Function",
        """Dim v As Variant ;; v = MakeArr() ;; P TypeName(v) ;; P "e" & Err.Number ;; Err.Clear ;; Dim w() As Collection ;; w = TakeV(v) ;; P "e" & Err.Number ;; Err.Clear ;; P UBound(w) ;; P TypeName(w(1)) ;; P "e" & Err.Number""",
        "Object()|e0|e0|1|Collection|e0|")]
    [DataRow("A03", "",
        "Function AsObjects(ByVal v As Variant) As Object() ;;     AsObjects = v ;; End Function ;; Function AsBoxes(ByVal v As Variant) As Box() ;;     AsBoxes = v ;; End Function",
        """Dim c(1) As Collection ;; Dim v As Variant ;; v = c ;; Dim o() As Object ;; o = AsObjects(v) ;; P "e" & Err.Number ;; Err.Clear ;; P UBound(o) ;; Dim d(1) As Object ;; v = d ;; Dim w() As Collection ;; w = v ;; P "e" & Err.Number ;; Err.Clear ;; P UBound(w) ;; Dim bx() As Box ;; bx = AsBoxes(v) ;; P "e" & Err.Number ;; Err.Clear ;; P UBound(bx) ;; Dim n(1) As Long ;; v = n ;; bx = AsBoxes(v) ;; P "e" & Err.Number""",
        "e0|1|e0|1|e0|1|e13|")]
    [DataRow("A04", "",
        "Function Count(ByRef a() As Collection) As Long ;;     Count = UBound(a) - LBound(a) + 1 ;; End Function",
        """Dim c(2) As Collection ;; Dim v As Variant ;; v = c ;; P Count(c) ;; P "e" & Err.Number ;; Err.Clear ;; Dim w() As Collection ;; w = v ;; P Count(w) ;; P "e" & Err.Number""",
        "3|e0|3|e0|")]
    public async Task ARowMeasuredInExcel_PrintsWhatMSVBAPrinted(string id, string boxExtras, string module, string probe, string printed)
        => Assert.AreEqual(printed, await Run(boxExtras, module, probe), id);
}
