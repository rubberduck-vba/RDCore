using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>ReDim</c> of a Variant that holds a value, and an <c>Erase</c> of one (<strong>MS-VBAL §5.4.3.3-4</strong>, RD-VBAL 5.4.3.3); a value assigned
/// to a resizable array (§5.4.3.8).
/// </summary>
/// <remarks>
/// Each expected outcome is what MS-VBA printed, or the compile error it reported, for the same code in Excel (64-bit, en-US).
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.3 ReDim")]
[TestCategory("MS-VBAL 5.4.3.4 Erase")]
public sealed class RedimOfAVariantTests
{
    private static string Program(string body) => string.Join("\r\n",
        "Attribute VB_Name = \"Program\"",
        "Option Base 0",
        "Public Sub Main()",
        "    On Error Resume Next",
        "    " + string.Join("\r\n    ", body.Split(" ;; ")),
        "End Sub",
        string.Empty);

    [TestMethod]
    [DataRow("Dim y ;; y = 5 ;; ReDim y(2) ;; Debug.Print Err.Number & \" \" & TypeName(y) & \" \" & UBound(y)", "0 Variant() 2", DisplayName = "V01 a number")]
    [DataRow("Dim y ;; y = 5 ;; ReDim Preserve y(2) ;; Debug.Print Err.Number & \" \" & TypeName(y)", "13 Integer", DisplayName = "V02 Preserve of a number")]
    [DataRow("Dim y ;; ReDim Preserve y(2) ;; Debug.Print Err.Number & \" \" & TypeName(y)", "13 Empty", DisplayName = "H01 Preserve of nothing yet")]
    [DataRow("Dim y ;; ReDim Preserve y(2) ;; Debug.Print IsArray(y)", "False", DisplayName = "H02 Preserve of nothing yet makes no array")]
    [DataRow("Dim y As Variant ;; y = Empty ;; ReDim Preserve y(2) ;; Debug.Print Err.Number & \" \" & TypeName(y)", "13 Empty", DisplayName = "H03 Preserve of Empty")]
    [DataRow("Dim y ;; y = Null ;; ReDim Preserve y(2) ;; Debug.Print Err.Number & \" \" & TypeName(y)", "13 Null", DisplayName = "H04 Preserve of Null")]
    [DataRow("Dim y ;; y = \"abc\" ;; ReDim Preserve y(2) ;; Debug.Print Err.Number & \" \" & TypeName(y)", "13 String", DisplayName = "H05 Preserve of a String")]
    [DataRow("Dim y ;; ReDim y(1) ;; ReDim Preserve y(2) ;; Debug.Print Err.Number & \" \" & TypeName(y) & \" \" & UBound(y)", "0 Variant() 2", DisplayName = "H06 Preserve of an array")]
    [DataRow("Dim y ;; ReDim Preserve y(2) As Long ;; Debug.Print Err.Number & \" \" & TypeName(y)", "13 Empty", DisplayName = "H07 Preserve As Long of nothing yet")]
    [DataRow("Dim y ;; y = \"abc\" ;; ReDim y(2) ;; Debug.Print Err.Number & \" \" & TypeName(y)", "0 Variant()", DisplayName = "V04 a String")]
    [DataRow("Dim y ;; Set y = New Collection ;; ReDim y(2) ;; Debug.Print Err.Number & \" \" & TypeName(y)", "0 Variant()", DisplayName = "V05 an object")]
    [DataRow("Dim y ;; y = Null ;; ReDim y(2) ;; Debug.Print Err.Number & \" \" & TypeName(y)", "0 Variant()", DisplayName = "V06 Null")]
    [DataRow("Dim y ;; y = 5 ;; Erase y ;; Debug.Print Err.Number & \" \" & TypeName(y)", "13 Integer", DisplayName = "V07 Erase of a number")]
    [DataRow("Dim y ;; Erase y ;; Debug.Print Err.Number & \" \" & TypeName(y)", "13 Empty", DisplayName = "V08 Erase of nothing yet")]
    [DataRow("Dim y As Variant ;; y = 5 ;; ReDim y(1 To 3) As Long ;; Debug.Print Err.Number & \" \" & TypeName(y)", "0 Long()", DisplayName = "V09 As Long")]
    public async Task AVariantThatHoldsAValue(string body, string expected)
        => Assert.AreEqual(expected, (await RunAsync([], Program(body))).Last());

    // an upper bound below the lower: 7 for a Variant, which is left Empty; 9 for a variable declared as an array (vb6.exe gives the same 7).
    [TestMethod]
    [DataRow("Dim q ;; q = 5 ;; ReDim q(5 To 2) ;; Debug.Print Err.Number & \" \" & TypeName(q)", "7 Empty", DisplayName = "B01 a Variant that holds a number")]
    [DataRow("Dim q ;; ReDim q(5 To 2) ;; Debug.Print Err.Number & \" \" & TypeName(q)", "7 Empty", DisplayName = "B02 a Variant that holds nothing yet")]
    [DataRow("Dim q ;; q = 5 ;; ReDim Preserve q(5 To 2) ;; Debug.Print Err.Number & \" \" & TypeName(q)", "13 Integer", DisplayName = "B05 Preserve of a number")]
    [DataRow("Dim a() As Long ;; ReDim a(5 To 2) ;; Debug.Print Err.Number", "9", DisplayName = "B03 a Long()")]
    [DataRow("ReDim z(5 To 2) ;; Debug.Print Err.Number", "9", DisplayName = "B04 one a ReDim declares")]
    [DataRow("x = 5 ;; ReDim x(5 To 2) ;; Debug.Print Err.Number & \" \" & TypeName(x)", "7 Empty", DisplayName = "#9 an implicit Variant")]
    public async Task AnUpperBoundBelowTheLower(string body, string expected)
        => Assert.AreEqual(expected, (await RunAsync([], Program(body))).Last());

    // what an array is left with (observed in Excel): a variable declared as an array raises 9 and loses its
    // dimensions, and keeps them with Preserve; a Variant one below raises 9 and keeps what it held, and further below raises 7 and is left Empty. Each
    // row prints the error, then each read on its own line, a read that fails as e and its error.
    [TestMethod]
    [DataRow("Dim v() As Long ;; ReDim v(5 To 10) ;; v(5) = 3 ;; ReDim v(5 To 4)", "LBound(v) ;; UBound(v) ;; v(5)", "9|e9|e9|e9", DisplayName = "F01 a Long(), one below")]
    [DataRow("Dim v() As Long ;; ReDim v(5 To 10) ;; v(5) = 3 ;; ReDim v(5 To 2)", "LBound(v) ;; UBound(v) ;; v(5)", "9|e9|e9|e9", DisplayName = "F02 a Long(), further below")]
    [DataRow("Dim v() As Long ;; ReDim v(5 To 10) ;; v(5) = 3 ;; ReDim v(1 To 0, 1 To 2)", "LBound(v)", "9|e9", DisplayName = "F08 a Long(), the first of two")]
    [DataRow("Dim v() As Long ;; ReDim v(5 To 10) ;; v(5) = 3 ;; ReDim v(1 To 2, 1 To 0)", "LBound(v)", "9|e9", DisplayName = "F09 a Long(), the last of two")]
    [DataRow("Dim v() As Long ;; ReDim v(0 To -1)", "LBound(v)", "9|e9", DisplayName = "F11 a Long() never dimensioned")]
    [DataRow("Dim v() As Long ;; ReDim v(5 To 10) ;; v(5) = 3 ;; ReDim Preserve v(5 To 2)", "LBound(v) & \",\" & UBound(v) & \",\" & v(5)", "9|5,10,3", DisplayName = "F07 a Long(), Preserve")]
    [DataRow("Dim v As Variant ;; ReDim v(5 To 10) ;; v(5) = 3 ;; ReDim v(5 To 4)", "LBound(v) & \",\" & UBound(v) & \",\" & v(5)", "9|5,10,3", DisplayName = "R06 a Variant, one below")]
    [DataRow("Dim v As Variant ;; ReDim v(5 To 10) ;; v(5) = 3 ;; ReDim v(5 To 3)", "TypeName(v)", "7|Empty", DisplayName = "F04 a Variant, two below")]
    [DataRow("Dim v As Variant ;; ReDim v(5 To 10) ;; v(5) = 3 ;; ReDim v(5 To 2)", "TypeName(v)", "7|Empty", DisplayName = "F03 a Variant, further below")]
    [DataRow("Dim v As Variant ;; ReDim v(5 To 4)", "TypeName(v)", "9|Empty", DisplayName = "F05 an Empty Variant, one below")]
    [DataRow("Dim v As Variant ;; v = 5 ;; ReDim v(5 To 4)", "TypeName(v)", "9|Integer", DisplayName = "F06 a Variant of 5, one below")]
    [DataRow("Dim v As Variant ;; ReDim v(5 To 10) ;; v(5) = 3 ;; ReDim v(1 To 2, 3 To 2)", "TypeName(v) ;; UBound(v)", "9|Variant()|10", DisplayName = "F10 a Variant, the last of two one below")]
    [DataRow("Dim v As Variant ;; ReDim v(1 To 3, 1 To 3) ;; v(1, 1) = 7 ;; ReDim v(5 To 4, 5 To 2)", "TypeName(v) ;; v(1, 1)", "9|Variant()|7",
        DisplayName = "I05 a Variant, one dimension one below and the next further")]
    [DataRow("Dim v As Variant ;; ReDim v(1 To 3, 1 To 3) ;; v(1, 1) = 7 ;; ReDim v(5 To 2, 5 To 4)", "TypeName(v) ;; v(1, 1)", "9|Variant()|7",
        DisplayName = "I06 a Variant, one dimension further below and the next one below")]
    [DataRow("Dim v As Variant ;; ReDim v(5 To 10) ;; v(5) = 3 ;; ReDim Preserve v(5 To 4)", "UBound(v)", "9|10", DisplayName = "F12 a Variant, Preserve one below")]
    [DataRow("Dim v As Variant ;; ReDim v(5 To 10) ;; v(5) = 3 ;; ReDim Preserve v(5 To 2)", "TypeName(v) ;; UBound(v)", "9|Variant()|10", DisplayName = "F13 a Variant, Preserve further below")]
    public async Task WhatAnUpperBoundBelowTheLower_LeavesTheVariableWith(string statements, string reads, string expected)
    {
        var lines = reads.Split(" ;; ").SelectMany(read => new[] { "Err.Clear", $"Debug.Print {read}", "If Err.Number <> 0 Then Debug.Print \"e\" & Err.Number" });
        var output = await RunAsync([], Program($"{statements} ;; Debug.Print Err.Number ;; {string.Join(" ;; ", lines)}"));

        Assert.AreEqual(expected, string.Join("|", output));
    }

    // every bound is evaluated, its side effects with it, before an inverted dimension's 9, whichever dimension is inverted: a bound that is no number
    // is 13 and one that overflows 6 (observed in Excel). Bump counts its calls; each P prints the error and clears it.
    [TestMethod]
    [DataRow("Dim a() As Long ;; ReDim a(5 To 4, 1 To \"x\") ;; P ;; ReDim a(1 To \"x\", 5 To 4) ;; P ;; ReDim a(5 To 4, 1 To 2 ^ 40) ;; P", "e13|e13|e6",
        DisplayName = "R01 a Long(), a bound that is no number or overflows")]
    [DataRow("Dim a() As Long ;; ReDim a(5 To 4, 1 To Bump()) ;; P ;; Debug.Print Counter ;; ReDim a(1 To Bump(), 5 To 4) ;; P ;; Debug.Print Counter ;; ReDim a(5 To 4, Bump() To 1) ;; P ;; Debug.Print Counter",
        "e9|1|e9|2|e9|3", DisplayName = "R02 a Long(), a bound with a side effect")]
    [DataRow("Dim v As Variant ;; ReDim v(5 To 4, 1 To \"x\") ;; P ;; ReDim v(5 To 4, 1 To Bump()) ;; P ;; Debug.Print Counter", "e13|e9|1",
        DisplayName = "R03 a Variant")]
    [DataRow("ReDim z(5 To 4, 1 To Bump()) ;; P ;; Debug.Print Counter ;; ReDim y(5 To 4, 1 To \"x\") ;; P", "e9|1|e13", DisplayName = "R04 one a ReDim declares")]
    [DataRow("Dim a() As Long ;; ReDim a(1 To 2) ;; ReDim Preserve a(5 To 4) ;; P ;; Dim m() As Long ;; ReDim m(1 To 2, 1 To 2) ;; ReDim Preserve m(1 To 2, 5 To Bump()) ;; P ;; Debug.Print Counter",
        "e9|e9|1", DisplayName = "R05 Preserve")]
    public async Task EveryBound_IsEvaluated_BeforeAnInvertedDimensionsError(string body, string expected)
    {
        var program = string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            "Private Counter As Long",
            "Private Function Bump() As Long",
            "    Counter = Counter + 1",
            "    Bump = 2",
            "End Function",
            "Public Sub Main()",
            "    On Error Resume Next",
            "    " + string.Join("\r\n    ", body.Split(" ;; ").Select(line => line == "P" ? "Debug.Print \"e\" & Err.Number: Err.Clear" : line)),
            "End Sub",
            string.Empty);

        Assert.AreEqual(expected, string.Join("|", await RunAsync([], program)));
    }

    [TestMethod]
    [DataRow("Dim a() As Long ;; a = 5", DisplayName = "A03 a Long()")]
    [DataRow("Dim a() As Variant ;; a = 5", DisplayName = "A04 a Variant()")]
    [DataRow("ReDim y(2) As Long ;; y = 5", DisplayName = "A06 one a ReDim declares As Long")]
    [DataRow("ReDim y(2) ;; y = 5", DisplayName = "R02 one a ReDim declares")]
    [DataRow("Dim a() As Long ;; a = \"abc\"", DisplayName = "G01 a String to a Long()")]
    [DataRow("Dim a() As Variant ;; a = \"abc\"", DisplayName = "G02 a String to a Variant()")]
    [DataRow("Dim a() As String ;; a = \"abc\"", DisplayName = "G03 a String to a String()")]
    [DataRow("Dim a() As Long ;; Dim s As String ;; s = \"1\" ;; a = s", DisplayName = "G04 a String variable")]
    [DataRow("Dim a() As Long ;; a = #1/1/2000#", DisplayName = "G05 a Date")]
    [DataRow("Dim a() As Variant ;; Dim d As Date ;; a = d", DisplayName = "G06 a Date variable to a Variant()")]
    [DataRow("Dim a() As Long ;; a = True", DisplayName = "G07 a Boolean")]
    [DataRow("Dim a() As Boolean ;; Dim b As Boolean ;; a = b", DisplayName = "G08 a Boolean to a Boolean()")]
    [DataRow("Dim a() As Long ;; Dim c As Currency ;; a = c", DisplayName = "G10 a Currency")]
    public async Task AValueAssignedToAResizableArray_IsACompileError(string body)
    {
        var errors = await LoadErrorsAsync([], Program(body));

        StringAssert.StartsWith(Assert.ContainsSingle(errors), "Can't assign to array");
    }

    // A02's `y = Empty` compiles and raises 13, which keeps the Variant() it printed.
    [TestMethod]
    [DataRow("ReDim y(2) ;; y = Array(1, 2) ;; Debug.Print TypeName(y)", "Variant()", DisplayName = "A01 an array")]
    [DataRow("Dim a() As Variant ;; a = Array(1, 2) ;; Debug.Print UBound(a)", "1", DisplayName = "A05 an array to a Variant()")]
    [DataRow("Dim a() As Byte ;; a = \"AB\" ;; Debug.Print Err.Number & \" \" & UBound(a)", "0 3", DisplayName = "G09 a String to a Byte()")]
    [DataRow("Dim a() As Long ;; Dim v As Variant ;; v = 5 ;; a = v ;; Debug.Print Err.Number", "13", DisplayName = "G12 a Variant that holds a number")]
    public async Task AnArrayOrAVariantAssignedToAResizableArray_IsAssigned(string body, string expected)
        => Assert.AreEqual(expected, (await RunAsync([], Program(body))).Last());
}
