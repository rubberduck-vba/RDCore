using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// The rows of the gaps card measured in Excel, each run whole as it was run there: what <c>P</c> prints of each statement under
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
            // a declaration of the module goes before its first procedure, as the oracle's does.
            ["Option Explicit", "Private acc As String", .. Lines(module), "Private Sub P(ByVal x As Variant)", "    acc = acc & CStr(x) & \"|\"", "End Sub",
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
    // Observed in Excel: Before and After that name no member - of an empty collection, or of one with a member - by an index or by a key.
    [DataRow("s58 C01", "", "",
        """Dim e As New Collection ;; e.Add 1, , 1 ;; P "e" & Err.Number ;; Err.Clear ;; e.Add 1, , , 1 ;; P "e" & Err.Number ;; Err.Clear ;; e.Add 1, , 0 ;; P "e" & Err.Number ;; Err.Clear ;; e.Add 1, , , 0 ;; P "e" & Err.Number ;; Err.Clear ;; e.Add 1, , "k" ;; P "e" & Err.Number ;; Err.Clear ;; e.Add 1, , , "k" ;; P "e" & Err.Number ;; Err.Clear ;; P e.Count""",
        "e5|e5|e5|e5|e5|e5|0|")]
    [DataRow("s58 C02", "", "",
        """Dim e As New Collection ;; e.Add 7 ;; e.Add 8, , 2 ;; P "e" & Err.Number ;; Err.Clear ;; e.Add 8, , , 2 ;; P "e" & Err.Number ;; Err.Clear ;; e.Add 8, , 0 ;; P "e" & Err.Number ;; Err.Clear ;; e.Add 8, , , 0 ;; P "e" & Err.Number ;; Err.Clear ;; e.Add 8, , "k" ;; P "e" & Err.Number ;; Err.Clear ;; e.Add 8, , , "k" ;; P "e" & Err.Number ;; Err.Clear ;; P e.Count""",
        "e9|e9|e9|e9|e5|e5|1|")]
    // Observed in Excel: the HelpContext a raise gives Err, and the one it keeps.
    [DataRow("s58 H03", "", "",
        """Err.Raise 13, , , , 77 ;; P Err.HelpContext ;; Err.Raise 5 ;; P Err.HelpContext ;; Err.Raise 9, , , , 0 ;; P Err.HelpContext ;; Error 11 ;; P Err.HelpContext ;; Dim z As Variant ;; z = 1 / 0 ;; P Err.HelpContext ;; Err.Clear ;; P Err.HelpContext ;; Err.Raise 13, , , , 77 ;; z = 1 / 0 ;; P Err.Number ;; P Err.HelpContext""",
        "77|77|1000009|1000011|1000011|0|11|1000011|")]
    [DataRow("s58 H04", "", "",
        """Err.Raise 50001 ;; P Err.HelpContext ;; Err.Clear ;; Err.Raise 400 ;; P Err.HelpContext ;; P Err.Description ;; Err.Clear ;; Err.Raise 1004 ;; P Err.HelpContext ;; Err.Clear ;; Err.Raise 65535 ;; P Err.HelpContext ;; Err.Clear ;; Err.Raise 31001 ;; P Err.HelpContext ;; P Err.Description ;; Err.Clear ;; Err.Raise 3 ;; P Err.HelpContext ;; P Err.Description ;; Err.Clear ;; Err.Raise 50002 ;; P Err.HelpContext""",
        "1000095|1000095|Application-defined or object-defined error|1000095|1000095|1000095|Application-defined or object-defined error|1000003|Return without GoSub|1000095|")]
    [DataRow("s58 H06", "", "",
        """Err.Raise 9 ;; Err.Raise 5 ;; P Err.HelpContext ;; P Err.Description ;; Err.Clear ;; Err.Raise 13, , "mine" ;; Err.Raise 5 ;; P Err.Description ;; Err.Clear ;; Err.Raise 13, , , , 77 ;; Error 5 ;; P Err.HelpContext ;; Err.Clear ;; Err.Raise 13, , , , 77 ;; Err.Raise 5, , , , 0 ;; P Err.HelpContext ;; Err.Clear ;; Err.Raise 13, , , , 77 ;; Dim z As Variant ;; z = 1 / 0 ;; Err.Raise 5 ;; P Err.HelpContext ;; Err.Clear ;; Err.Raise 50001, , , , 77 ;; P Err.HelpContext ;; Err.Clear ;; Err.Raise 13, , , "f.chm" ;; P Err.HelpFile ;; P Err.HelpContext ;; Err.Raise 5 ;; P Err.HelpFile""",
        "1000009|Subscript out of range|mine|1000005|1000005|1000011|77|f.chm|1000013|f.chm|")]
    public async Task ARowMeasuredInExcel_PrintsWhatMSVBAPrinted(string id, string boxExtras, string module, string probe, string printed)
        => await AssertPrintedAsync(id, boxExtras, module, probe, printed);

    // RD-VBAL §6.1.3.2.2.3: a run-time error leaves Err.HelpFile empty, where MS-VBA names its own help file (VbLR6.chm, observed in Excel); its HelpContext
    // is MS-VBA's, and a help file the program gives is kept until Err.Clear.
    [TestMethod]
    public async Task AnErrorRaisedWithNoHelpFile_LeavesErrHelpFileEmpty_AndItsHelpContextIsMSVBAs()
        => await AssertPrintedAsync("s58 H03, RD-VBAL §6.1.3.2.2.3", "", "",
            """Dim z As Variant ;; z = 1 / 0 ;; P "[" & Err.HelpFile & "]" ;; P Err.HelpContext ;; Err.Clear ;; Err.Raise 5 ;; P "[" & Err.HelpFile & "]" ;; Err.Clear ;; Err.HelpFile = "f.chm" ;; P Err.HelpFile ;; Err.Clear ;; P "[" & Err.HelpFile & "]" """,
            "[]|1000011|[]|f.chm|[]|");

    // MS-VBAL §6.1.3.2.1.2: Err.Raise sets Err.HelpFile to the help file it is given, and one given none keeps the one Err has while it has an error.
    [TestMethod]
    public async Task AnErrorRaisedWithAHelpFile_SetsErrHelpFile()
        => await AssertPrintedAsync("MS-VBAL §6.1.3.2.1.2", "", "",
            """Err.Raise 5, , , "g.chm" ;; P Err.HelpFile ;; Err.Raise 6 ;; P Err.HelpFile ;; Err.Clear ;; P "[" & Err.HelpFile & "]" """,
            "g.chm|g.chm|[]|");

    // Observed in Excel: an error an automation object raises has the help context of its message, whatever one Err had - Scripting.Dictionary's 457 is
    // 1000457, and a RegExp's 5020 is no message of MS-VBA's (1000095).
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task AnErrorAnAutomationObjectRaises_HasTheHelpContextOfItsMessage()
        => await AssertPrintedAsync("s58 H05", "", "",
            """Dim d As Object ;; Set d = CreateObject("Scripting.Dictionary") ;; d.Add "a", 1 ;; d.Add "a", 2 ;; P Err.Number ;; P Err.HelpContext ;; Err.Clear ;; Dim r As Object ;; Set r = CreateObject("VBScript.RegExp") ;; r.Pattern = "(" ;; P r.Test("a") ;; P Err.Number ;; P Err.HelpContext ;; Err.Clear ;; Err.Raise 13, , , , 77 ;; d.Add "a", 3 ;; P Err.Number ;; P Err.HelpContext ;; Err.Clear ;; Err.Raise 13, , , , 77 ;; P r.Test("a") ;; P Err.Number ;; P Err.HelpContext""",
            "457|1000457|5020|1000095|457|1000457|5020|1000095|");

    // Observed in Excel: MS-VBA negates the least Long of a constant expression - a literal, a Const - as the Long it is, where the negation of a
    // Long variable overflows, and so does that of the least Integer or LongLong in any expression.
    [TestMethod]
    [DataRow("s58 N07", "",
        """P -&H80000000 ;; P "e" & Err.Number ;; Err.Clear ;; P -&H8000000000000000^ ;; P "e" & Err.Number ;; Err.Clear ;; P -&H80000000^ ;; P TypeName(-&H80000000^) ;; P -&H80000000& ;; P "e" & Err.Number""",
        "-2147483648|e0|e6|-2147483648|LongLong|-2147483648|e0|")]
    [DataRow("s58 N08", "Private Const K = -&H80000000", """P K ;; P "e" & Err.Number""", "-2147483648|e0|")]
    public async Task TheNegationOfTheLeastLong_InAConstantExpression_IsTheLeastLong(string id, string module, string probe, string printed)
        => await AssertPrintedAsync(id, "", module, probe, printed);

    // Observed in Excel: an error's source is the project's - VBAProject in Excel, Project1 here - unless Err.Raise is given one, or keeps the one
    // Err has: a run-time error, the Error statement and an automation object's error are the project's, whatever source Err had, in a class too.
    [TestMethod]
    [DataRow("s58 H06", "", """Err.Raise 13 ;; P Err.Source""", "Project1|")]
    [DataRow("s61 H07", "", """Err.Source = "x" ;; Dim z As Variant ;; z = 1 / 0 ;; P Err.Source ;; P Err.Number""", "Project1|11|")]
    [DataRow("s61 H08", "", """Err.Raise 5, "lib" ;; Dim z As Variant ;; z = 1 / 0 ;; P Err.Source ;; P Err.Number""", "Project1|11|")]
    [DataRow("s61 H09", "",
        """Dim z As Variant ;; z = 1 / 0 ;; P Err.Source ;; Err.Clear ;; P "[" & Err.Source & "]" ;; Err.Raise 5 ;; P Err.Source ;; Err.Clear ;; Error 9 ;; P Err.Source""",
        "Project1|[]|Project1|Project1|")]
    [DataRow("s61 H10", "", """Err.Raise 5, "lib" ;; Err.Raise 6 ;; P Err.Source ;; P Err.Number""", "lib|6|")]
    [DataRow("s61 H12", "Public Sub Fail() ;;     Dim z As Variant ;;     z = 1 / 0 ;; End Sub ;; Public Sub RaiseIt() ;;     Err.Raise 5 ;; End Sub",
        """Dim b As New Box ;; b.Fail ;; P Err.Source ;; Err.Clear ;; b.RaiseIt ;; P Err.Source""", "Project1|Project1|")]
    public async Task AnErrorGivenNoSource_IsTheProjects(string id, string boxExtras, string probe, string printed)
        => await AssertPrintedAsync(id, boxExtras, "", probe, printed);

    // Observed in Excel: an automation object's error is the project's too. Scripting.Dictionary is a COM server, so this runs where COM does.
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task AnAutomationObjectsError_IsTheProjects()
        => await AssertPrintedAsync("s61 H11", "", "",
            """Dim d As Object ;; Set d = CreateObject("Scripting.Dictionary") ;; d.Add 1, 1 ;; d.Add 1, 1 ;; P Err.Number ;; P Err.Source""",
            "457|Project1|");

    private static async Task AssertPrintedAsync(string id, string boxExtras, string module, string probe, string printed)
    {
        var observed = await Run(boxExtras, module, probe);
        Assert.AreEqual(printed, observed, $"{id}, which printed {observed}");
    }
}
