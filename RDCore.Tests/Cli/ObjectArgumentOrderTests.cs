using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// The order the arguments of a call are evaluated in, as <strong>MS-VBA</strong> evaluates them (observed in Excel, 64-bit): MS-VBAL §5.3.1.11 binds the
/// parameters left to right and does not say when an argument's expression is evaluated. But for the last argument, the arguments that are calls are
/// evaluated first, left to right, with the calls inside the others; then, in a call statement, the calls inside the arguments of the target's member calls;
/// then every other argument, right to left, the last one first; the rest of the target of a member call last. The objects the arguments gave are let
/// go of when the statement is over, in the order of the parameters, then the target. A call is a procedure or a member of an object; a function the
/// standard library evaluates inline (<c>Len</c>, <c>Abs</c>, the conversions) is none. A class's <c>Class_Initialize</c> and <c>Class_Terminate</c>
/// (MS-VBAL §5.3.1.10) and a function that makes an object write to <c>Trail</c>, which shows the order.
/// </summary>
[TestClass]
public sealed class ObjectArgumentOrderTests
{
    internal static readonly (string Name, string Source) Box = ("Box", ClassModule("Box",
        "Public Id As Long", "Public Value As Long",
        "Private Sub Class_Initialize()", "    Made = Made + 1", "    Id = Made", "    Trail = Trail & \"i\" & Id & \" \"", "End Sub",
        "Private Sub Class_Terminate()", "    Trail = Trail & \"t\" & Id & \" \"", "End Sub",
        "Public Function Addr() As LongPtr", "    Addr = VarPtr(Value)", "End Function",
        "Public Sub Touch()", "    Value = Value + 1", "End Sub",
        "Public Function Pair(ByVal A As Box, ByVal B As Box) As String", "    Pair = Id & \":\" & A.Id & \",\" & B.Id & \":\" & Trail", "End Function",
        "Public Sub Take(ByVal A As Box, ByVal B As Box)", "    Trail = Trail & \"take\" & A.Id & B.Id & \" \"", "End Sub",
        "Public Property Get Twice() As Long", "    Twice = Value * 2", "End Property",
        "Public Function Stable() As Boolean", "    Dim First As LongPtr, I As Long", "    First = VarPtr(Value)",
        "    For I = 1 To 3", "        Touch", "    Next", "    Stable = (VarPtr(Value) = First)", "End Function"));

    internal static readonly (string Name, string Source) Log = ("Log", StandardModule("Log",
        "Public Trail As String", "Public Made As Long", "Public Counter As Long", "Public Type Rec", "    F As Long", "End Type",
        "Public Function Tagged(ByVal Tag As String) As Box", "    Trail = Trail & Tag & \" \"", "    Set Tagged = New Box", "End Function",
        "Public Function Pair(ByVal A As Box, ByVal B As Box) As String", "    Pair = A.Id & \",\" & B.Id & \":\" & Trail", "End Function",
        "Public Function Three(ByVal A As Box, ByVal B As Box, ByVal C As Box) As String",
        "    Three = A.Id & \",\" & B.Id & \",\" & C.Id & \":\" & Trail", "End Function",
        "Public Sub Both(ByVal A As Box, ByVal B As Box)", "    Trail = Trail & \"both\" & A.Id & B.Id & \" \"", "End Sub",
        "Public Sub Both2(ByVal A As Long, ByVal B As Long)", "    Trail = Trail & A & \",\" & B", "End Sub",
        "Public Function Cleared(ByRef X As Box) As Long", "    Set X = Nothing", "    Cleared = 1", "End Function",
        "Public Function IdOf(ByVal X As Box) As Long", "    If Not X Is Nothing Then IdOf = X.Id", "End Function",
        "Public Function Sum2(ByVal A As Long, ByVal B As Long) As String", "    Sum2 = A & \",\" & B", "End Function",
        "Public Function Pick(ByVal A As Box, ByVal B As Box) As Box", "    Set Pick = A", "End Function",
        "Public Function AddrOf(ByVal X As Box) As LongPtr", "    AddrOf = X.Addr", "End Function",
        "Public Function AddrRef(ByRef X As Box) As LongPtr", "    AddrRef = X.Addr", "End Function",
        "Public Function Bump(ByRef N As Long) As Long", "    N = N + 10", "    Bump = N", "End Function",
        "Public Function Swapped(ByRef X As Box) As Box", "    Set Swapped = X", "    Set X = New Box", "End Function",
        "Public Function Swapped2(ByRef X As Variant) As Box", "    Set Swapped2 = X", "    Set X = New Box", "End Function",
        "Public Function SwappedO(ByRef X As Object) As Box", "    Set SwappedO = X", "    Set X = New Box", "End Function",
        "Public Function One(ByVal A As Long) As String", "    One = CStr(A)", "End Function",
        "Public Function Pre(ByRef S As String) As String", "    S = \"x\" & S", "    Pre = S", "End Function",
        "Public Function Grow(ByRef S As String) As Long", "    S = S & \"xx\"", "    Grow = Len(S)", "End Function",
        "Public Function Grown(ByRef S As String) As String", "    S = S & \"xx\"", "    Grown = S", "End Function",
        "Public Function Join2(ByVal A As String, ByVal B As String) As String", "    Join2 = A & \",\" & B", "End Function",
        "Public Function Sum3(ByVal A As Long, ByVal B As Long, ByVal C As Long) As String", "    Sum3 = A & \",\" & B & \",\" & C", "End Function",
        "Public Function SumR(ByRef A As Long, ByVal B As Long) As String", "    SumR = A & \",\" & B", "End Function",
        "Public Function Opt(ByVal A As Long, Optional ByVal B As Long = 5, Optional ByVal C As Long = 7) As String",
        "    Opt = A & \",\" & B & \",\" & C", "End Function",
        "Public Function Many(ParamArray V() As Variant) As String", "    Dim K As Long", "    For K = LBound(V) To UBound(V)",
        "        Many = Many & V(K) & \",\"", "    Next", "End Function",
        "Public Function BumpI(ByRef I As Long) As Long", "    I = I + 1", "    BumpI = I", "End Function",
        "Public Function BumpV(ByRef V As Variant) As Long", "    V = V + 10", "    BumpV = V", "End Function",
        "Public Function BumpBox(ByRef B As Box) As Long", "    B.Value = B.Value + 10", "    BumpBox = B.Value", "End Function",
        "Public Function BumpArr(ByRef A() As Long) As Long", "    A(1) = A(1) + 10", "    BumpArr = A(1)", "End Function",
        "Public Function BumpRec(ByRef R As Rec) As Long", "    R.F = R.F + 10", "    BumpRec = R.F", "End Function",
        "Public Function ReDimmer(ByRef A() As Long) As Long", "    ReDim A(1 To 5)", "    ReDimmer = 5", "End Function",
        "Public Function Bumped() As Long", "    Counter = Counter + 10", "    Bumped = Counter", "End Function",
        "Public Property Get CounterP() As Long", "    CounterP = Counter", "End Property",
        "Public Function IdS(ByVal X As Box) As String", "    IdS = CStr(X.Id)", "End Function",
        "Public Function Echo(ByVal S As String) As String", "    Echo = S", "End Function",
        "Public Function Fails() As Long", "    Err.Raise 5", "End Function"));

    // the body runs in a procedure of its own, which lets go of its locals when it returns: Main prints what it collected, then the trail its end left.
    internal static async Task<string> RunAsync(string body)
        => string.Join(" ", await ModuleWorkspace.RunAsync([Box], [Log], "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPrivate acc As String\r\n"
            + "Private Sub P(ByVal x As Variant)\r\n    acc = acc & CStr(x) & \"|\"\r\nEnd Sub\r\n"
            + $"Private Sub Body()\r\n{string.Join("\r\n", body.Split(" ;; "))}\r\nEnd Sub\r\n"
            + "Public Sub Main()\r\n    Body\r\n    Debug.Print acc\r\nEnd Sub\r\n"));

    [TestMethod]
    [TestCategory("MS-VBAL 5.3.1.11 Procedure Invocation Argument Processing")]
    [DataRow("Dim X As Box ;; Set X = New Box ;; P Sum2(Cleared(X), IdOf(X)) ;; P Trail", "1,0|i1 t1 |",
        DisplayName = "a call that lets go of a variable by reference, left of a call that reads it")]
    [DataRow("Dim X As Box ;; Set X = New Box ;; P Sum2(IdOf(X), Cleared(X)) ;; P Trail", "1,1|i1 t1 |",
        DisplayName = "a call that reads a variable, left of a call that lets go of it by reference")]
    [DataRow("Dim X As Box ;; Set X = New Box ;; P Pair(Swapped(X), X) ;; P Trail", "1,2:i1 i2 |i1 i2 t1 |",
        DisplayName = "a call that gives a variable another object by reference, left of the variable")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(Bump(N), N)", "11,11|", DisplayName = "a call that adds to a variable by reference, left of the variable")]
    [DataRow("Dim X As Box ;; Set X = Tagged(\"x\") ;; Set X = Pick(Tagged(\"a\"), X) ;; P X.Id ;; P Trail", "2|x i1 a i2 t1 |",
        DisplayName = "a Set of what a call returns, given the variable it replaces")]
    public async Task ACallAmongTheArguments_IsEvaluatedBeforeTheArgumentsRightOfIt(string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(body));

    [TestMethod]
    [TestCategory("MS-VBAL 5.3.1.11 Procedure Invocation Argument Processing")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(N, Bump(N))", "11,11|", DisplayName = "a variable, left of a call that adds to it by reference")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(N + 0, Bump(N))", "11,11|", DisplayName = "an expression of a variable, left of a call that adds to it")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2((N), Bump(N))", "11,11|", DisplayName = "a variable in parentheses, left of a call that adds to it")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(CLng(N), Bump(N))", "11,11|", DisplayName = "a conversion of a variable, left of a call that adds to it")]
    [DataRow("Dim N As Long ;; N = 1 ;; Both2 N, Bump(N) ;; P Trail", "11,11|", DisplayName = "a variable, left of a call, in a call statement")]
    [DataRow("Dim X As Box ;; Set X = New Box ;; P Pair(X, Swapped(X)) ;; P Trail", "2,1:i1 i2 |i1 i2 t1 |",
        DisplayName = "an object variable, left of a call that gives it another object by reference")]
    [DataRow("Dim V As Variant ;; Set V = New Box ;; P Pair(V, Swapped2(V)) ;; P Trail", "2,1:i1 i2 |i1 i2 t1 |",
        DisplayName = "a Variant, left of a call that gives it another object by reference")]
    [DataRow("P Pair(New Box, New Box) ;; P Trail", "2,1:i1 i2 |i1 i2 t2 t1 |", DisplayName = "two New arguments")]
    [DataRow("P Three(New Box, New Box, New Box) ;; P Trail", "3,2,1:i1 i2 i3 |i1 i2 i3 t3 t2 t1 |", DisplayName = "three New arguments")]
    [DataRow("P Three(New Box, Tagged(\"b\"), New Box) ;; P Trail", "3,1,2:b i1 i2 i3 |b i1 i2 i3 t3 t1 t2 |",
        DisplayName = "a call between two New arguments")]
    [DataRow("P Pair(New Box, Tagged(\"b\")) ;; P Trail", "2,1:b i1 i2 |b i1 i2 t2 t1 |", DisplayName = "a New argument, left of a call that makes an object")]
    [DataRow("Both New Box, New Box ;; P Trail", "i1 i2 both21 t2 t1 |", DisplayName = "two New arguments, in a call statement")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(-N, Bump(N))", "-11,11|", DisplayName = "a negation of a variable, left of a call that adds to it")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(Bump(N), Bump(N))", "11,21|", DisplayName = "two calls that add to a variable")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum3(N, Bump(N), N * 2)", "11,11,22|", DisplayName = "expressions of a variable, either side of a call")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Opt(N, Bump(N))", "11,11,7|", DisplayName = "a variable, left of a call, an Optional argument left out")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Many(N, Bump(N), N)", "11,11,11,|", DisplayName = "the arguments a ParamArray collects")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(B:=Bump(N), A:=N)", "11,11|", DisplayName = "named arguments, the call written first")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(B:=N, A:=Bump(N))", "11,11|", DisplayName = "named arguments, the call written last")]
    [DataRow("Dim N As Long ;; N = 1 ;; Call Both2(N, Bump(N)) ;; P Trail", "11,11|", DisplayName = "a variable, left of a call, in a Call statement")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(N, Sum2(N, Bump(N)))", "11,1111|", DisplayName = "a variable, left of a call that makes a call")]
    [DataRow("Dim S As String ;; S = \"a\" ;; P Join2(S, Grown(S))", "axx,axx|", DisplayName = "a String variable, left of a call that adds to it")]
    [DataRow("Dim S As String ;; S = \"a\" ;; P Join2(S & \"\", Grown(S))", "axx,axx|", DisplayName = "a concatenation, left of a call that adds to it")]
    [DataRow("Dim V As Variant ;; V = 1 ;; P Sum2(V, BumpV(V))", "11,11|", DisplayName = "a Variant variable, left of a call that adds to it")]
    [DataRow("Dim A(1 To 2) As Long ;; A(1) = 1 ;; P Sum2(A(1), BumpArr(A))", "11,11|", DisplayName = "an element of an array, left of a call")]
    [DataRow("Dim R As Rec ;; R.F = 1 ;; P Sum2(R.F, BumpRec(R))", "11,11|", DisplayName = "a field of a record, left of a call")]
    [DataRow("Counter = 1 ;; P Sum2(Counter, Bumped)", "11,11|", DisplayName = "a public variable of a module, left of a call that adds to it")]
    [DataRow("Dim A(1 To 3) As Long, I As Long ;; A(1) = 1 ;; A(2) = 2 ;; I = 1 ;; P SumR(A(I), BumpI(I))", "2,2|",
        DisplayName = "an element given by reference, its index left of a call that adds to it")]
    public async Task TheArgumentsThatAreNoCall_AreEvaluatedAfterTheCalls_RightToLeft(string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(body));

    [TestMethod]
    [TestCategory("MS-VBAL 5.3.1.11 Procedure Invocation Argument Processing")]
    [DataRow("Dim B As New Box ;; B.Value = 1 ;; P Sum2(B.Value, BumpBox(B))", "1,11|", DisplayName = "a public variable of an object")]
    [DataRow("Dim B As New Box ;; B.Value = 1 ;; P Sum2(B.Twice, BumpBox(B))", "2,11|", DisplayName = "a Property Get of an object")]
    [DataRow("Dim X As New Box ;; X.Value = 1 ;; With X ;; P Sum2(.Value, BumpBox(X)) ;; End With", "1,11|",
        DisplayName = "a public variable of the object of a With block")]
    [DataRow("Counter = 1 ;; P Sum2(CounterP, Bumped)", "1,11|", DisplayName = "a Property Get of a module")]
    public async Task AMemberOfAnObject_OrAPropertyOfAModule_IsACall(string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(body));

    [TestMethod]
    [TestCategory("MS-VBAL 5.3.1.11 Procedure Invocation Argument Processing")]
    [DataRow("Dim S As String ;; S = \"a\" ;; P Join2(UCase$(S), Grown(S))", "A,axx|", DisplayName = "UCase$")]
    [DataRow("Dim S As String ;; S = \"a\" ;; P Join2(UCase(S), Pre(S))", "A,xa|", DisplayName = "UCase")]
    [DataRow("Dim S As String ;; S = \"a\" ;; P Join2(Mid$(S, 1), Grown(S))", "a,axx|", DisplayName = "Mid$")]
    [DataRow("Dim S As String ;; S = \"a\" ;; P Join2(Mid(S, 1), Pre(S))", "a,xa|", DisplayName = "Mid")]
    [DataRow("Dim S As String ;; S = \"a\" ;; P Join2(Replace(S, \"z\", \"y\"), Grown(S))", "a,axx|", DisplayName = "Replace")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(IIf(True, N, 0), Bump(N))", "1,11|", DisplayName = "IIf")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(Val(CStr(N)), Bump(N))", "1,11|", DisplayName = "Val")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(Sqr(N), Bump(N))", "1,11|", DisplayName = "Sqr")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(Round(N), Bump(N))", "1,11|", DisplayName = "Round")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Join2(Format$(N), CStr(Bump(N)))", "1,11|", DisplayName = "Format$")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Join2(Hex$(N), CStr(Bump(N)))", "1,11|", DisplayName = "Hex$")]
    [DataRow("Dim N As Long ;; N = 1 ;; P \"[\" ;; P Join2(Str$(N), CStr(Bump(N)))", "[| 1,11|", DisplayName = "Str$")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Join2(Chr$(N + 64), CStr(Bump(N)))", "A,11|", DisplayName = "Chr$")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Join2(ChrW$(N + 64), CStr(Bump(N)))", "A,11|", DisplayName = "ChrW$")]
    [DataRow("Dim S As String ;; S = \"a\" ;; P Join2(Left$(S, 1), Pre(S))", "a,xa|", DisplayName = "Left$")]
    [DataRow("Dim S As String ;; S = \"a\" ;; P Join2(Right$(S, 2), Pre(S))", "a,xa|", DisplayName = "Right$")]
    [DataRow("Dim S As String ;; S = \"a\" ;; P Join2(Trim$(S), Pre(S))", "a,xa|", DisplayName = "Trim$")]
    [DataRow("Dim S As String ;; S = \"a\" ;; P Join2(LCase$(S), Pre(S))", "a,xa|", DisplayName = "LCase$")]
    [DataRow("Dim S As String ;; S = \"a\" ;; P Sum2(Asc(S), Len(Pre(S)))", "97,2|", DisplayName = "Asc")]
    [DataRow("Dim S As String ;; S = \"a\" ;; P Sum2(AscW(S), Len(Pre(S)))", "97,2|", DisplayName = "AscW")]
    [DataRow("Dim V As Variant ;; P Sum2(IsEmpty(V), BumpV(V))", "-1,10|", DisplayName = "IsEmpty")]
    [DataRow("Dim V As Variant ;; P Sum2(VarType(V), BumpV(V))", "0,10|", DisplayName = "VarType")]
    [DataRow("Dim V As Variant ;; P Join2(TypeName(V), CStr(BumpV(V)))", "Empty,10|", DisplayName = "TypeName")]
    public async Task AFunctionOfTheStandardLibrary_IsACall(string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(body));

    [TestMethod]
    [TestCategory("MS-VBAL 5.3.1.11 Procedure Invocation Argument Processing")]
    [DataRow("Dim S As String ;; S = \"a\" ;; P Sum2(Len(S), Grow(S))", "3,3|", DisplayName = "Len")]
    [DataRow("Dim S As String ;; S = \"a\" ;; P Sum2(LenB(S), Len(Pre(S)))", "4,2|", DisplayName = "LenB")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(Abs(N), Bump(N))", "11,11|", DisplayName = "Abs")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(Int(N), Bump(N))", "11,11|", DisplayName = "Int")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(Int(N) + 0, Bump(N))", "11,11|", DisplayName = "Int, in an expression")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(Fix(N), Bump(N))", "11,11|", DisplayName = "Fix")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(Sgn(N - 5), Bump(N))", "1,11|", DisplayName = "Sgn")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(CInt(N), Bump(N))", "11,11|", DisplayName = "CInt")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(CDbl(N), Bump(N))", "11,11|", DisplayName = "CDbl")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(CVar(N), Bump(N))", "11,11|", DisplayName = "CVar")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(CCur(N), Bump(N))", "11,11|", DisplayName = "CCur")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(CByte(N), Bump(N))", "11,11|", DisplayName = "CByte")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(CBool(N - 1), Bump(N))", "-1,11|", DisplayName = "CBool")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(CDate(N), Bump(N))", "11,11|", DisplayName = "CDate")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Join2(CStr(N), CStr(Bump(N)))", "11,11|", DisplayName = "CStr")]
    [DataRow("Dim S As String ;; S = \"a\" ;; P Sum2(InStr(S, \"x\"), Len(Pre(S)))", "1,2|", DisplayName = "InStr")]
    [DataRow("Dim S As String ;; S = \"a\" ;; P Sum2(StrComp(S, \"a\"), Len(Pre(S)))", "1,2|", DisplayName = "StrComp")]
    [DataRow("Dim A() As Long ;; ReDim A(1 To 2) ;; P Sum2(UBound(A), ReDimmer(A))", "5,5|", DisplayName = "UBound")]
    [DataRow("Dim A() As Long ;; ReDim A(2 To 3) ;; P Sum2(LBound(A), ReDimmer(A))", "1,5|", DisplayName = "LBound")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(Len(Space$(N)), Bump(N))", "1,11|", DisplayName = "Len of a call, made with the calls")]
    public async Task AFunctionTheStandardLibraryEvaluatesInline_IsNoCall(string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(body));

    [TestMethod]
    [TestCategory("MS-VBAL 5.3.1.11 Procedure Invocation Argument Processing")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(N + Bump(N), N)", "22,11|", DisplayName = "an expression with a call, left of a variable")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(N, N + Bump(N))", "11,12|", DisplayName = "an expression with a call, the last argument")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(N + Bump(N), 0)", "22,0|", DisplayName = "an expression with a call, left of a constant")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(0, N + Bump(N))", "0,12|", DisplayName = "an expression with a call, right of a constant")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(Bump(N) + N, N)", "22,11|", DisplayName = "an expression that begins with a call, left of a variable")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(N, Bump(N) + N)", "11,22|", DisplayName = "an expression that begins with a call, the last argument")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum3(N + Bump(N), N, 0)", "22,11,0|", DisplayName = "an expression with a call, the first of three")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum3(0, N + Bump(N), N)", "0,22,11|", DisplayName = "an expression with a call, the second of three")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum3(N, N + Bump(N), 0)", "11,22,0|", DisplayName = "an expression with a call, right of a variable")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(N + Bump(N), Bump(N))", "32,21|", DisplayName = "an expression with a call, left of a call")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(Bump(N), N + Bump(N))", "11,32|", DisplayName = "an expression with a call, right of a call")]
    [DataRow("Dim N As Long ;; N = 1 ;; P Sum2(N + One(Bump(N)), N)", "22,11|", DisplayName = "an expression with a call that makes a call")]
    [DataRow("Dim S As String ;; S = \"a\" ;; P Join2(S & Grown(S), S)", "axxaxx,axx|", DisplayName = "a concatenation with a call")]
    [DataRow("Dim N As Long ;; N = 1 ;; P One(N + Bump(N))", "12|", DisplayName = "an expression with a call, the only argument")]
    [DataRow("Dim N As Long ;; N = 1 ;; P N + Bump(N)", "12|", DisplayName = "an expression with a call, given to a call statement")]
    public async Task TheCallsInsideAnArgumentThatIsNoCall_AreMadeWithTheCalls_ButInTheLastArgument(string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(body));

    [TestMethod]
    [TestCategory("MS-VBAL 5.3.1.11 Procedure Invocation Argument Processing")]
    [DataRow("Dim N As Long, Z As Long ;; N = 1 ;; On Error Resume Next ;; P Sum2(1 \\ Z, Bump(N)) ;; P Err.Number ;; P N", "11|11|",
        DisplayName = "a division by zero, left of a call")]
    [DataRow("Dim N As Long ;; N = 1 ;; On Error Resume Next ;; P Sum2(Fails(), Bump(N)) ;; P Err.Number ;; P N", "5|1|",
        DisplayName = "a call that raises an error, left of a call")]
    [DataRow("Dim N As Long ;; N = 1 ;; On Error Resume Next ;; P Sum2(Bump(N), Fails()) ;; P Err.Number ;; P N", "5|11|",
        DisplayName = "a call that raises an error, right of a call")]
    [DataRow("Dim B As Box ;; On Error Resume Next ;; P Pair(B, Tagged(\"a\")) ;; P Err.Number ;; P Trail", "91|a i1 t1 |",
        DisplayName = "an object variable that is Nothing, left of a call")]
    [DataRow("Dim N As Long, Z As Long ;; N = 1 ;; On Error Resume Next ;; Call Sum2(Bump(N), 1 \\ Z) ;; P N ;; P Bump(N)", "11|21|",
        DisplayName = "a division by zero, after a call that changed a variable by reference")]
    [DataRow("Dim N As Long ;; N = 1 ;; On Error GoTo H ;; P Bump(N) + Fails() ;; H: ;; P N", "11|",
        DisplayName = "a call that raises an error, after a call that changed a variable by reference, under a handler")]
    [DataRow("Dim N As Long, Z As Long, X As Long ;; N = 1 ;; On Error Resume Next ;; X = Bump(N) + 1 \\ Z ;; P N", "11|",
        DisplayName = "a division by zero, after a call that changed a variable by reference, in an assignment")]
    public async Task AnErrorAnArgumentRaises_IsRaisedWhereItIsEvaluated(string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(body));

    [TestMethod]
    [TestCategory("MS-VBAL 5.3.1.11 Procedure Invocation Argument Processing")]
    [DataRow("P Tagged(\"t\").Pair(Tagged(\"a\"), Tagged(\"b\")) ;; P Trail", "3:1,2:a i1 b i2 t i3 |a i1 b i2 t i3 t1 t2 t3 |",
        DisplayName = "a target a call returns, after the calls among the arguments")]
    [DataRow("Dim X As Box ;; Set X = New Box ;; P X.Pair(New Box, Tagged(\"b\")) ;; P Trail", "1:3,2:i1 b i2 i3 |i1 b i2 i3 t3 t2 |",
        DisplayName = "a target variable, with a New argument left of a call")]
    [DataRow("Dim X As Box ;; Set X = New Box ;; P X.Pair(Swapped(X), New Box) ;; P Trail", "2:1,3:i1 i2 i3 |i1 i2 i3 t1 t3 |",
        DisplayName = "a target variable a call among the arguments gives another object")]
    [DataRow("Dim O As Object ;; Set O = New Box ;; P O.Pair(SwappedO(O), New Box) ;; P Trail", "2:1,3:i1 i2 i3 |i1 i2 i3 t1 t3 |",
        DisplayName = "a target reached late-bound, given another object by a call among the arguments")]
    [DataRow("Dim X As Box ;; Set X = New Box ;; P X.Pair(Swapped(X), Swapped(X)) ;; P Trail", "3:1,2:i1 i2 i3 |i1 i2 i3 t1 t2 |",
        DisplayName = "a target variable two calls among the arguments give another object")]
    [DataRow("P Tagged(\"t\").Pair(Tagged(\"a\"), New Box) ;; P Trail", "3:1,2:a i1 i2 t i3 |a i1 i2 t i3 t1 t2 t3 |",
        DisplayName = "a target a call returns, after a New argument")]
    [DataRow("P Pick(Tagged(\"a\"), Tagged(\"b\")).Pair(Tagged(\"c\"), Tagged(\"d\")) ;; P Trail",
        "3:1,2:c i1 d i2 a i3 b i4 |c i1 d i2 a i3 b i4 t4 t1 t2 t3 |", DisplayName = "a target a call with arguments returns")]
    [DataRow("With Tagged(\"w\") ;; P .Pair(Tagged(\"a\"), Tagged(\"b\")) ;; End With ;; P Trail", "1:2,3:w i1 a i2 b i3 |w i1 a i2 b i3 t2 t3 t1 |",
        DisplayName = "the object of a With block, evaluated with the block")]
    [DataRow("Dim X As Box ;; Set X = New Box ;; X.Take Swapped(X), New Box ;; P Trail", "i1 i2 i3 take13 t1 t3 |",
        DisplayName = "a target variable, in a call statement")]
    [DataRow("Dim X As Box ;; On Error Resume Next ;; P X.Pair(Tagged(\"a\"), Tagged(\"b\")) ;; P Err.Number ;; P Trail", "91|a i1 b i2 t1 t2 |",
        DisplayName = "a target that is Nothing, after its arguments")]
    [DataRow("Dim X As Box ;; On Error Resume Next ;; X.Take Tagged(\"a\"), Tagged(\"b\") ;; P Err.Number ;; P Trail", "91|a i1 b i2 t1 t2 |",
        DisplayName = "a target that is Nothing, after its arguments, in a call statement")]
    public async Task TheTargetOfAMemberCall_IsEvaluatedAfterItsArguments(string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(body));

    internal static readonly (string Name, string Source) Node = ("Node", ClassModule("Node",
        "Private mKids(1 To 3) As Node",
        "Public Property Get Kid(ByVal K As Long) As Node", "    Set Kid = mKids(K)", "End Property",
        "Public Property Set Kid(ByVal K As Long, ByVal V As Node)", "    Set mKids(K) = V", "End Property",
        "Public Property Get KidL(ByVal K As Long) As Node", "    Trail = Trail & \"kid(\" & K & \") \"", "    Set KidL = mKids(K)", "End Property",
        "Public Property Get KidS(ByVal K As Long) As Node", "    Trail = Trail & \"kid(\" & K & \") \"", "    Set KidS = Me", "End Property",
        "Public Function KidF(ByVal K As Long) As Node", "    Trail = Trail & \"kidf(\" & K & \") \"", "    Set KidF = Me", "End Function",
        "Public Property Get Me1() As Node", "    Trail = Trail & \"me \"", "    Set Me1 = Me", "End Property",
        "Public Sub Take1(ByVal A As Long)", "    Trail = Trail & \"take(\" & A & \") \"", "End Sub",
        "Public Sub Take2(ByVal A As Long, ByVal B As Long)", "    Trail = Trail & \"take(\" & A & \",\" & B & \") \"", "End Sub",
        "Public Sub Take3(ByVal A As Long, ByVal B As Long, ByVal C As Long)", "    Trail = Trail & \"take(\" & A & \",\" & B & \",\" & C & \") \"", "End Sub",
        "Public Function Sum2F(ByVal A As Long, ByVal B As Long) As String", "    Trail = Trail & \"sum \"", "    Sum2F = A & \",\" & B", "End Function"));

    internal static readonly (string Name, string Source) Steps = ("Steps", StandardModule("Steps",
        "Public Trail As String",
        "Public Function Ix(ByVal Tag As String, ByVal I As Long) As Long", "    Trail = Trail & Tag & \" \"", "    Ix = I", "End Function",
        "Public Function Vl(ByVal Tag As String, ByVal V As Long) As Long", "    Trail = Trail & Tag & \" \"", "    Vl = V", "End Function",
        "Public Function Tg(ByVal Tag As String) As Node", "    Trail = Trail & Tag & \" \"", "    Set Tg = New Node", "End Function",
        "Public Function Tg2(ByVal N As Long) As Node", "    Trail = Trail & \"t\" & N & \" \"", "    Set Tg2 = New Node", "End Function",
        "Public Function BumpI(ByRef I As Long) As Long", "    I = I + 1", "    BumpI = I", "End Function",
        "Public Function Fails(ByVal Tag As String) As Long", "    Trail = Trail & Tag & \" \"", "    Err.Raise 5", "End Function"));

    // as RunAsync, with a class whose members write what they are called with to Trail, and makes nothing that writes to it.
    internal static async Task<string> RunTargetAsync(string body)
        => string.Join(" ", await ModuleWorkspace.RunAsync([Node], [Steps], "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPrivate acc As String\r\n"
            + "Private Sub P(ByVal x As Variant)\r\n    acc = acc & CStr(x) & \"|\"\r\nEnd Sub\r\n"
            + $"Private Sub Body()\r\n{string.Join("\r\n", body.Split(" ;; "))}\r\nEnd Sub\r\n"
            + "Public Sub Main()\r\n    Body\r\n    Debug.Print acc\r\nEnd Sub\r\n"));

    private const string AnObject = "Dim B As Node ;; Set B = New Node ;; Trail = \"\" ;; ";

    [TestMethod]
    [TestCategory("MS-VBAL 5.3.1.11 Procedure Invocation Argument Processing")]
    [DataRow("Dim B As Node ;; Set B = New Node ;; Set B.Kid(1) = New Node ;; B.KidL(Ix(\"i\", 1)).Take2 Ix(\"j\", 2), Vl(\"v\", 5) ;; P Trail",
        "j i v kid(1) take(2,5) |", DisplayName = "a Property Get of a variable")]
    [DataRow("Dim B As New Node ;; Set B.Kid(1) = New Node ;; B.KidL(Ix(\"i\", 1)).Take2 Ix(\"j\", 2), Vl(\"v\", 5) ;; P Trail",
        "j i v kid(1) take(2,5) |", DisplayName = "a Property Get of a variable declared As New")]
    [DataRow(AnObject + "B.KidS(Ix(\"i\", 1)).Take1 Vl(\"v\", 5) ;; P Trail", "i v kid(1) take(5) |", DisplayName = "one argument")]
    [DataRow(AnObject + "B.KidS(Ix(\"i\", 1)).Take3 Ix(\"j\", 2), Ix(\"k\", 3), Vl(\"v\", 5) ;; P Trail", "j k i v kid(1) take(2,3,5) |",
        DisplayName = "three arguments")]
    [DataRow(AnObject + "B.KidS(Ix(\"i\", 1)).Take2 Ix(\"j\", 2) + 0, Vl(\"v\", 5) ;; P Trail", "j i v kid(1) take(2,5) |",
        DisplayName = "an argument that makes a call, left of the last")]
    [DataRow(AnObject + "B.KidS(Ix(\"i\", 1)).Take2 Ix(\"j\", 2), Vl(\"v\", 5) + 0 ;; P Trail", "j i v kid(1) take(2,5) |",
        DisplayName = "a last argument that makes a call")]
    [DataRow(AnObject + "B.KidS(Ix(\"i\", 1)).Take2 Ix(\"j\", 2), Ix(\"k\", 3) + Vl(\"v\", 5) ;; P Trail", "j i k v kid(1) take(2,8) |",
        DisplayName = "a last argument that makes two calls")]
    [DataRow(AnObject + "B.KidS(Ix(\"i\", 1)).Take2 Vl(\"v\", 5), Ix(\"j\", 2) ;; P Trail", "v i j kid(1) take(5,2) |",
        DisplayName = "the arguments the other way round")]
    [DataRow(AnObject + "Dim I As Long ;; I = 1 ;; B.KidS(I).Take2 7, BumpI(I) ;; P Trail", "kid(2) take(7,2) |",
        DisplayName = "a variable argument of the target, read after the last argument")]
    [DataRow(AnObject + "Dim I As Long ;; I = 1 ;; B.KidS(I + 0).Take2 BumpI(I), 7 ;; P Trail", "kid(2) take(2,7) |",
        DisplayName = "an argument of the target that makes no call, evaluated after the calls")]
    [DataRow(AnObject + "B.KidF(Ix(\"i\", 1)).Take2 Ix(\"j\", 2), Vl(\"v\", 5) ;; P Trail", "j i v kidf(1) take(2,5) |",
        DisplayName = "a Function of a variable")]
    [DataRow(AnObject + "B.Me1.KidS(Ix(\"i\", 1)).Take2 Ix(\"j\", 2), Vl(\"v\", 5) ;; P Trail", "j i v me kid(1) take(2,5) |",
        DisplayName = "a Property Get of a Property Get")]
    [DataRow("Tg(\"t\").KidS(Ix(\"i\", 1)).Take2 Ix(\"j\", 2), Vl(\"v\", 5) ;; P Trail", "j i v t kid(1) take(2,5) |",
        DisplayName = "a Property Get of what a function returns")]
    [DataRow(AnObject + "B.KidS(Ix(\"i\", 1)).KidS(Ix(\"m\", 2)).Take2 Ix(\"j\", 3), Vl(\"v\", 5) ;; P Trail", "j m i v kid(1) kid(2) take(3,5) |",
        DisplayName = "a Property Get of a Property Get, both given a call")]
    [DataRow("Dim A(1 To 2) As Node ;; Set A(1) = New Node ;; A(Ix(\"i\", 1)).KidS(Ix(\"m\", 2)).Take2 Ix(\"j\", 3), Vl(\"v\", 5) ;; P Trail",
        "j m v i kid(2) take(3,5) |", DisplayName = "a Property Get of an element of an array")]
    [DataRow(AnObject + "With B ;; .KidS(Ix(\"i\", 1)).Take2 Ix(\"j\", 2), Vl(\"v\", 5) ;; End With ;; P Trail", "j i v kid(1) take(2,5) |",
        DisplayName = "a Property Get of the object of a With block")]
    [DataRow(AnObject + "Call B.KidS(Ix(\"i\", 1)).Take2(Ix(\"j\", 2), Vl(\"v\", 5)) ;; P Trail", "j i v kid(1) take(2,5) |",
        DisplayName = "a Call statement")]
    [DataRow(AnObject + "On Error Resume Next ;; B.KidS(Fails(\"f\")).Take2 Ix(\"j\", 2), Vl(\"v\", 5) ;; P Err.Number ;; P Trail", "5|j f |",
        DisplayName = "an argument of the target that raises an error")]
    [DataRow("Dim D As Object ;; Set D = New Node ;; D.KidS(Ix(\"i\", 1)).Take2 Ix(\"j\", 2), Vl(\"v\", 5) ;; P Trail", "j v i kid(1) take(2,5) |",
        DisplayName = "a Property Get of an object reached late-bound, evaluated after the arguments")]
    [DataRow("Tg2(Ix(\"i\", 1)).Take2 Ix(\"j\", 2), Vl(\"v\", 5) ;; P Trail", "j v i t1 take(2,5) |", DisplayName = "a function, run after the arguments")]
    [DataRow("Steps.Tg2(Ix(\"i\", 1)).Take2 Ix(\"j\", 2), Vl(\"v\", 5) ;; P Trail", "j v i t1 take(2,5) |",
        DisplayName = "a function named with its module, evaluated after the arguments")]
    [DataRow(AnObject + "P B.KidS(Ix(\"i\", 1)).Sum2F(Ix(\"j\", 2), Vl(\"v\", 5)) ;; P Trail", "2,5|j v i kid(1) sum |",
        DisplayName = "a call inside an expression, its target evaluated after the arguments")]
    [DataRow(AnObject + "Dim X As String ;; X = B.KidS(Ix(\"i\", 1)).Sum2F(Ix(\"j\", 2), Vl(\"v\", 5)) ;; P X ;; P Trail", "2,5|j v i kid(1) sum |",
        DisplayName = "a call assigned, its target evaluated after the arguments")]
    public async Task TheArgumentsOfTheTargetsMemberCalls_AreEvaluatedBeforeTheLastArgument_InACallStatement(string body, string expected)
        => Assert.AreEqual(expected, await RunTargetAsync(body));

    [TestMethod]
    [TestCategory("MS-VBAL 5.3.1.11 Procedure Invocation Argument Processing")]
    [DataRow("Tg2(1).Take1 Len(Trail) ;; P Trail", "t1 take(0) |", DisplayName = "a function")]
    [DataRow(AnObject + "B.KidS(1).Take1 Len(Trail) ;; P Trail", "kid(1) take(0) |", DisplayName = "a Property Get")]
    [DataRow("P Tg2(1).Sum2F(Len(Trail), 0) ;; P Trail", "0,0|t1 sum |", DisplayName = "a function, inside an expression")]
    [DataRow("Tg2(Ix(\"i\", 1)).Take1 Len(Trail) ;; P Trail", "i t1 take(0) |", DisplayName = "a function given a call")]
    [DataRow(AnObject + "B.KidS(Ix(\"i\", 1)).Take1 Len(Trail) ;; P Trail", "i kid(1) take(2) |", DisplayName = "a Property Get given a call")]
    [DataRow(AnObject + "B.KidS(1).Take2 Len(Trail), Vl(\"v\", 5) ;; P Trail", "v kid(1) take(2,5) |", DisplayName = "a Property Get, after the last argument")]
    [DataRow("Tg2(1).Take2 Vl(\"v\", 5), Len(Trail) ;; P Trail", "v t1 take(5,2) |", DisplayName = "a function, after the calls")]
    [DataRow(AnObject + "B.Me1.Take1 Len(Trail) ;; P Trail", "me take(0) |", DisplayName = "a Property Get with no argument")]
    [DataRow(AnObject + "P B.KidS(1).Sum2F(Len(Trail), 0) ;; P Trail", "0,0|kid(1) sum |", DisplayName = "a Property Get, inside an expression")]
    public async Task ATargetThatMakesACall_IsEvaluatedAfterWhatTheArgumentsRead(string body, string expected)
        => Assert.AreEqual(expected, await RunTargetAsync(body));

    [TestMethod]
    [TestCategory("MS-VBAL 5.3.1.11 Procedure Invocation Argument Processing")]
    [DataRow("P Three(Tagged(\"a\"), Tagged(\"b\"), Tagged(\"c\")) ;; P Trail", "1,2,3:a i1 b i2 c i3 |a i1 b i2 c i3 t1 t2 t3 |",
        DisplayName = "three objects calls returned")]
    [DataRow("Both Tagged(\"a\"), Tagged(\"b\") ;; P Trail", "a i1 b i2 both12 t1 t2 |", DisplayName = "two objects calls returned, in a call statement")]
    [DataRow("P IdOf(Pick(Tagged(\"a\"), Tagged(\"b\"))) ;; P Trail", "1|a i1 b i2 t2 t1 |", DisplayName = "the objects a call given to a call was given")]
    [DataRow("P Pick(Tagged(\"a\"), Tagged(\"b\")).Id & \":\" & Trail ;; P Trail", "1:a i1 b i2 |a i1 b i2 t2 t1 |",
        DisplayName = "the objects a call was given, when the statement is over")]
    [DataRow("P Join2(IdS(Tagged(\"a\")), Echo(Trail)) ;; P Trail", "1,a i1 |a i1 t1 |", DisplayName = "an object a call was given, after the next call")]
    [DataRow("P Pair(Pick(New Box, Tagged(\"b\")), New Box) ;; P Trail", "2,3:b i1 i2 i3 |b i1 i2 i3 t1 t2 t3 |",
        DisplayName = "the objects a call given to a call was given, then a New argument")]
    public async Task TheObjectsTheArgumentsGave_AreLetGoOfInParameterOrder(string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(body));
}
