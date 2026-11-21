using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// Where <c>VarPtr</c> says a variable is: on the boundary its type is aligned to, as <strong>MS-VBA</strong> places it (observed in Excel, 64-bit). The
/// variables of a module are side by side in declaration order, each on its boundary; a record is as wide as its layout, padding included, and the
/// elements of an array of records are that far apart.
/// </summary>
[TestClass]
public sealed class VarPtrAlignmentTests
{
    // what follows a Byte, and an Integer, on an 8-byte boundary: a Double before each puts it there, as the start of a module is in MS-VBA.
    private static readonly string[] Followers =
        ["Byte", "Integer", "Boolean", "Long", "Single", "Double", "Currency", "Date", "LongLong", "LongPtr", "String", "Object", "Variant", "TBD", "T3B"];

    private static readonly (string Name, string Source) Aligned = ("Aligned", StandardModule("Aligned", [
        "Option Explicit",
        "Public Type TPair", "    X As Long", "    Y As Long", "End Type",
        "Public Type TBD", "    B As Byte", "    D As Double", "End Type",
        "Public Type T3B", "    B1 As Byte", "    B2 As Byte", "    B3 As Byte", "End Type",
        "Public Type TIB", "    I As Integer", "    B As Byte", "End Type",
        "Public Type TBL", "    B As Byte", "    L As Long", "End Type",
        "Public Type TBV", "    B As Byte", "    V As Variant", "End Type",
        "Public Type TBN", "    B As Byte", "    Inner As TBL", "End Type",
        "Public Type TBCB", "    B As Byte", "    C As Currency", "    B2 As Byte", "End Type",
        "Public Type TBA", "    B As Byte", "    A(1) As Integer", "End Type",
        "Public Type TILL", "    I As Integer", "    LL As LongLong", "End Type",
        "Public Type TBF", "    B As Byte", "    F As String * 3", "    L As Long", "End Type",
        "Public Type TBDA", "    B As Byte", "    D() As Long", "End Type",
        "Private anchor As Double",
        "Private b As Byte", "Private i As Integer", "Private l As Long", "Private d As Double", "Private s As String", "Private v As Variant",
        "Private c As Currency", "Private b2 As Byte", "Private ll As LongLong", "Private r As TBD", "Private b3 As Byte",
        .. Followers.SelectMany((type, k) => new[] { $"Private ab{k} As Double", $"Private b{k}x As Byte", $"Private xb{k} As {type}" }),
        .. Followers.SelectMany((type, k) => new[] { $"Private ai{k} As Double", $"Private i{k}x As Integer", $"Private xi{k} As {type}" }),
        "Public Function Offsets() As String",
        "    Offsets = (VarPtr(i) - VarPtr(b)) & \"|\" & (VarPtr(l) - VarPtr(b)) & \"|\" & (VarPtr(d) - VarPtr(b)) & \"|\" & (VarPtr(s) - VarPtr(b))",
        "    Offsets = Offsets & \"|\" & (VarPtr(v) - VarPtr(b)) & \"|\" & (VarPtr(c) - VarPtr(b)) & \"|\" & (VarPtr(b2) - VarPtr(b))",
        "    Offsets = Offsets & \"|\" & (VarPtr(ll) - VarPtr(b)) & \"|\" & (VarPtr(r) - VarPtr(b)) & \"|\" & (VarPtr(b3) - VarPtr(b))",
        "End Function",
        "Public Function AfterAByte() As String",
        .. Followers.Select((_, k) => $"    AfterAByte = AfterAByte & (VarPtr(xb{k}) - VarPtr(b{k}x)) & \"|\""),
        "End Function",
        "Public Function AfterAnInteger() As String",
        .. Followers.Select((_, k) => $"    AfterAnInteger = AfterAnInteger & (VarPtr(xi{k}) - VarPtr(i{k}x)) & \"|\""),
        "End Function"]));

    private static string Program(string body)
        => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPrivate acc As String\r\n"
            + "Private Sub P(ByVal v As Variant)\r\n    acc = acc & CStr(v) & \"|\"\r\nEnd Sub\r\n"
            + $"Public Sub Main()\r\nOn Error Resume Next\r\n{string.Join("\r\n", body.Split(" ;; "))}\r\nDebug.Print acc\r\nEnd Sub\r\n";

    private static async Task<string> RunAsync(string body)
        => string.Join(" ", await ModuleWorkspace.RunAsync([], [Aligned], Program(body)));

    [TestMethod]
    public async Task TheVariablesOfAModule_AreSideBySide_InDeclarationOrder_EachOnItsBoundary()
        => Assert.AreEqual("2|4|8|16|24|48|56|64|72|88|", await RunAsync("P Aligned.Offsets()"));

    [TestMethod]
    public async Task AModuleVariable_AfterAByte_IsOnItsBoundary()
        => Assert.AreEqual("1|2|2|4|4|8|8|8|8|8|8|8|8|8|1||", await RunAsync("P Aligned.AfterAByte()"));

    [TestMethod]
    public async Task AModuleVariable_AfterAnInteger_IsOnItsBoundary()
        => Assert.AreEqual("2|2|2|4|4|8|8|8|8|8|8|8|8|8|2||", await RunAsync("P Aligned.AfterAnInteger()"));

    [TestMethod]
    [DataRow("Integer", 2)]
    [DataRow("Boolean", 2)]
    [DataRow("Long", 4)]
    [DataRow("Single", 4)]
    [DataRow("Double", 8)]
    [DataRow("Currency", 8)]
    [DataRow("Date", 8)]
    [DataRow("LongLong", 8)]
    [DataRow("LongPtr", 8)]
    [DataRow("String", 8)]
    [DataRow("Object", 8)]
    [DataRow("Variant", 8)]
    [DataRow("TPair", 4)]
    [DataRow("TBD", 8)]
    [DataRow("TIB", 2)]
    public async Task ALocal_AfterAByteOrAnInteger_IsOnItsBoundary(string type, int boundary)
        => Assert.AreEqual("True|0|True|0|", await RunAsync(
            $"Dim b As Byte, x As {type}, i As Integer, y As {type} ;; P (VarPtr(b) <> 0) ;; P (VarPtr(x) Mod {boundary})"
            + $" ;; P (VarPtr(i) <> 0) ;; P (VarPtr(y) Mod {boundary})"));

    [TestMethod]
    [DataRow("Dim a(2) As TBD ;; P VarPtr(a(1)) - VarPtr(a(0)) ;; P VarPtr(a(1).D) - VarPtr(a(0)) ;; P (VarPtr(a(0)) Mod 8)", "16|24|0|",
        DisplayName = "Byte and Double")]
    [DataRow("Dim a(2) As TBL ;; P VarPtr(a(1)) - VarPtr(a(0)) ;; P VarPtr(a(1).L) - VarPtr(a(0)) ;; P (VarPtr(a(0)) Mod 4)", "8|12|0|",
        DisplayName = "Byte and Long")]
    [DataRow("Dim a(2) As TIB ;; P VarPtr(a(1)) - VarPtr(a(0)) ;; P (VarPtr(a(0)) Mod 2)", "4|0|", DisplayName = "Integer and Byte")]
    [DataRow("Dim a(2) As T3B ;; P VarPtr(a(1)) - VarPtr(a(0)) ;; P VarPtr(a(2)) - VarPtr(a(0))", "3|6|", DisplayName = "three Bytes")]
    [DataRow("Dim a() As TBD ;; ReDim a(2) ;; P VarPtr(a(1)) - VarPtr(a(0)) ;; P (VarPtr(a(0)) Mod 8)", "16|0|", DisplayName = "resizable, Byte and Double")]
    [DataRow("Dim a() As T3B ;; ReDim a(2) ;; P VarPtr(a(1)) - VarPtr(a(0))", "3|", DisplayName = "resizable, three Bytes")]
    public async Task TheElementsOfAnArrayOfRecords_AreAsFarApartAsARecordIsWide_FromItsBoundary(string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(body));

    [TestMethod]
    [DataRow("Dim r As TBV ;; P VarPtr(r.V) - VarPtr(r) ;; P LenB(r) ;; P Len(r)", "8|32|25|", DisplayName = "Byte and Variant")]
    [DataRow("Dim r As TBN ;; P VarPtr(r.Inner) - VarPtr(r) ;; P VarPtr(r.Inner.L) - VarPtr(r) ;; P LenB(r) ;; P Len(r)", "4|8|12|6|",
        DisplayName = "a nested record")]
    [DataRow("Dim r As TBCB ;; P VarPtr(r.C) - VarPtr(r) ;; P VarPtr(r.B2) - VarPtr(r) ;; P LenB(r) ;; P Len(r)", "8|16|24|10|",
        DisplayName = "Byte, Currency, Byte")]
    [DataRow("Dim r As TBA ;; P VarPtr(r.A(0)) - VarPtr(r) ;; P VarPtr(r.A(1)) - VarPtr(r) ;; P LenB(r) ;; P Len(r)", "2|4|6|5|",
        DisplayName = "a fixed-size array field")]
    [DataRow("Dim r As TILL ;; P VarPtr(r.LL) - VarPtr(r) ;; P LenB(r) ;; P Len(r)", "8|16|10|", DisplayName = "Integer and LongLong")]
    [DataRow("Dim r As TBF ;; P VarPtr(r.L) - VarPtr(r) ;; P LenB(r) ;; P Len(r)", "8|12|8|", DisplayName = "a fixed-length String field")]
    [DataRow("Dim r As TBDA ;; P LenB(r) ;; P Len(r) ;; ReDim r.D(1) ;; P VarPtr(r.D(1)) - VarPtr(r.D(0))", "16|9|4|", DisplayName = "a resizable array field")]
    public async Task TheFieldsOfARecord_AreOnTheirBoundaries_AndItsLenBCountsThePadding(string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(body));
}
