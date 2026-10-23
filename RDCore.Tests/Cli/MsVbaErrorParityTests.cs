using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// What MS-VBA rejects at compile time, and what it raises at run time, for the member accesses, indexes, special forms and statement targets the bound
/// tree evaluates (IR lowering S0.12): each case recorded in Excel, compiled with Debug > Compile, then run with its errors trapped one statement at a time.
/// </summary>
[TestClass]
public sealed class MsVbaErrorParityTests
{
    private static readonly IReadOnlyList<(string Name, string Source)> Classes =
    [
        ("Counter", ClassModule("Counter",
            "Private mValue As Long",
            "Public Sub Increment()", "    mValue = mValue + 1", "End Sub",
            "Public Property Get Value() As Long", "    Value = mValue", "End Property")),
        ("Point", ClassModule("Point", "Public X As Long", "Public Inner As Point")),
    ];

    private static readonly IReadOnlyList<(string Name, string Source)> Modules =
    [
        ("Globals", StandardModule("Globals",
            "Public Count As Long",
            "Public Enum Color", "    Red = 1", "    Green = 2", "End Enum",
            "Public Function NoArgs() As Variant", "    NoArgs = Array(10, 20, 30)", "End Function",
            "Public Function LongFn() As Long", "    LongFn = 7", "End Function",
            "Public Function Twice(ByVal N As Long) As Long", "    Twice = 2 * N", "End Function",
            "Public Sub SomeSub()", "End Sub",
            "Public Sub TakesLong(ByRef N As Long)", "End Sub",
            "Public Sub TakesLongArray(ByRef A() As Long)", "End Sub",
            "Public Sub TakesObject(ByRef O As Object)", "End Sub",
            "Public Sub TakesCollection(ByRef C As Collection)", "End Sub",
            "Public Sub TakesPtr(ByVal P As LongPtr)", "End Sub",
            "Public Function Two() As Long", "    Two = 2", "End Function")),
    ];

    // the case's statements, in a procedure that traps every error and prints what each statement printed.
    private static string Program(string body)
        => string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            "Option Explicit",
            "Private Type Pair", "    X As Long", "End Type",
            "Public Sub Main()",
            "    On Error Resume Next",
            "    " + string.Join("\r\n    ", body.Split(" ;; ")),
            "End Sub") + "\r\n";

    // MS-VBA: Compile error: Method or data member not found - a class module of the workspace is a closed set of members.
    [TestMethod]
    [DataRow("Dim c As New Counter ;; Debug.Print c.Reset", DisplayName = "an unknown member read")]
    [DataRow("Dim c As New Counter ;; c.Reset", DisplayName = "an unknown member called")]
    [DataRow("Dim c As New Counter ;; Debug.Print c.Reset(1)", DisplayName = "an unknown member called with arguments")]
    [DataRow("Dim c As New Counter ;; With c ;; Debug.Print .Reset ;; End With", DisplayName = "an unknown member of a With target")]
    [DataRow("Debug.Print Globals.Nope", DisplayName = "an unknown member of a module")]
    [DataRow("Debug.Print Color.Blue", DisplayName = "an unknown member of an enumeration")]
    [DataRow("Dim r As Pair ;; Debug.Print r.Z", DisplayName = "an unknown field of a record")]
    public async Task AnUnknownMemberOfAClosedSet_IsMethodOrDataMemberNotFound(string body)
        => StringAssert.Contains(string.Join(" / ", await LoadErrorsAsync(Classes, Modules, Program(body))), "Method or data member not found");

    // MS-VBA's compile errors, by their message.
    [TestMethod]
    [DataRow("Debug.Print LongFn()(1)", "Type mismatch", DisplayName = "an index of what a function returns, a Long")]
    [DataRow("Dim n As Long ;; Debug.Print UBound(n)", "Expected array", DisplayName = "UBound of a Long")]
    [DataRow("Debug.Print Me Is Nothing", "Invalid use of Me keyword", DisplayName = "Me in a standard module")]
    [DataRow("Dim c As New Counter ;; Debug.Print c.Increment", "Expected Function or variable", DisplayName = "a Sub of a class printed")]
    [DataRow("Debug.Print Globals.SomeSub", "Expected Function or variable", DisplayName = "a Sub of a module printed")]
    [DataRow("Dim x As Long ;; x = Globals.SomeSub", "Expected Function or variable", DisplayName = "a Sub assigned")]
    [DataRow("Dim c As New Counter ;; Dim x As Long ;; x = c.Increment + 1", "Expected Function or variable", DisplayName = "a Sub as an operand")]
    [DataRow("Dim c As New Counter ;; Debug.Print Globals.Twice(c.Increment)", "Expected Function or variable", DisplayName = "a Sub as an argument")]
    [DataRow("Dim c As New Counter ;; c.Increment 1", "Wrong number of arguments or invalid property assignment", DisplayName = "an argument to a Sub that takes none")]
    [DataRow("Dim c As New Counter ;; c.Increment By:=1", "Wrong number of arguments or invalid property assignment", DisplayName = "a named argument to a Sub that takes none")]
    [DataRow("Debug.Print Globals.Twice(1, 2)", "Wrong number of arguments or invalid property assignment", DisplayName = "one argument too many")]
    [DataRow("Globals.SomeSub 1", "Wrong number of arguments or invalid property assignment", DisplayName = "an argument to a Sub of a module that takes none")]
    [DataRow("Call Globals.SomeSub(1)", "Wrong number of arguments or invalid property assignment", DisplayName = "an argument to a Sub that takes none, with Call")]
    [DataRow("Dim c As New Collection ;; Debug.Print c.Count(1)", "Wrong number of arguments or invalid property assignment", DisplayName = "an argument to a property that returns a Long and takes none")]
    [DataRow("Debug.Print Globals.Twice()", "Argument not optional", DisplayName = "a required argument left out")]
    [DataRow("Dim c As New Collection ;; c.Add", "Argument not optional", DisplayName = "a required argument of a library's method left out")]
    [DataRow("Dim c As Collection ;; c = New Collection", "Argument not optional", DisplayName = "a Let of a Collection variable, whose default member takes an argument")]
    [DataRow("Debug.Print Globals.Twice(N:=1, N:=2)", "Named argument already specified", DisplayName = "a parameter named twice")]
    [DataRow("Debug.Print Globals.Twice(Q:=1)", "Named argument not found", DisplayName = "a name the procedure does not declare")]
    [DataRow("Dim c As New Counter ;; c.Value = 1", "Can't assign to read-only property", DisplayName = "a Let of a property that has only a Get")]
    [DataRow("Dim c As New Counter ;; Set c.Value = New Counter", "Invalid use of property", DisplayName = "a Set of a property that has only a Get")]
    [DataRow("Dim n As Long ;; LSet n = \"x\"", "LSet allowed only on strings and user-defined types", DisplayName = "an LSet of a Long")]
    [DataRow("Dim n As Long ;; RSet n = \"x\"", "RSet allowed only on strings", DisplayName = "an RSet of a Long")]
    [DataRow("Dim n As Long ;; Set n = New Collection", "Object required", DisplayName = "a Set of a Long")]
    [DataRow("Color.Red = 3", "Assignment to constant not permitted", DisplayName = "a Let of an enumeration's member")]
    [DataRow("vbSunday = 5", "Assignment to constant not permitted", DisplayName = "a Let of a library enumeration's member")]
    [DataRow("Const k As Long = 1 ;; k = 2", "Assignment to constant not permitted", DisplayName = "a Let of a constant")]
    [DataRow("Const k As Long = 1 ;; Set k = Nothing", "Assignment to constant not permitted", DisplayName = "a Set of a constant")]
    [DataRow("Dim n As Long ;; Const k As Long = n", "Constant expression required", DisplayName = "a constant of a variable")]
    [DataRow("Const k = Len(\"abc\")", "Constant expression required", DisplayName = "a constant of a function MS-VBAL admits and MS-VBA refuses")]
    [DataRow("Const k = Globals.Two", "Constant expression required", DisplayName = "a constant of a function of the workspace")]
    [DataRow("Const k = Nothing", "Invalid data type for constant", DisplayName = "a constant of Nothing")]
    [DataRow("Const k As Integer = 99999", "Overflow", DisplayName = "a constant too large for its type")]
    [DataRow("Const k As Long = 2147483647 + 1", "Overflow", DisplayName = "a constant whose operation overflows")]
    [DataRow("Const k = 1 / 0", "Division by zero", DisplayName = "a constant that divides by zero")]
    [DataRow("Dim a(Globals.Two) As Long", "Constant expression required", DisplayName = "a bound of a fixed-size array that calls a function")]
    [DataRow("Dim i As Integer ;; Globals.TakesLong i", "ByRef argument type mismatch", DisplayName = "an Integer variable to a ByRef Long")]
    [DataRow("Dim v As Variant ;; Globals.TakesLong v", "ByRef argument type mismatch", DisplayName = "a Variant variable to a ByRef Long")]
    [DataRow("Dim v As Variant ;; Set v = New Collection ;; Globals.TakesCollection v", "ByRef argument type mismatch", DisplayName = "a Variant variable to a ByRef Collection")]
    [DataRow("Dim a(2) As Integer ;; Globals.TakesLongArray a", "Type mismatch: array or user-defined type expected", DisplayName = "an Integer array to a Long() parameter")]
    [DataRow("Dim n As Long ;; Debug.Print n.X", "Invalid qualifier", DisplayName = "a member of a Long")]
    [DataRow("Dim n As Long ;; Debug.Print n(1)", "Expected array", DisplayName = "an index of a Long")]
    [DataRow("Dim n As Long ;; Erase n", "Expected array", DisplayName = "an Erase of a Long")]
    [DataRow("Dim a(2) As Long ;; Debug.Print a(1, 2)", "Wrong number of dimensions", DisplayName = "two subscripts of a one-dimension array")]
    [DataRow("Dim a(2) As Long ;; ReDim a(5)", "Array already dimensioned", DisplayName = "a ReDim of a fixed-size array")]
    [DataRow("Globals.Two() = 1", "Function call on left-hand side of assignment must return Variant or Object", DisplayName = "a call of a function that returns a Long assigned")]
    [DataRow("Dim i As Long ;; For i = 1 To 2 ;; For i = 1 To 2 ;; Next ;; Next", "For control variable already in use", DisplayName = "a For loop nested in a loop of its variable")]
    [DataRow("Dim c As New Collection, x As Variant ;; For Each x In c ;; For Each x In c ;; Next ;; Next", "For control variable already in use", DisplayName = "a For Each loop nested in a loop of its variable")]
    [DataRow("Dim i As Long, j As Long ;; For i = 1 To 2 ;; Next j", "Invalid Next control variable reference", DisplayName = "a Next of another variable")]
    [DataRow("Dim i As Long, j As Long ;; For i = 1 To 2 ;; For j = 1 To 2 ;; Next i, j", "Invalid Next control variable reference", DisplayName = "a Next of two loops, in the wrong order")]
    [DataRow("Dim i As Long, x As Variant, c As New Collection ;; For Each x In c ;; Next i", "Invalid Next control variable reference", DisplayName = "a Next of another variable, of a For Each loop")]
    [DataRow("Dim a(2) As Long, b(2) As Long ;; a = b", "Can't assign to array", DisplayName = "a fixed-size array assigned")]
    [DataRow("Dim x As New Long", "Syntax error", DisplayName = "a variable of a Long declared As New")]
    [DataRow("Dim n As Long ;; ReDim n(5)", "Expected array", DisplayName = "a ReDim of a Long")]
    [DataRow("Dim x As Variant ;; Set x = 5", "Object required", DisplayName = "a Set of a Variant to a number")]
    [DataRow("Dim x As Variant ;; Set x = \"a\"", "Object required", DisplayName = "a Set of a Variant to a String")]
    [DataRow("Dim c As Collection ;; Set c = 5", "Type mismatch", DisplayName = "a Set of a Collection variable to a number")]
    [DataRow("Dim o As Object ;; Set o = 5", "Type mismatch", DisplayName = "a Set of an Object variable to a number")]
    [DataRow("Dim x As Long ;; Call x", "Expected Sub, Function, or Property", DisplayName = "a variable called")]
    [DataRow("Call Globals", "Expected variable or procedure, not module", DisplayName = "a module called")]
    [DataRow("Dim x As Variant ;; x = Globals", "Expected variable or procedure, not module", DisplayName = "a module assigned")]
    [DataRow("Dim x As Globals", "A module is not a valid type", DisplayName = "a variable typed as a module")]
    [DataRow("Dim a(5 To 1) As Long", "Range has no values", DisplayName = "an upper bound less than the lower one")]
    [DataRow("Dim a(-1) As Long", "Range has no values", DisplayName = "a negative upper bound")]
    [DataRow("Debug.Print Len()", "Syntax error", DisplayName = "Len with no argument")]
    [DataRow("Debug.Print Len", "Syntax error", DisplayName = "Len with no argument list")]
    [DataRow("Dim x As Long ;; x = LenB", "Syntax error", DisplayName = "LenB assigned with no argument list")]
    [DataRow("Debug.Print Abs(1, 2)", "Syntax error", DisplayName = "Abs with two arguments")]
    [DataRow("Debug.Print CLng()", "Syntax error", DisplayName = "a conversion function with no argument")]
    [DataRow("Debug.Print String(2)", "Syntax error", DisplayName = "String with one argument")]
    [DataRow("Debug.Print CDec()", "Argument not optional", DisplayName = "CDec, whose name is not reserved, with no argument")]
    [DataRow("Dim c As New Counter ;; Globals.TakesPtr AddressOf c.Increment", "Invalid use of AddressOf operator", DisplayName = "AddressOf an object's method")]
    [DataRow("Globals.TakesPtr AddressOf Counter.Increment", "Invalid use of AddressOf operator", DisplayName = "AddressOf a class module's method")]
    [DataRow("Globals.TakesPtr AddressOf Globals.Count", "Expected Sub, Function, or Property", DisplayName = "AddressOf a variable")]
    [DataRow("Globals.TakesPtr AddressOf Globals.Nope", "Method or data member not found", DisplayName = "AddressOf a procedure that is not there")]
    [DataRow("Dim ll As LongLong ;; Debug.Print Globals.Twice(ll)", "Type mismatch", DisplayName = "a LongLong to a ByVal Long")]
    public async Task WhatMsVbaRejectsAtCompileTime_IsACompileError(string body, string message)
        => StringAssert.Contains(string.Join(" / ", await LoadErrorsAsync(Classes, Modules, Program(body))), message);

    // MS-VBA stops at the first compile error; RD-VBA reports each of them, a diagnostic each - one for each statement, in every procedure.
    [TestMethod]
    public async Task EveryCompileError_IsReported()
    {
        var program = Program("Dim c As New Counter, n As Long ;; Debug.Print UBound(n) ;; c.Reset ;; Color.Red = 3")
            + "Private Sub Other()\r\n    Globals.SomeSub 1\r\nEnd Sub\r\n";

        var errors = await LoadErrorsAsync(Classes, Modules, program);

        Assert.HasCount(4, errors, string.Join(" | ", errors));
        StringAssert.Contains(errors[0], "Expected array");
        StringAssert.Contains(errors[1], "Method or data member not found");
        StringAssert.Contains(errors[2], "Assignment to constant not permitted");
        StringAssert.Contains(errors[3], "Wrong number of arguments or invalid property assignment");
    }

    // MS-VBA compiles a constant subscript out of an array's bounds, and raises error 9 when it runs; the sweep states it before the program runs.
    [TestMethod]
    [Ignore("the sweep assumes every variable it reads, an array included: the bounds of a fixed-size array are declared, and certain, but its read makes the subscript's error 9 an assumed one, which is not stated")]
    public async Task AConstantSubscriptOutOfBounds_IsStatedBeforeTheProgramRuns()
    {
        var model = await ModelAsync([], "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n    Dim a(5) As Long\r\n    a(6) = 1\r\nEnd Sub\r\n");

        var facts = model.Procedures.Single().Runtime;
        Assert.IsNotNull(facts, "the module was loaded, so its code was evaluated");
        Assert.AreEqual((int)RDCore.SDK.Model.Errors.VBRuntimeErrorId.SubscriptOutOfRange, facts.Errors.Single().ErrorId);
    }

    // what MS-VBA compiles, and raises at run time: each statement prints the error it raised.
    [TestMethod]
    [DataRow("Dim c As New Collection ;; Debug.Print c.Nothingness ;; Debug.Print Err.Number", "438", DisplayName = "an unknown member of a library class")]
    [DataRow("Debug.Print Err.Nope ;; Debug.Print Err.Number", "438", DisplayName = "an unknown member of the standard library's ErrObject")]
    [DataRow("Dim o As Object ;; Set o = New Collection ;; Debug.Print o.Nothingness ;; Debug.Print Err.Number", "438", DisplayName = "an unknown member, late-bound")]
    [DataRow("Dim o As Object ;; Set o = New Collection ;; With o ;; Debug.Print .Nothingness ;; End With ;; Debug.Print Err.Number", "438", DisplayName = "an unknown member of a late-bound With target")]
    [DataRow("Dim o As Object ;; Set o = New Counter ;; o.Nope = 1 ;; Debug.Print Err.Number", "438", DisplayName = "an unknown member assigned, late-bound")]
    [DataRow("Dim o As Object ;; Set o = New Counter ;; o.Value = 1 ;; Debug.Print Err.Number", "451", DisplayName = "a Let of a property that has only a Get, late-bound")]
    [DataRow("Dim o As Object ;; Set o = New Counter ;; Set o.Value = Nothing ;; Debug.Print Err.Number", "450", DisplayName = "a Set of a property that has only a Get, late-bound")]
    [DataRow("Dim o As Object ;; Set o = New Counter ;; o.Increment 1 ;; Debug.Print Err.Number", "450", DisplayName = "too many arguments, late-bound")]
    [DataRow("Dim v As Variant ;; v = 5 ;; Debug.Print v.X ;; Debug.Print Err.Number", "424", DisplayName = "a member of a Variant that holds a number")]
    [DataRow("Dim v As Variant ;; v = 5 ;; With v ;; Debug.Print .X ;; End With ;; Debug.Print Err.Number", "424", DisplayName = "a member of a With target that holds a number")]
    [DataRow("Dim c As Counter ;; With c ;; .Increment ;; End With ;; Debug.Print Err.Number", "91", DisplayName = "a member of a With target that is Nothing")]
    [DataRow("Dim v As Variant ;; v = 5 ;; Debug.Print v(1) ;; Debug.Print Err.Number", "13", DisplayName = "an index of a Variant that holds a number")]
    [DataRow("Dim a(2) As Long ;; Debug.Print UBound(a, 0) ;; Debug.Print Err.Number", "9", DisplayName = "UBound of dimension 0")]
    [DataRow("Dim v As Variant ;; v = 5 ;; Debug.Print LBound(v) ;; Debug.Print Err.Number", "13", DisplayName = "LBound of a Variant that holds a number")]
    [DataRow("Dim o As Object ;; Set o = New Point ;; Debug.Print o!X ;; Debug.Print Err.Number", "438", DisplayName = "a dictionary access of an object with no default member, late-bound")]
    [DataRow("Dim pt As Point ;; Set pt = New Collection ;; Debug.Print Err.Number", "13", DisplayName = "a Set of an object of another class")]
    [DataRow("Dim r As Pair ;; r.X = \"x\" ;; Debug.Print Err.Number", "13", DisplayName = "a field of a record let a String that is no number")]
    [DataRow("Dim v As Variant ;; v = 5 ;; LSet v = \"x\" ;; Debug.Print Err.Number", "0", DisplayName = "an LSet of a Variant that holds a number")]
    [DataRow("Dim pt As New Point ;; pt = 5 ;; Debug.Print Err.Number", "438", DisplayName = "a Let of an object whose class has no default member")]
    [DataRow("Dim a(5) As Long ;; a(6) = 1 ;; Debug.Print Err.Number", "9", DisplayName = "a constant subscript past the upper bound")]
    [DataRow("Dim a(5) As Long ;; Debug.Print a(-1) ;; Debug.Print Err.Number", "9", DisplayName = "a constant subscript below the lower bound")]
    public async Task WhatMsVbaRaisesAtRunTime_IsRaised(string body, string error)
        => Assert.AreEqual(error, (await RunAsync(Classes, Modules, Program(body))).Last());

    // MS-VBA: what MS-VBA compiles and runs to a value.
    [TestMethod]
    [DataRow("Debug.Print Globals.NoArgs(1)", "20", DisplayName = "a function that takes no argument, named through its module, is indexed")]
    [DataRow("Dim v As Variant ;; Set v = New Collection ;; v.Add 1 ;; With v ;; Debug.Print .Count ;; End With", "1", DisplayName = "a With target that is a Variant holding an object")]
    [DataRow("Dim v As Variant ;; v = 12345 ;; RSet v = \"x\" ;; Debug.Print \"[\" & v & \"]\"", "[    x]", DisplayName = "an RSet of a Variant that holds a number fits the String it reads as")]
    [DataRow("Const k = 1 + Globals.Color.Red ;; Debug.Print k", "2", DisplayName = "a constant of an enumeration's member, qualified by its module")]
    [DataRow("Const k = \"a\" & vbCrLf ;; Debug.Print Len(k)", "3", DisplayName = "a constant of a library's constant")]
    [DataRow("Const k = 2 ^ 3 Mod 5 ;; Debug.Print k", "3", DisplayName = "a constant of operators")]
    [DataRow("Const n As Long = 2 ;; Dim a(n) As Long ;; Debug.Print UBound(a)", "2", DisplayName = "a bound of a fixed-size array that is a constant")]
    [DataRow("Dim n As Long ;; Globals.TakesLong (n) ;; Debug.Print \"ok\"", "ok", DisplayName = "a variable of another type passed in parentheses, as a value")]
    [DataRow("Dim c As New Collection ;; Globals.TakesObject c ;; Debug.Print \"ok\"", "ok", DisplayName = "a variable of a class to a ByRef Object")]
    [DataRow("Dim o As Object ;; Set o = New Collection ;; Globals.TakesCollection o ;; Debug.Print \"ok\"", "ok", DisplayName = "an Object variable to a ByRef parameter of a class")]
    [DataRow("Dim i As Long, j As Long ;; For i = 1 To 2 ;; For j = 1 To 2 ;; Next j, i ;; Debug.Print i", "3", DisplayName = "a Next of two loops")]
    [DataRow("Dim a() As Long, b(2) As Long ;; a = b ;; Debug.Print UBound(a)", "2", DisplayName = "an array without dimensions assigned")]
    [DataRow("Dim v As Variant ;; v = 5 ;; LSet v = \"xy\" ;; Debug.Print TypeName(v) & \"[\" & v & \"]\"", "String[x]", DisplayName = "an LSet of a Variant that holds a number leaves a String")]
    [DataRow("Dim v As Variant ;; ReDim v(3) ;; Debug.Print UBound(v)", "3", DisplayName = "a ReDim of a Variant")]
    [DataRow("Dim a() As Long ;; ReDim a(3) ;; Debug.Print UBound(a)", "3", DisplayName = "a ReDim of an array without dimensions")]
    [DataRow("ReDim b(3) As Long ;; Debug.Print UBound(b)", "3", DisplayName = "a ReDim that declares its array")]
    [DataRow("Dim a(0 To 0) As Long ;; Debug.Print UBound(a)", "0", DisplayName = "an array of one element")]
    [DataRow("Debug.Print Len(\"abc\")", "3", DisplayName = "Len with its argument")]
    [DataRow("Debug.Print String(2, \"x\")", "xx", DisplayName = "String with its two arguments")]
    public async Task WhatMsVbaRuns_IsRun(string body, string printed)
        => Assert.AreEqual(printed, (await RunAsync(Classes, Modules, Program(body))).Last());
}
