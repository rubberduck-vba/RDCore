using System.Runtime.Versioning;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>Range.Value</c> and the 2-D arrays it reads and writes, against Excel: each row is a snippet run on a sheet prepared the same way, and what it says, or
/// the error it raises, is what MS-VBA did with the same snippet in Excel. The whole grid runs once, in one Excel, started late bound as a program does it
/// with <c>CreateObject</c>. Skipped on a machine that does not have Excel.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §2.3.1.2 References")]
[OSCondition(OperatingSystems.Windows)]
[SupportedOSPlatform("windows")]
public sealed class ExcelRangeArrayTests
{
    // the cases, by the identifier their row names: G, a get; S, a set from a 2-D array; O, from a 1-D one; C, from a scalar; P, how the array is passed;
    // V, the array once it is in a Variant.
    private static readonly (string Id, string[] Snippet)[] Cases =
    [
        ("G01", ["Say Describe(S.Range(\"A1\").Value)"]),
        ("G02", ["Say Describe(S.Range(\"A1\").Value2)"]),
        ("G03", ["Say Describe(S.Range(\"A1:C2\").Value)"]),
        ("G04", ["Say Describe(S.Range(\"E5\").Value)"]),
        ("G05", ["Say Describe(S.Range(\"B5\").Value)"]),
        ("G06", ["Say Describe(S.Range(\"B5\").Value2)"]),
        ("G07", ["Say Describe(S.Range(\"C5\").Value)"]),
        ("G08", ["Say Describe(S.Range(\"C5\").Value2)"]),
        ("G09", ["Say Describe(S.Range(\"D5\").Value)"]),
        ("G10", ["Say Describe(S.Range(\"A5:F5\").Value)"]),
        ("G11", ["V = S.Range(\"A:A\").Value", "Say LBound(V, 1) & \" \" & UBound(V, 1) & \" \" & LBound(V, 2) & \" \" & UBound(V, 2) & \" \" & Describe(V(2, 1)) & \" \" & Describe(V(9, 1))"]),
        ("G12", ["V = S.Rows(1).Value", "Say LBound(V, 1) & \" \" & UBound(V, 1) & \" \" & LBound(V, 2) & \" \" & UBound(V, 2) & \" \" & Describe(V(1, 3)) & \" \" & Describe(V(1, 4))"]),
        ("G13", ["Say Describe(S.Range(\"A1:B1,A3:B3\").Value)"]),
        ("G14", ["V = S.Range(\"A1:C3\").Value", "Say Describe(V(2, 3))"]),
        ("G15", ["V = S.Range(\"B2\").Value", "Say IsArray(V) & \" \" & TypeName(V)"]),
        ("S01", ["Dim A(1 To 2, 1 To 2) As Variant", "A(1, 1) = 1: A(1, 2) = \"two\": A(2, 1) = 3.5: A(2, 2) = Empty", "S.Range(\"H1:I2\").Value = A", "Say Describe(S.Range(\"H1:I2\").Value)"]),
        ("S02", ["Dim A(1 To 2, 1 To 2) As Variant", "A(1, 1) = 1: A(1, 2) = 2: A(2, 1) = 3: A(2, 2) = 4", "S.Range(\"H1:J3\").Value = A", "Say Describe(S.Range(\"H1:J3\").Value)"]),
        ("S03", ["Dim A(1 To 3, 1 To 3) As Variant", "A(1, 1) = 1: A(1, 2) = 2: A(1, 3) = 3: A(2, 1) = 4: A(2, 2) = 5: A(3, 3) = 9", "S.Range(\"H1:I2\").Value = A", "Say Describe(S.Range(\"H1:J3\").Value)"]),
        ("S04", ["Dim A(0 To 1, 0 To 1) As Variant", "A(0, 0) = 1: A(0, 1) = 2: A(1, 0) = 3: A(1, 1) = 4", "S.Range(\"H1:I2\").Value = A", "Say Describe(S.Range(\"H1:I2\").Value)"]),
        ("S05", ["Dim A(1 To 2, 1 To 2) As Long", "A(1, 1) = 1: A(1, 2) = 2: A(2, 1) = 3: A(2, 2) = 4", "S.Range(\"H1:I2\").Value = A", "Say Describe(S.Range(\"H1:I2\").Value)"]),
        ("S06", ["Dim A(1 To 2, 1 To 2) As String", "A(1, 1) = \"a\": A(1, 2) = \"b\": A(2, 1) = \"c\"", "S.Range(\"H1:I2\").Value = A", "Say Describe(S.Range(\"H1:I2\").Value)"]),
        ("S07", ["Dim A(1 To 2, 1 To 2) As Double", "A(1, 1) = 0.5: A(1, 2) = 1.5: A(2, 1) = 2.5: A(2, 2) = 3.5", "S.Range(\"H1:I2\").Value = A", "Say Describe(S.Range(\"H1:I2\").Value)"]),
        ("S08", ["Dim A() As Variant", "ReDim A(1 To 2, 1 To 2)", "A(1, 1) = 1: A(2, 2) = 4", "S.Range(\"H1:I2\").Value = A", "Say Describe(S.Range(\"H1:I2\").Value)"]),
        ("S09", ["V = S.Range(\"A1:B2\").Value", "S.Range(\"H1:I2\").Value = V", "Say Describe(S.Range(\"H1:I2\").Value)"]),
        ("S10", ["S.Range(\"A1:C3\").Value = S.Range(\"A1:C3\").Value", "Say Describe(S.Range(\"A1:C3\").Value)"]),
        ("S11", ["Dim A(1 To 2, 1 To 2) As Long", "A(1, 1) = 1: A(1, 2) = 2: A(2, 1) = 3: A(2, 2) = 4", "V = A", "S.Range(\"H1:I2\").Value = V", "Say Describe(S.Range(\"H1:I2\").Value)"]),
        ("S12", ["Dim A(1 To 1, 1 To 2) As Variant", "A(1, 1) = 1: A(1, 2) = \"x\"", "S.Range(\"H1:I1\").Value2 = A", "Say Describe(S.Range(\"H1:I1\").Value)"]),
        ("S13", ["Dim A(1 To 1, 1 To 3) As Variant", "A(1, 1) = DateSerial(2026, 1, 2): A(1, 2) = CCur(2.5): A(1, 3) = True", "S.Range(\"H1:J1\").Value = A", "Say Describe(S.Range(\"H1:J1\").Value) & \" / \" & Describe(S.Range(\"H1:J1\").Value2)"]),
        ("O01", ["S.Range(\"H1:J1\").Value = Array(1, 2, 3)", "Say Describe(S.Range(\"H1:J1\").Value)"]),
        ("O02", ["S.Range(\"H1:H3\").Value = Array(1, 2, 3)", "Say Describe(S.Range(\"H1:H3\").Value)"]),
        ("O03", ["S.Range(\"H1:H3\").Value = Xl.Transpose(Array(1, 2, 3))", "Say Describe(S.Range(\"H1:H3\").Value)"]),
        ("O04", ["Dim B(1 To 3) As Long", "B(1) = 7: B(2) = 8: B(3) = 9", "S.Range(\"H1:J1\").Value = B", "Say Describe(S.Range(\"H1:J1\").Value)"]),
        ("O05", ["Say Describe(Xl.Transpose(Array(1, 2, 3)))"]),
        ("C01", ["S.Range(\"H1:I2\").Value = 5", "Say Describe(S.Range(\"H1:I2\").Value)"]),
        ("C02", ["S.Range(\"H1:I2\").Value = \"text\"", "Say Describe(S.Range(\"H1:I2\").Value)"]),
        ("C03", ["S.Range(\"H1\").Value = DateSerial(2026, 11, 12)", "Say Describe(S.Range(\"H1\").Value)"]),
        ("C04", ["S.Range(\"A1:B1\").Value = Empty", "Say Describe(S.Range(\"A1:C1\").Value)"]),
        ("C05", ["S.Range(\"A1\").Value = Null", "Say Describe(S.Range(\"A1\").Value)"]),
        ("C06", ["S.Range(\"A1\").Value = Nothing", "Say Describe(S.Range(\"A1\").Value)"]),
        ("C07", ["S.Range(\"H1\").Value = CCur(2.5)", "Say Describe(S.Range(\"H1\").Value) & \" / \" & Describe(S.Range(\"H1\").Value2)"]),
        ("C08", ["S.Range(\"H1\").Value = True", "Say Describe(S.Range(\"H1\").Value)"]),
        ("C09", ["S.Range(\"H1\").Value = CVErr(2042)", "Say Describe(S.Range(\"H1\").Value)"]),
        ("P01", ["Dim A(1 To 2, 1 To 2) As Variant", "A(1, 1) = 1: A(1, 2) = 2: A(2, 1) = 3: A(2, 2) = 4", "S.Range(\"H1:I2\").Value = (A)", "Say Describe(S.Range(\"H1:I2\").Value)"]),
        ("P02", ["Dim A(1 To 2, 1 To 3) As Variant", "A(1, 1) = 1: A(1, 3) = 3: A(2, 2) = 5", "S.Range(\"H1\").Resize(UBound(A, 1), UBound(A, 2)).Value = A", "Say Describe(S.Range(\"H1:J2\").Value)"]),
        ("P03", ["V = S.Range(\"A1:C3\").Value", "S.Range(\"H1\").Resize(3, 3).Value = V", "Say Describe(S.Range(\"H1:J3\").Value)"]),
        ("P04", ["Dim A(1 To 2, 1 To 2) As Variant", "A(1, 1) = 1: A(1, 2) = 2: A(2, 1) = 3: A(2, 2) = 4", "S.Range(S.Cells(1, 8), S.Cells(2, 9)).Value = A", "Say Describe(S.Range(\"H1:I2\").Value)"]),
        ("P05", ["V = S.Range(\"A1:B2\").Value", "PutByRef S.Range(\"H1:I2\"), V", "Say Describe(S.Range(\"H1:I2\").Value)"]),
        ("P06", ["V = S.Range(\"A1:B2\").Value", "PutByVal S.Range(\"H1:I2\"), V", "Say Describe(S.Range(\"H1:I2\").Value)"]),
        ("P07", ["S.Range(\"H1:I2\").Value = S.Range(\"A1:B2\")", "Say Describe(S.Range(\"H1:I2\").Value)"]),
        ("V01", ["ReDim V(1 To 2, 1 To 3)", "V(2, 3) = 7", "Say Describe(V)"]),
        ("V02", ["V = S.Range(\"A1:C2\").Value", "V(1, 1) = 99", "Say Describe(V) & \" / \" & Describe(S.Range(\"A1\").Value)"]),
        ("V03", ["V = S.Range(\"A1:C2\").Value", "Say LBound(V, 1) & \" \" & UBound(V, 1) & \" \" & LBound(V, 2) & \" \" & UBound(V, 2)"]),
        ("V04", ["V = S.Range(\"A1:C2\").Value", "Say UBound(V, 3)"]),
        ("V05", ["V = S.Range(\"A1:C2\").Value", "Say Describe(V(1))"]),
        ("V06", ["V = S.Range(\"A1:C2\").Value", "Say IsArray(V) & \" \" & TypeName(V) & \" \" & VarType(V)"]),
        ("V07", ["V = S.Range(\"A1:C2\").Value", "Erase V", "Say IsArray(V) & \" \" & TypeName(V) & \" \" & Describe(V)"]),
        ("V08", ["Dim E As Variant, T As String", "V = S.Range(\"A1:C2\").Value", "For Each E In V", "    T = T & \" \" & Describe(E)", "Next", "Say T"]),
        ("V09", ["V = S.Range(\"A1:C2\").Value", "ReDim Preserve V(1 To 2, 1 To 4)", "Say Describe(V)"]),
        ("V10", ["V = S.Range(\"A1:C2\").Value", "ReDim Preserve V(1 To 3, 1 To 3)", "Say Describe(V)"]),
        ("V11", ["V = S.Range(\"A1:C2\").Value", "Say Describe(V(3, 1))"]),
        ("V12", ["V = S.Range(\"A1:C2\").Value", "V(2, 2) = \"changed\"", "S.Range(\"A1:C2\").Value = V", "Say Describe(S.Range(\"A1:C2\").Value)"]),
        ("C10", ["S.Range(\"H1\").Value = CVErr(2007)", "Say S.Range(\"H1\").Text"]),
        ("C11", ["V = S.Range(\"D5\").Value", "Say Describe(IsError(V)) & \" \" & CLng(V)"]),
        ("S14", ["Dim A(1 To 1, 1 To 2) As Variant", "A(1, 1) = CVErr(2042): A(1, 2) = 1", "S.Range(\"H1:I1\").Value = A", "Say S.Range(\"H1\").Text & \" \" & Describe(S.Range(\"I1\").Value)"]),
        ("E01", ["V = CVErr(2007)", "Say Describe(CBool(V))"]),
        ("E02", ["V = CVErr(2007)", "Say Describe(CByte(V))"]),
        ("E03", ["V = CVErr(2007)", "Say Describe(CCur(V))"]),
        ("E04", ["V = CVErr(2007)", "Say Describe(CDbl(V))"]),
        ("E05", ["V = CVErr(2007)", "Say Describe(CInt(V))"]),
        ("E06", ["V = CVErr(2007)", "Say Describe(CLng(V))"]),
        ("E07", ["V = CVErr(2007)", "Say Describe(CLngLng(V))"]),
        ("E08", ["V = CVErr(2007)", "Say Describe(CLngPtr(V))"]),
        ("E09", ["V = CVErr(2007)", "Say Describe(CSng(V))"]),
        ("E10", ["V = CVErr(2007)", "Say Describe(CStr(V))"]),
        ("E11", ["V = CVErr(2007)", "Say Describe(Str(V))"]),
        ("E12", ["V = CVErr(2007)", "Say Describe(Str$(V))"]),
        ("E13", ["V = CVErr(2007)", "Say Describe(CDate(V))"]),
        ("E14", ["V = CVErr(2007)", "Say Describe(CVDate(V))"]),
        ("E15", ["V = CVErr(2007)", "Say Describe(CDec(V))"]),
        ("E16", ["V = CVErr(2007)", "Say Describe(CVar(V))"]),
        ("E17", ["V = CVErr(2007)", "Say Describe(Fix(V))"]),
        ("E18", ["V = CVErr(2007)", "Say Describe(Int(V))"]),
        ("E19", ["V = CVErr(2007)", "Say Describe(Hex(V))"]),
        ("E20", ["V = CVErr(2007)", "Say Describe(Oct(V))"]),
        ("E21", ["V = CVErr(2007)", "Say Describe(Val(V))"]),
        ("E22", ["Dim N As Long", "V = CVErr(2007)", "N = V", "Say Describe(N)"]),
        ("E23", ["Dim T As String", "V = CVErr(2007)", "T = V", "Say Describe(T)"]),
        ("E24", ["Dim D As Double", "V = CVErr(2007)", "D = V", "Say Describe(D)"]),
    ];

    // Say, Describe (a value as text that does not depend on the locale), Prepare (the sheet), and the helpers the P rows pass the array through.
    private const string Harness = """""
        Attribute VB_Name = "Grid"
        Option Explicit
        '  Range.Value and 2-D arrays: each case on a sheet prepared the same way, what it says or the error it raises.
        
        Private Said As String
        
        Private Sub Say(ByVal Text As String)
            Said = Said & Text
        End Sub
        
        '  a value as text that does not depend on the locale: its type, and what it holds.
        Public Function Describe(ByVal X As Variant) As String
            If IsArray(X) Then
                Describe = DescribeArray(X)
            ElseIf IsObject(X) Then
                If X Is Nothing Then Describe = "Nothing" Else Describe = "Object:" & TypeName(X)
            Else
                Select Case VarType(X)
                    Case vbEmpty: Describe = "Empty"
                    Case vbNull: Describe = "Null"
                    Case vbString: Describe = "String:""" & X & """"
                    Case vbBoolean: Describe = "Boolean:" & IIf(X, "True", "False")
                    Case vbDate: Describe = "Date:" & Format$(X, "yyyy\-mm\-dd hh\:nn\:ss")
                    Case vbError: Describe = "Error:" & Mid$(CStr(X), 7)
                    Case Else: Describe = TypeName(X) & ":" & Trim$(Str$(X))
                End Select
            End If
        End Function
        
        Private Function DimensionsOf(ByRef X As Variant) As Long
            Dim D As Long, B As Long
            On Error GoTo Done
            For D = 1 To 60
                B = UBound(X, D)
                DimensionsOf = D
            Next
        Done:
        End Function
        
        Private Function DescribeArray(ByRef X As Variant) As String
            Dim D As Long, Text As String, I As Long, J As Long
            D = DimensionsOf(X)
            If D = 0 Then
                DescribeArray = TypeName(X) & "(unallocated)"
                Exit Function
            End If
            Text = TypeName(X) & "("
            For I = 1 To D
                If I > 1 Then Text = Text & ", "
                Text = Text & LBound(X, I) & " To " & UBound(X, I)
            Next
            Text = Text & "){"
            If D = 1 Then
                For I = LBound(X) To UBound(X)
                    If I > LBound(X) Then Text = Text & ", "
                    Text = Text & Describe(X(I))
                Next
            ElseIf D = 2 Then
                For I = LBound(X, 1) To UBound(X, 1)
                    If I > LBound(X, 1) Then Text = Text & "; "
                    For J = LBound(X, 2) To UBound(X, 2)
                        If J > LBound(X, 2) Then Text = Text & ", "
                        Text = Text & Describe(X(I, J))
                    Next
                Next
            Else
                Text = Text & "..."
            End If
            DescribeArray = Text & "}"
        End Function
        
        '  A1:C3 numbers and a String, row 5 one cell of each kind (Boolean, Date, Currency, an error, Empty, String); H:J are where the cases write.
        Private Sub Prepare(ByVal S As Object)
            S.Cells.Clear
            S.Range("A1").Value = 1: S.Range("B1").Value = "text": S.Range("C1").Value = 2.5
            S.Range("A2").Value = 4: S.Range("B2").Value = 5: S.Range("C2").Value = 6
            S.Range("A3").Value = 7: S.Range("B3").Value = 8: S.Range("C3").Value = 9
            S.Range("A5").Value = True: S.Range("B5").Value = DateSerial(2026, 1, 2): S.Range("C5").Value = CCur(1.25)
            S.Range("D5").Formula = "=1/0": S.Range("F5").Value = "x"
        End Sub
        
        Private Sub PutByRef(ByVal Target As Object, ByRef V As Variant)
            Target.Value = V
        End Sub
        
        Private Sub PutByVal(ByVal Target As Object, ByVal V As Variant)
            Target.Value = V
        End Sub
        
        """"";

    private static Dictionary<string, string>? _said;

    [ClassInitialize]
    public static async Task RunTheGrid(TestContext _)
    {
        if (Type.GetTypeFromProgID("Excel.Application") is null)
        {
            return;
        }

        var grid = new System.Text.StringBuilder(Harness.ReplaceLineEndings("\r\n"));
        grid.Append("\r\n");
        foreach (var (id, snippet) in Cases)
        {
            grid.Append($"Private Sub Case{id}(ByVal Xl As Object, ByVal S As Object)\r\n    Dim V As Variant\r\n");
            grid.Append(string.Concat(snippet.Select(line => $"    {line}\r\n")));
            grid.Append("End Sub\r\n\r\n");
        }

        grid.Append("Private Sub RunCase(ByVal Index As Long, ByVal Xl As Object, ByVal S As Object)\r\n    Select Case Index\r\n");
        grid.Append(string.Concat(Cases.Select((c, index) => $"        Case {index + 1}: Case{c.Id} Xl, S\r\n")));
        grid.Append("    End Select\r\nEnd Sub\r\n\r\n");
        grid.Append(string.Join("\r\n",
            "Public Sub RunAll(ByVal Xl As Object)",
            "    Dim Book As Object, S As Object, Ids As Variant, I As Long",
            $"    Ids = Array({string.Join(", ", Cases.Select(c => $"\"{c.Id}\""))})",
            "    Set Book = Xl.Workbooks.Add",
            "    Set S = Book.Worksheets(1)",
            "    For I = 0 To UBound(Ids)",
            "        Prepare S",
            "        Said = \"\"",
            "        On Error GoTo Failed",
            "        RunCase I + 1, Xl, S",
            "        Debug.Print Ids(I) & \"|\" & Said",
            "        GoTo Done",
            "Failed:",
            "        Debug.Print Ids(I) & \"|\" & Said & \"|error \" & Err.Number",
            "        Resume Done",
            "Done:",
            "        On Error GoTo 0",
            "    Next",
            "    Book.Close False",
            "End Sub"));

        string[] output = [];
        try
        {
            output = await RunAsync([], [("Grid", grid.ToString())], "Attribute VB_Name = \"Program\"\r\n" + string.Join("\r\n",
                "Public Sub Main()",
                "    Dim Xl As Object",
                "    Set Xl = CreateObject(\"Excel.Application\")",
                "    Debug.Print Xl.Hwnd",
                "    On Error GoTo Done",
                "    Xl.Visible = False",
                "    Xl.DisplayAlerts = False",
                "    RunAll Xl",
                "Done:",
                "    Xl.Quit",
                "End Sub") + "\r\n", libraries: null);
        }
        finally
        {
            StopExcel(output.FirstOrDefault());
        }

        _said = output.Skip(1).Select(line => line.Split('|', 2)).Where(parts => parts.Length == 2).ToDictionary(parts => parts[0], parts => parts[1]);
    }

    [TestMethod]
    [DataRow("G01", "Double:1", DisplayName = "one cell, Value: a scalar")]
    [DataRow("G02", "Double:1", DisplayName = "one cell, Value2")]
    [DataRow("G03", "Variant()(1 To 2, 1 To 3){Double:1, String:\"text\", Double:2.5; Double:4, Double:5, Double:6}", DisplayName = "a block, Value: a 1-based 2-D Variant array")]
    [DataRow("G04", "Empty", DisplayName = "an empty cell")]
    [DataRow("G05", "Date:2026-01-02 00:00:00", DisplayName = "a date, Value")]
    [DataRow("G06", "Double:46024", DisplayName = "a date, Value2")]
    [DataRow("G07", "Currency:1.25", DisplayName = "currency, Value")]
    [DataRow("G08", "Double:1.25", DisplayName = "currency, Value2")]
    [DataRow("G09", "Error:2007", IgnoreMessage = "an error cell read back: RD-VBA reads the Long code of its HRESULT where MS-VBA reads an Error, because the .NET late binder turns a VT_ERROR result into a number and RD-VBA does not call IDispatch itself", DisplayName = "an error cell")]
    [DataRow("G10", "Variant()(1 To 1, 1 To 6){Boolean:True, Date:2026-01-02 00:00:00, Currency:1.25, Error:2007, Empty, String:\"x\"}", IgnoreMessage = "an error cell read back: RD-VBA reads the Long code of its HRESULT where MS-VBA reads an Error, because the .NET late binder turns a VT_ERROR result into a number and RD-VBA does not call IDispatch itself", DisplayName = "a row of mixed cells")]
    [DataRow("G11", "1 1048576 1 1 Double:4 Empty", DisplayName = "a whole column: its bounds")]
    [DataRow("G12", "1 1 1 16384 Double:2.5 Empty", DisplayName = "a whole row: its bounds")]
    [DataRow("G13", "Variant()(1 To 1, 1 To 2){Double:1, String:\"text\"}", DisplayName = "a non-contiguous range")]
    [DataRow("G14", "Double:6", DisplayName = "one element of a block read")]
    [DataRow("G15", "False Double", DisplayName = "a one-cell range read into a Variant: no array")]
    [DataRow("S01", "Variant()(1 To 2, 1 To 2){Double:1, String:\"two\"; Double:3.5, Empty}", DisplayName = "a 2-D Variant array the range's size")]
    [DataRow("S02", "Variant()(1 To 3, 1 To 3){Double:1, Double:2, Error:2042; Double:3, Double:4, Error:2042; Error:2042, Error:2042, Error:2042}", IgnoreMessage = "an error cell read back: RD-VBA reads the Long code of its HRESULT where MS-VBA reads an Error, because the .NET late binder turns a VT_ERROR result into a number and RD-VBA does not call IDispatch itself", DisplayName = "an array smaller than the range: #N/A beyond it")]
    [DataRow("S03", "Variant()(1 To 3, 1 To 3){Double:1, Double:2, Empty; Double:4, Double:5, Empty; Empty, Empty, Empty}", DisplayName = "an array larger than the range: truncated")]
    [DataRow("S04", "Variant()(1 To 2, 1 To 2){Double:1, Double:2; Double:3, Double:4}", DisplayName = "a 0-based array")]
    [DataRow("S05", "Variant()(1 To 2, 1 To 2){Double:1, Double:2; Double:3, Double:4}", DisplayName = "a Long() array")]
    [DataRow("S06", "Variant()(1 To 2, 1 To 2){String:\"a\", String:\"b\"; String:\"c\", Empty}", DisplayName = "a String() array")]
    [DataRow("S07", "Variant()(1 To 2, 1 To 2){Double:.5, Double:1.5; Double:2.5, Double:3.5}", DisplayName = "a Double() array")]
    [DataRow("S08", "Variant()(1 To 2, 1 To 2){Double:1, Empty; Empty, Double:4}", DisplayName = "a Variant() array made by ReDim")]
    [DataRow("S09", "Variant()(1 To 2, 1 To 2){Double:1, String:\"text\"; Double:4, Double:5}", DisplayName = "an array taken from Range.Value, written elsewhere")]
    [DataRow("S10", "Variant()(1 To 3, 1 To 3){Double:1, String:\"text\", Double:2.5; Double:4, Double:5, Double:6; Double:7, Double:8, Double:9}", DisplayName = "the round trip r.Value = r.Value")]
    [DataRow("S11", "Variant()(1 To 2, 1 To 2){Double:1, Double:2; Double:3, Double:4}", DisplayName = "a Variant holding an array")]
    [DataRow("S12", "Variant()(1 To 1, 1 To 2){Double:1, String:\"x\"}", DisplayName = "Value2 set from an array")]
    [DataRow("S13", "Variant()(1 To 1, 1 To 3){Date:2026-01-02 00:00:00, Currency:2.5, Boolean:True} / Variant()(1 To 1, 1 To 3){Double:46024, Double:2.5, Boolean:True}", DisplayName = "a block written over the prepared cells, then read back with Value2")]
    [DataRow("O01", "Variant()(1 To 1, 1 To 3){Double:1, Double:2, Double:3}", DisplayName = "Array(1, 2, 3) into a row")]
    [DataRow("O02", "Variant()(1 To 3, 1 To 1){Double:1; Double:1; Double:1}", DisplayName = "Array(1, 2, 3) into a column: the first element in every cell")]
    [DataRow("O03", "Variant()(1 To 3, 1 To 1){Double:1; Double:2; Double:3}", DisplayName = "Application.Transpose into a column")]
    [DataRow("O04", "Variant()(1 To 1, 1 To 3){Double:7, Double:8, Double:9}", DisplayName = "a 1-D Long() array into a row")]
    [DataRow("O05", "Variant()(1 To 3, 1 To 1){Double:1; Double:2; Double:3}", DisplayName = "Application.Transpose of a 1-D array: its shape")]
    [DataRow("C01", "Variant()(1 To 2, 1 To 2){Double:5, Double:5; Double:5, Double:5}", DisplayName = "a number into every cell of a block")]
    [DataRow("C02", "Variant()(1 To 2, 1 To 2){String:\"text\", String:\"text\"; String:\"text\", String:\"text\"}", DisplayName = "a String into every cell")]
    [DataRow("C03", "Date:2026-11-12 00:00:00", DisplayName = "a Date")]
    [DataRow("C04", "Variant()(1 To 1, 1 To 3){Empty, Empty, Double:2.5}", DisplayName = "Empty clears the cells")]
    [DataRow("C05", "Empty", DisplayName = "Null")]
    [DataRow("C06", "|error 1004", DisplayName = "Nothing")]
    [DataRow("C07", "Currency:2.5 / Double:2.5", DisplayName = "a Currency")]
    [DataRow("C08", "Boolean:True", DisplayName = "a Boolean")]
    [DataRow("C09", "Error:2042", IgnoreMessage = "an error cell read back: RD-VBA reads the Long code of its HRESULT where MS-VBA reads an Error, because the .NET late binder turns a VT_ERROR result into a number and RD-VBA does not call IDispatch itself", DisplayName = "an error value")]
    [DataRow("P01", "Variant()(1 To 2, 1 To 2){Double:1, Double:2; Double:3, Double:4}", DisplayName = "a parenthesized array")]
    [DataRow("P02", "Variant()(1 To 2, 1 To 3){Double:1, Empty, Double:3; Empty, Double:5, Empty}", DisplayName = "Resize(UBound, UBound)")]
    [DataRow("P03", "Variant()(1 To 3, 1 To 3){Double:1, String:\"text\", Double:2.5; Double:4, Double:5, Double:6; Double:7, Double:8, Double:9}", DisplayName = "Range(\"A1\").Resize(n, m).Value = arr")]
    [DataRow("P04", "Variant()(1 To 2, 1 To 2){Double:1, Double:2; Double:3, Double:4}", DisplayName = "Range(Cells, Cells)")]
    [DataRow("P05", "Variant()(1 To 2, 1 To 2){Double:1, String:\"text\"; Double:4, Double:5}", DisplayName = "through a helper's ByRef Variant parameter")]
    [DataRow("P06", "Variant()(1 To 2, 1 To 2){Double:1, String:\"text\"; Double:4, Double:5}", DisplayName = "through a helper's ByVal Variant parameter")]
    [DataRow("P07", "Variant()(1 To 2, 1 To 2){Empty, Empty; Empty, Empty}", DisplayName = "a Range object assigned to Value: Excel is given the object, and leaves the cells Empty")]
    [DataRow("V01", "Variant()(1 To 2, 1 To 3){Empty, Empty, Empty; Empty, Empty, Integer:7}", DisplayName = "ReDim v(1 To 2, 1 To 3) in a Variant, written and read")]
    [DataRow("V02", "Variant()(1 To 2, 1 To 3){Integer:99, String:\"text\", Double:2.5; Double:4, Double:5, Double:6} / Double:1", DisplayName = "an element written after the get: the sheet does not change")]
    [DataRow("V03", "1 2 1 3", DisplayName = "LBound and UBound of each dimension after the get")]
    [DataRow("V04", "|error 9", DisplayName = "UBound of a third dimension")]
    [DataRow("V05", "|error 9", DisplayName = "one subscript of a 2-D array")]
    [DataRow("V06", "True Variant() 8204", DisplayName = "IsArray and TypeName after the get")]
    [DataRow("V07", "True Variant() Variant()(unallocated)", DisplayName = "Erase of a Variant holding the get")]
    [DataRow("V08", " Double:1 Double:4 String:\"text\" Double:5 Double:2.5 Double:6", DisplayName = "For Each over the get: column by column")]
    [DataRow("V09", "Variant()(1 To 2, 1 To 4){Double:1, String:\"text\", Double:2.5, Empty; Double:4, Double:5, Double:6, Empty}", DisplayName = "ReDim Preserve of the last dimension after the get")]
    [DataRow("V10", "|error 9", DisplayName = "ReDim Preserve of the first dimension")]
    [DataRow("V11", "|error 9", DisplayName = "an element out of range")]
    [DataRow("V12", "Variant()(1 To 2, 1 To 3){Double:1, String:\"text\", Double:2.5; Double:4, String:\"changed\", Double:6}", DisplayName = "a Variant array written back to the sheet after it was changed")]
    [DataRow("C10", "#DIV/0!", DisplayName = "CVErr(2007) into a cell: what the sheet shows")]
    [DataRow("C11", "Boolean:True 2007", IgnoreMessage = "an error cell read back: RD-VBA reads the Long code of its HRESULT where MS-VBA reads an Error, because the .NET late binder turns a VT_ERROR result into a number and RD-VBA does not call IDispatch itself", DisplayName = "a #DIV/0! cell read back: an Error, its Long error code")]
    [DataRow("S14", "#N/A Double:1", DisplayName = "a Variant array holding an Error, written to a range")]
    [DataRow("E01", "Boolean:True", DisplayName = "CBool of a Variant holding an Error")]
    [DataRow("E02", "|error 6", DisplayName = "CByte of an Error")]
    [DataRow("E03", "Currency:2007", DisplayName = "CCur of an Error")]
    [DataRow("E04", "Double:2007", DisplayName = "CDbl of an Error")]
    [DataRow("E05", "Integer:2007", DisplayName = "CInt of an Error")]
    [DataRow("E06", "Long:2007", DisplayName = "CLng of an Error")]
    [DataRow("E07", "LongLong:2007", DisplayName = "CLngLng of an Error")]
    [DataRow("E08", "LongLong:2007", DisplayName = "CLngPtr of an Error")]
    [DataRow("E09", "Single:2007", DisplayName = "CSng of an Error")]
    [DataRow("E10", "String:\"Error 2007\"", DisplayName = "CStr of an Error")]
    [DataRow("E11", "|error 13", DisplayName = "Str of an Error")]
    [DataRow("E12", "|error 13", DisplayName = "Str$ of an Error")]
    [DataRow("E13", "|error 13", DisplayName = "CDate of an Error")]
    [DataRow("E14", "|error 13", DisplayName = "CVDate of an Error")]
    [DataRow("E15", "|error 13", DisplayName = "CDec of an Error")]
    [DataRow("E16", "Error:2007", DisplayName = "CVar of an Error")]
    [DataRow("E17", "|error 13", DisplayName = "Fix of an Error")]
    [DataRow("E18", "|error 13", DisplayName = "Int of an Error")]
    [DataRow("E19", "|error 13", DisplayName = "Hex of an Error")]
    [DataRow("E20", "|error 13", DisplayName = "Oct of an Error")]
    [DataRow("E21", "|error 13", DisplayName = "Val of an Error")]
    [DataRow("E22", "|error 13", DisplayName = "an Error Let into a Long")]
    [DataRow("E23", "|error 13", DisplayName = "an Error Let into a String")]
    [DataRow("E24", "|error 13", DisplayName = "an Error Let into a Double")]
    public void ARow_DoesWhatMSVBADidInExcel(string id, string expected)
    {
        if (_said is null)
        {
            Assert.Inconclusive("Excel is not installed on this machine.");
        }

        Assert.IsTrue(_said.TryGetValue(id, out var said), $"The grid said nothing of {id}.");
        Assert.AreEqual(expected, said, string.Join(" / ", Cases.First(c => c.Id == id).Snippet));
    }

    // the Excel the grid started, by its window, if it is still there once the program quit it: never another, which another program may be using.
    private static void StopExcel(string? hwnd)
    {
        if (hwnd is null || !long.TryParse(hwnd.Trim(), out var handle) || GetWindowThreadProcessId(new IntPtr(handle), out var processId) == 0 || processId == 0)
        {
            return;
        }

        try
        {
            using var excel = System.Diagnostics.Process.GetProcessById((int)processId);
            if (!excel.WaitForExit(10_000) && excel.ProcessName.Equals("EXCEL", StringComparison.OrdinalIgnoreCase))
            {
                excel.Kill();
            }
        }
        catch (ArgumentException)
        {
            // it has quit.
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
