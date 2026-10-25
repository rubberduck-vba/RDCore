using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>VarPtr</c>: the address of a variable in the session's memory, as <strong>MS-VBA</strong> gives it (recorded in Excel, oracle\s17 V rows and
/// oracle\s21) - never 0, the same for a variable for as long as it exists, the caller's for a <c>ByRef</c> parameter, and a temporary's for what is no
/// variable, which lives as long as the statement that passes it.
/// </summary>
[TestClass]
public sealed class VarPtrTests
{
    private static readonly (string Name, string Source) Holder = ("Holder", ClassModule("Holder",
        "Option Explicit", "Public Value As Long",
        "Public Function Addr() As LongPtr", "    Addr = VarPtr(Value)", "End Function",
        "Public Function MeAddr() As LongPtr", "    MeAddr = VarPtr(Me.Value)", "End Function",
        "Public Function Same() As Boolean", "    Same = (VarPtr(Value) = VarPtr(Value))", "End Function",
        "Public Property Get SelfAddr() As Boolean", "    Dim a As LongPtr", "    a = VarPtr(SelfAddr)", "    SelfAddr = (a = VarPtr(SelfAddr) And a <> 0)", "End Property"));

    private static readonly (string Name, string Source) Globals = ("Globals", StandardModule("Globals",
        "Option Explicit",
        "Public Declare PtrSafe Sub RtlMove Lib \"kernel32\" Alias \"RtlMoveMemory\" (Dest As Any, Src As Any, ByVal n As LongPtr)",
        "Public Type TPair", "    X As Long", "    Y As Long", "End Type",
        "Public Type TOuter", "    Head As Byte", "    Inner As TPair", "    Tail As Double", "End Type",
        "Public Type TFixed", "    B As Byte", "    N(2) As Long", "    After As Integer", "End Type",
        "Public Type TDyn", "    A() As Long", "End Type",
        "Public Type Mixed", "    B As Byte", "    L As Long", "    D As Double", "    I As Integer", "    S As String", "    V As Variant", "    F As String * 3", "    C As Currency", "End Type",
        "Public G As Long",
        "Public Function Stable() As Boolean", "    Dim a As LongPtr, b As LongPtr", "    a = VarPtr(Stable)", "    Stable = True", "    b = VarPtr(Stable)", "    Stable = (a = b)", "End Function",
        "Public Function NotALocal() As Boolean", "    Dim x As Long", "    NotALocal = (VarPtr(NotALocal) <> VarPtr(x))", "End Function",
        "Public Function Echo(ByVal n As Long) As Long", "    Dim a As LongPtr", "    a = VarPtr(Echo)", "    Echo = n + 1", "End Function",
        "Public Function Twice(ByVal n As Long) As Long", "    Twice = n * 2", "End Function",
        "Public Sub ByRefAddr(ByRef x As Long, ByRef addr As LongPtr)", "    addr = VarPtr(x)", "End Sub",
        "Public Sub ByValAddr(ByVal x As Long, ByRef addr As LongPtr)", "    addr = VarPtr(x)", "End Sub",
        "Public Sub Outer(ByRef x As Long, ByRef addr As LongPtr)", "    Inner x, addr", "End Sub",
        "Public Sub Inner(ByRef y As Long, ByRef addr As LongPtr)", "    addr = VarPtr(y)", "End Sub",
        "Public Sub VariantAddr(ByRef v As Variant, ByRef addr As LongPtr)", "    addr = VarPtr(v)", "End Sub",
        "Public Sub VariantTwice(ByRef v As Variant, ByRef same As Boolean)", "    Dim q As LongPtr", "    q = VarPtr(v)", "    same = (q = VarPtr(v))", "End Sub",
        "Public Function StaticAddr() As LongPtr", "    Static z As Long", "    StaticAddr = VarPtr(z)", "End Function"));

    private static string Program(string body)
        => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPrivate acc As String\r\nPrivate Sub P(ByVal v As Variant)\r\n    acc = acc & CStr(v) & \"|\"\r\nEnd Sub\r\n"
            + $"Public Sub Main()\r\nOn Error Resume Next\r\n{string.Join("\r\n", body.Split(" ;; "))}\r\nDebug.Print acc\r\nEnd Sub\r\n";

    private static async Task<string> RunAsync(string body)
        => string.Join(" ", await ModuleWorkspace.RunAsync([Holder], [Globals], Program(body)));

    private static Task<string[]> ErrorsOf(string body) => LoadErrorsAsync([Holder], [Globals], Program(body));

    // V01, R15
    [TestMethod]
    public async Task TheAddressOfAVariable_IsALongLongThatIsNotZero_AndTheSameEveryTime()
        => Assert.AreEqual("True|True|LongLong|", await RunAsync("Dim l As Long ;; P (VarPtr(l) <> 0) ;; P (VarPtr(l) = VarPtr(l)) ;; P TypeName(VarPtr(l))"));

    [TestMethod]
    [DataRow("Dim s As String, q As LongPtr ;; P (VarPtr(s) <> 0) ;; q = VarPtr(s) ;; s = \"abc\" ;; P (VarPtr(s) = q) ;; s = \"a much longer text than before\" ;; P (VarPtr(s) = q)",
        "True|True|True|", DisplayName = "R03 a String, never assigned, then assigned twice")]
    [DataRow("Dim v As Variant, q As LongPtr ;; P (VarPtr(v) <> 0) ;; q = VarPtr(v) ;; v = \"x\" ;; P (VarPtr(v) = q) ;; v = Null ;; P (VarPtr(v) = q) ;; Set v = New Collection ;; P (VarPtr(v) = q)",
        "True|True|True|True|", DisplayName = "R04 a Variant, Empty, then a String, Null, an object")]
    [DataRow("Dim i As Long, q As LongPtr ;; q = VarPtr(i) ;; For i = 1 To 3 ;;     If VarPtr(i) <> q Then P \"moved\" ;; Next ;; P \"same\"",
        "same|", DisplayName = "R11 the counter of a For loop")]
    [DataRow("Dim o As Object, q As LongPtr ;; q = VarPtr(o) ;; Set o = New Collection ;; P (VarPtr(o) = q) ;; Set o = Nothing ;; P (VarPtr(o) = q)",
        "True|True|", DisplayName = "R12 an Object, Nothing, then set, then Nothing again")]
    [DataRow("Dim c As New Collection ;; P (VarPtr(c) <> 0) ;; Dim o As Object ;; P (VarPtr(o) <> 0)", "True|True|", DisplayName = "V24 object variables")]
    [DataRow("Dim a As Long, b As Long ;; P (VarPtr(a) <> VarPtr(b))", "True|", DisplayName = "R13 two variables")]
    [DataRow("Dim l As Long ;; P (VarPtr(Ptr:=l) = VarPtr(l))", "True|", DisplayName = "R14 a named argument")]
    [DataRow("P (VarPtr(G) = VarPtr(Globals.G))", "True|", DisplayName = "R17 a module's variable, bare or qualified")]
    [DataRow("P (StaticAddr() = StaticAddr())", "True|", DisplayName = "V19 a Static local, across calls")]
    [DataRow("Dim l As Long ;; P (VarPtr(l) Mod 4)", "0|", DisplayName = "R33 a Long, aligned")]
    [DataRow("Dim c As Currency, d As Double ;; P (VarPtr(c) Mod 8) ;; P (VarPtr(d) Mod 8)", "0|0|", DisplayName = "R34 a Currency and a Double, aligned")]
    public async Task AVariable_KeepsItsAddress(string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(body));

    // V14, R06, R31, R05, R16: two temporaries of one statement are two addresses.
    [TestMethod]
    [DataRow("P (VarPtr(1) <> 0) ;; P (VarPtr(1) = VarPtr(1)) ;; Dim l As Long ;; P (VarPtr(l + 0) = VarPtr(l)) ;; P (VarPtr((l)) = VarPtr(l))",
        "True|False|False|False|", DisplayName = "V14 a literal, an expression, a parenthesized variable")]
    [DataRow("P (VarPtr(Twice(2)) <> 0) ;; P (VarPtr(Twice(2)) = VarPtr(Twice(2)))", "True|False|", DisplayName = "R06 what a function returns")]
    [DataRow("Dim q As LongPtr ;; q = VarPtr(Twice(2)) ;; P (q <> 0)", "True|", DisplayName = "R31 assigned")]
    [DataRow("Const c As Long = 5 ;; P (VarPtr(c) <> 0)", "True|", DisplayName = "R05 a constant")]
    [DataRow("P (VarPtr(vbNullString) <> 0) ;; P (VarPtr(\"\") <> 0) ;; P (VarPtr(Empty) <> 0) ;; P (VarPtr(Nothing) <> 0)", "True|True|True|True|",
        DisplayName = "R16 vbNullString, an empty string, Empty, Nothing")]
    public async Task WhatIsNoVariable_IsPassedInATemporaryOfTheStatement(string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(body));

    [TestMethod]
    [DataRow("Dim l As Long, addr As LongPtr ;; ByRefAddr l, addr ;; P (addr = VarPtr(l)) ;; ByValAddr l, addr ;; P (addr = VarPtr(l))", "True|False|",
        DisplayName = "V15 ByRef: the caller's variable; ByVal: the parameter's own")]
    [DataRow("Dim l As Long, addr As LongPtr ;; Outer l, addr ;; P (addr = VarPtr(l))", "True|", DisplayName = "R08 ByRef, passed on ByRef")]
    [DataRow("Dim l As Long, addr As LongPtr ;; ByRefAddr l + 1, addr ;; P (addr <> 0)", "True|", DisplayName = "R09 ByRef, given an expression")]
    [DataRow("Dim l As Long, addr As LongPtr ;; VariantAddr l, addr ;; P (addr = VarPtr(l))", "False|",
        DisplayName = "R19 a Variant ByRef parameter given a Long is a VARIANT of its own")]
    [DataRow("Dim l As Long, same As Boolean ;; VariantTwice l, same ;; P same", "True|", DisplayName = "R27 which keeps its address for the call")]
    [DataRow("Dim v As Variant, addr As LongPtr ;; VariantAddr v, addr ;; P (addr = VarPtr(v))", "True|",
        DisplayName = "R20 a Variant ByRef parameter given a Variant is the variable")]
    public async Task AParameter_IsTheVariableItIsASecondNameFor(string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(body));

    // R07, R29: a public variable of an object is a property of its interface, to anything outside the class's own code - Me.Value included.
    [TestMethod]
    [DataRow("Dim h As New Holder ;; P (VarPtr(h.Value) = VarPtr(h.Value)) ;; P (VarPtr(h.Value) = h.Addr) ;; P (h.Addr = h.MeAddr)", "False|False|False|",
        DisplayName = "R07 through the object, and through Me")]
    [DataRow("Dim h As New Holder ;; P h.Same", "True|", DisplayName = "R29 by its bare name, in the class's code")]
    public async Task AFieldOfAnObject_HasAnAddressOnlyInTheCodeOfItsClass(string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(body));

    // V23
    [TestMethod]
    public async Task AnElementOfAnArrayThatHasNone_IsSubscriptOutOfRange()
        => Assert.AreEqual("9|", await RunAsync("Dim a() As Long ;; P VarPtr(a(0)) ;; P Err.Number"));

    // R01, R02, R25: As Any takes a variable of any type but an array, a Declare's as much as VarPtr's.
    [TestMethod]
    [DataRow("Dim a() As Long ;; P (VarPtr(a) <> 0)", DisplayName = "R01 a resizable array")]
    [DataRow("Dim a(2) As Long ;; P (VarPtr(a) <> 0)", DisplayName = "R02 a fixed-size array")]
    [DataRow("Dim a(1) As Long, b As Long ;; RtlMove a, b, 0", DisplayName = "R25 to a Declare's As Any")]
    public async Task AnArrayGivenToAsAny_IsATypeMismatch(string body)
    {
        var errors = await ErrorsOf(body);

        Assert.HasCount(1, errors);
        StringAssert.StartsWith(errors[0], "Type mismatch");
    }

    // R26: an element of one it takes.
    [TestMethod]
    public async Task AnElementOfAnArrayGivenToAsAny_IsValid()
        => Assert.IsEmpty(await ErrorsOf("Dim a(1) As Long, b As Long ;; RtlMove a(0), b, 0"));

    // the elements of an array are side by side, each as wide as its type: in a block of the array's own (oracle\s17 V02-V12, oracle\s24).
    [TestMethod]
    [DataRow("Dim a(3) As Long ;; P VarPtr(a(1)) - VarPtr(a(0)) ;; P VarPtr(a(3)) - VarPtr(a(0))", "4|12|", DisplayName = "V02 Long")]
    [DataRow("Dim a(3) As Byte ;; P VarPtr(a(1)) - VarPtr(a(0))", "1|", DisplayName = "V03 Byte")]
    [DataRow("Dim a(3) As Integer ;; P VarPtr(a(1)) - VarPtr(a(0))", "2|", DisplayName = "V04 Integer")]
    [DataRow("Dim a(3) As Double ;; P VarPtr(a(1)) - VarPtr(a(0))", "8|", DisplayName = "V05 Double")]
    [DataRow("Dim a(3) As String ;; P VarPtr(a(1)) - VarPtr(a(0))", "8|", DisplayName = "V06 String, a pointer")]
    [DataRow("Dim a(3) As Variant ;; P VarPtr(a(1)) - VarPtr(a(0))", "24|", DisplayName = "V07 Variant, a VARIANT")]
    [DataRow("Dim a(3) As Object ;; P VarPtr(a(1)) - VarPtr(a(0))", "8|", DisplayName = "V08 Object, a pointer")]
    [DataRow("Dim a(3) As Currency ;; P VarPtr(a(1)) - VarPtr(a(0))", "8|", DisplayName = "V09 Currency")]
    [DataRow("Dim a(3) As Boolean ;; P VarPtr(a(1)) - VarPtr(a(0))", "2|", DisplayName = "V10 Boolean")]
    [DataRow("Dim m(1, 1) As Long ;; P VarPtr(m(1, 0)) - VarPtr(m(0, 0)) ;; P VarPtr(m(0, 1)) - VarPtr(m(0, 0))", "4|8|", DisplayName = "V12 two dimensions, column-major")]
    [DataRow("Dim a(1) As TPair ;; P VarPtr(a(1)) - VarPtr(a(0))", "8|", DisplayName = "P01 records")]
    [DataRow("Dim a(1) As TPair ;; P VarPtr(a(1).Y) - VarPtr(a(0))", "12|", DisplayName = "P02 a field of an element")]
    [DataRow("Dim r As TDyn ;; ReDim r.A(2) ;; P VarPtr(r.A(1)) - VarPtr(r.A(0))", "4|", DisplayName = "P05 a dynamic array field")]
    [DataRow("Dim v As Variant ;; v = Array(1, 2) ;; P VarPtr(v(1)) - VarPtr(v(0))", "24|", DisplayName = "P06 a Variant holding an array")]
    [DataRow("Dim a(1, 1) As TPair ;; P VarPtr(a(1, 0)) - VarPtr(a(0, 0)) ;; P VarPtr(a(0, 1)) - VarPtr(a(0, 0))", "8|16|", DisplayName = "P07 records in two dimensions")]
    [DataRow("Dim a(3) As Long, q As LongPtr ;; q = VarPtr(a(2)) ;; a(2) = 7 ;; P (VarPtr(a(2)) = q)", "True|", DisplayName = "P08 stays where it is")]
    [DataRow("Dim a(2) As Long ;; P (VarPtr(a(0)) Mod 4)", "0|", DisplayName = "P10 aligned")]
    public async Task TheElementsOfAnArray_AreSideBySide(string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(body));

    // V11: every ReDim moves the data, Preserve or not; P09: an array assigned is a copy, elsewhere.
    [TestMethod]
    [DataRow("Dim a() As Long, a0 As LongPtr ;; ReDim a(3) ;; a0 = VarPtr(a(0)) ;; ReDim a(3) ;; P (VarPtr(a(0)) = a0) ;; a0 = VarPtr(a(0)) ;; ReDim Preserve a(5) ;; P (VarPtr(a(0)) = a0)",
        "False|False|", DisplayName = "V11 ReDim")]
    [DataRow("Dim a() As Long, b() As Long, q As LongPtr ;; ReDim a(2) ;; q = VarPtr(a(0)) ;; b = a ;; P (VarPtr(b(0)) = q) ;; P (VarPtr(a(0)) = q)",
        "False|True|", DisplayName = "P09 assigned")]
    public async Task AnotherArray_IsElsewhere(string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(body));

    // the fields of a record are at their offsets, in MS-VBA's layout for a 64-bit host (oracle\s17 V13, oracle\s21 R10, R30, oracle\s24).
    [TestMethod]
    [DataRow("Dim r As TPair ;; P (VarPtr(r) = VarPtr(r.X)) ;; P (VarPtr(r.Y) - VarPtr(r)) ;; P (VarPtr(r.X) = VarPtr(r.X))", "True|4|True|", DisplayName = "R10 R30")]
    [DataRow("Dim r As Mixed, b As LongPtr ;; b = VarPtr(r) ;; P VarPtr(r.B) - b ;; P VarPtr(r.L) - b ;; P VarPtr(r.D) - b ;; P VarPtr(r.I) - b ;; P VarPtr(r.S) - b ;; P VarPtr(r.V) - b ;; P VarPtr(r.C) - b ;; P LenB(r) ;; P Len(r)",
        "0|4|8|16|24|32|64|72|58|", DisplayName = "V13 every kind of field")]
    [DataRow("Dim r As TOuter ;; P VarPtr(r.Inner) - VarPtr(r) ;; P VarPtr(r.Inner.Y) - VarPtr(r) ;; P VarPtr(r.Tail) - VarPtr(r) ;; P LenB(r)", "4|8|16|24|", DisplayName = "P03 P11 a nested record")]
    [DataRow("Dim r As TFixed ;; P VarPtr(r.N(0)) - VarPtr(r) ;; P VarPtr(r.N(2)) - VarPtr(r) ;; P VarPtr(r.After) - VarPtr(r)", "4|12|16|", DisplayName = "P04 a fixed-size array field, inline")]
    public async Task TheFieldsOfARecord_AreAtTheirOffsets(string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(body));

    // F01-F03, R18: a Function's or a Property Get's name, in its own code, is its result variable, which keeps one address that no local has.
    [TestMethod]
    [DataRow("P Stable()", "True|", DisplayName = "F01 across assignments")]
    [DataRow("P NotALocal()", "True|", DisplayName = "F02 not a local's")]
    [DataRow("Dim h As New Holder ;; P h.SelfAddr", "True|", DisplayName = "F03 a Property Get")]
    [DataRow("P Echo(41)", "42|", DisplayName = "what it is assigned is what it returns")]
    public async Task AFunctionsResultVariable_HasAnAddress(string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(body));

    // V20: a fixed-length String is passed to VarPtr as a String made of it, a temporary; a field of one too.
    [TestMethod]
    public async Task AFixedLengthString_IsATemporary()
        => Assert.AreEqual("False|False|", await RunAsync("Dim f As String * 4, r As Mixed ;; P (VarPtr(f) = VarPtr(f)) ;; P (VarPtr(r.F) = VarPtr(r.F))"));
}
