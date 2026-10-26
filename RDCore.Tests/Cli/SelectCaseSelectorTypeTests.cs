using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// RD-VBAL §5.4.2.10: the value of each <c>Case</c> clause is let-coerced to the declared type of the selector, and compared with the selector in that type,
/// as MS-VBA does - where MS-VBAL §5.4.2.10 states the comparison as an expression of the two. A selector declared <c>Variant</c> is compared with the value as
/// it is. Each row is the probe it was recorded with in Excel, under <c>On Error Resume Next</c> (grids <c>oracle\s46</c> F15a-g, <c>oracle\s48</c>).
/// </summary>
[TestClass]
public sealed class SelectCaseSelectorTypeTests
{
    private static string Run(string body, string declarations = "")
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" + string.Join("\r\n", declarations.Split(" ;; "))
            + "\r\nPrivate Sub P(ByVal v As Variant)\r\nDebug.Print CStr(v)\r\nEnd Sub\r\nPublic Sub Main()\r\nOn Error Resume Next\r\n"
            + string.Join("\r\n", body.Split(" ;; ")) + "\r\nEnd Sub\r\n";
        return string.Join("/", RunAsync([], program).GetAwaiter().GetResult());
    }

    [TestMethod]
    [DataRow("Dim n As Long: n = 2 ;; Select Case n ;; Case 1.7: P \"1 matched\" ;; Case Else: P \"1 else\" ;; End Select", "1 matched", DisplayName = "F15a Long, Case 1.7")]
    [DataRow("Dim v: v = 5 ;; Select Case CInt(v) ;; Case Is < 5.4: P \"2 matched\" ;; Case Else: P \"2 else\" ;; End Select", "2 else", DisplayName = "F15b CInt(v), Case Is < 5.4")]
    [DataRow("Dim b As Boolean: b = False ;; Select Case b ;; Case Is < 1: P \"3 matched\" ;; Case Else: P \"3 else\" ;; End Select", "3 else", DisplayName = "F15c Boolean, Case Is < 1")]
    [DataRow("Dim s As String: s = \"1.0\" ;; Select Case s ;; Case 1: P \"4 matched\" ;; Case Else: P \"4 else\" ;; End Select", "4 else", DisplayName = "F15d String, Case 1")]
    [DataRow("Dim s As String: s = \"10\" ;; Select Case s ;; Case Is < 5: P \"m\" ;; Case Else: P \"e\" ;; End Select ;; P (s < 5)", "m/False", DisplayName = "G03 String, Case Is < 5")]
    [DataRow("Dim n As Long: n = 2 ;; Select Case n ;; Case 1.5 To 2.4: P \"m\" ;; Case Else: P \"e\" ;; End Select ;; n = 1 ;; Select Case n ;; Case 1.5 To 2.4: P \"m\" ;; Case Else: P \"e\" ;; End Select", "m/e", DisplayName = "G05 Long, Case 1.5 To 2.4")]
    [DataRow("Dim n As Long: n = 2 ;; Dim v: v = \"2\" ;; Select Case n ;; Case v: P \"m\" ;; Case Else: P \"e\" ;; End Select ;; v = \"1.6\" ;; Select Case n ;; Case v: P \"m\" ;; Case Else: P \"e\" ;; End Select", "m/m", DisplayName = "G06 Long, Case a Variant String")]
    [DataRow("Dim d As Date: d = #12/31/1899# ;; Select Case d ;; Case 1: P \"m\" ;; Case Else: P \"e\" ;; End Select ;; Select Case d ;; Case \"12/31/1899\": P \"m\" ;; Case Else: P \"e\" ;; End Select", "m/m", DisplayName = "G07 Date, Case 1 and Case a date String")]
    [DataRow("Dim c As Currency: c = 1.2346 ;; Select Case c ;; Case 1.23456: P \"m\" ;; Case Else: P \"e\" ;; End Select", "m", DisplayName = "G08 Currency, Case 1.23456")]
    [DataRow("Dim b As Boolean: b = True ;; Select Case b ;; Case 5: P \"m\" ;; Case Else: P \"e\" ;; End Select ;; P (b = 5)", "m/False", DisplayName = "G09 Boolean, Case 5")]
    [DataRow("Dim n As Long: n = 2 ;; Select Case n + 0.5 ;; Case 2.5: P \"m\" ;; Case Else: P \"e\" ;; End Select ;; Select Case n * 1 ;; Case 1.7: P \"m\" ;; Case Else: P \"e\" ;; End Select", "m/m", DisplayName = "G10 the declared type of an expression")]
    [DataRow("Dim n As Long: n = 3 ;; Select Case n ;; Case 1, 2.6: P \"m\" ;; Case Else: P \"e\" ;; End Select", "m", DisplayName = "G13 Long, Case 1, 2.6")]
    [DataRow("Dim s As String: s = \"abc\" ;; Select Case s ;; Case \"ABC\": P \"m\" ;; Case Else: P \"e\" ;; End Select", "e", DisplayName = "G19 String, Option Compare Binary")]
    [DataRow("Dim i As Integer: i = 2 ;; Select Case i ;; Case Is < 2.5: P \"m\" ;; Case Else: P \"e\" ;; End Select ;; Select Case i ;; Case Is <= 1.5: P \"m\" ;; Case Else: P \"e\" ;; End Select", "e/m", DisplayName = "G20 Integer, Case Is < 2.5 and Is <= 1.5, rounded to even")]
    [DataRow("Dim f As String * 5: f = \"ab\" ;; Select Case f ;; Case \"ab\": P \"m\" ;; Case Else: P \"e\" ;; End Select ;; Select Case f ;; Case \"ab   \": P \"m\" ;; Case Else: P \"e\" ;; End Select", "e/m", DisplayName = "H07 String * 5, compared as a String")]
    public void TheValueOfACase_IsConvertedToTheDeclaredTypeOfTheSelector(string body, string expected)
        => Assert.AreEqual(expected, Run(body));

    // a Single, a Double and a Currency selector convert a value too; an integer is not rounded to a Single, and is compared at its exact value (s48 M01-M07).
    [TestMethod]
    [DataRow("Dim s As Single: s = 0.1 ;; Select Case s ;; Case 0.1: P \"m\" ;; Case Else: P \"e\" ;; End Select", "m", DisplayName = "M01 Single, Case 0.1")]
    [DataRow("Dim s As Single: s = 16777216 ;; Select Case s ;; Case 16777217: P \"m\" ;; Case Else: P \"e\" ;; End Select ;; Select Case s ;; Case Is < 16777217: P \"m\" ;; Case Else: P \"e\" ;; End Select", "e/m", DisplayName = "M02 Single, an integer at its exact value")]
    [DataRow("Dim s As Single: s = 0.5 ;; Select Case s ;; Case Is < 0.50000001: P \"m\" ;; Case Else: P \"e\" ;; End Select", "e", DisplayName = "M03 Single, Case Is < 0.50000001")]
    [DataRow("Dim c As Currency: c = 2 ;; Select Case c ;; Case 2.00001: P \"m\" ;; Case Else: P \"e\" ;; End Select", "m", DisplayName = "M04 Currency, Case 2.00001")]
    [DataRow("Dim n As Long: n = 2 ;; Select Case n ;; Case 1.6@: P \"m\" ;; Case Else: P \"e\" ;; End Select", "m", DisplayName = "M05 Long, a Currency")]
    [DataRow("Dim s As Single: s = 16777216 ;; Select Case s ;; Case \"16777217\": P \"m\" ;; Case Else: P \"e\" ;; End Select", "m", DisplayName = "M06 Single, a String")]
    [DataRow("Dim d As Double: d = 0.1 ;; Select Case d ;; Case CSng(0.1): P \"m\" ;; Case Else: P \"e\" ;; End Select", "e", DisplayName = "M07 Double, a Single")]
    public void AFloatingPointSelector_ConvertsAValue_ButNotAnIntegerToASingle(string body, string expected)
        => Assert.AreEqual(expected, Run(body));

    [TestMethod]
    public void AnEnumSelector_IsALong()
        => Assert.AreEqual("5 matched", Run("Dim e As Size: e = szFive ;; Select Case e ;; Case 4.6: P \"5 matched\" ;; Case Else: P \"5 else\" ;; End Select",
            "Public Enum Size ;; szFive = 5 ;; End Enum"));

    // the error a conversion raises is the Case's: Resume Next goes on in its body (MS-VBA, s48 G01).
    [TestMethod]
    [DataRow("Dim n As Long: n = 2 ;; Select Case n ;; Case \"abc\": P \"m\" ;; Case Else: P \"e\" ;; End Select ;; P \"after \" & Err.Number", "m/after 13", DisplayName = "G01 Long, Case \"abc\": 13")]
    [DataRow("Dim i As Integer: i = 2 ;; Select Case i ;; Case 40000: P \"m\" ;; Case Else: P \"e\" ;; End Select ;; P \"after \" & Err.Number", "m/after 6", DisplayName = "G02 Integer, Case 40000: 6")]
    [DataRow("Dim i As Integer: i = 2 ;; Select Case i ;; Case Null: P \"m\" ;; Case Else: P \"e\" ;; End Select ;; P \"after \" & Err.Number", "m/after 94", DisplayName = "G11 Integer, Case Null: 94")]
    [DataRow("Dim y As Byte: y = 2 ;; Select Case y ;; Case -1: P \"m\" ;; Case Else: P \"e\" ;; End Select ;; P \"after \" & Err.Number", "m/after 6", DisplayName = "G12 Byte, Case -1: 6")]
    [DataRow("Dim n As Long: n = 2 ;; Select Case n ;; Case \"abc\", 2: P \"m\" ;; Case Else: P \"e\" ;; End Select ;; P \"after \" & Err.Number", "m/after 13", DisplayName = "G15 the first clause raises")]
    [DataRow("Dim n As Long: n = 2 ;; Select Case n ;; Case 2, \"abc\": P \"m\" ;; Case Else: P \"e\" ;; End Select ;; P \"after \" & Err.Number", "m/after 0", DisplayName = "G16 a clause after the match is not evaluated")]
    [DataRow("Dim i As Integer: i = 2 ;; Select Case i ;; Case 1: P \"one\" ;; Case 40000: P \"m\" ;; Case Else: P \"e\" ;; End Select ;; P \"after \" & Err.Number", "m/after 6", DisplayName = "G17 the second Case raises")]
    public void AValueThatDoesNotConvert_RaisesTheErrorOfTheConversion(string body, string expected)
        => Assert.AreEqual(expected, Run(body));

    [TestMethod]
    [DataRow("Dim v: v = 2& ;; Select Case v ;; Case 1.7: P \"m\" ;; Case Else: P \"e\" ;; End Select ;; Dim d: d = 1.5 ;; Select Case d ;; Case 2: P \"m\" ;; Case Else: P \"e\" ;; End Select", "e/e", DisplayName = "G04 a Variant holding a number")]
    [DataRow("Dim h: h = 100 ;; Select Case h ;; Case \"100\": P \"m\" ;; Case Else: P \"e\" ;; End Select ;; P (h = \"100\")", "m/True", DisplayName = "G14 a Variant 100, Case \"100\"")]
    [DataRow("Dim h: h = 100 ;; Select Case h ;; Case Is > \"9\": P \"7 matched\" ;; Case Else: P \"7 else\" ;; End Select", "7 else", DisplayName = "F15g a Variant 100, Case Is > \"9\": compared as text")]    [DataRow("Dim e ;; Select Case e ;; Case 0: P \"m\" ;; Case Else: P \"e\" ;; End Select ;; Select Case e ;; Case \"\": P \"m\" ;; Case Else: P \"e\" ;; End Select", "m/m", DisplayName = "G19 an Empty Variant")]
    public void AVariantSelector_IsComparedWithTheValueAsItIs(string body, string expected)
        => Assert.AreEqual(expected, Run(body));
}
