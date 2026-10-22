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
            "Public Function LongFn() As Long", "    LongFn = 7", "End Function")),
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
    public async Task WhatMsVbaRejectsAtCompileTime_IsACompileError(string body, string message)
        => StringAssert.Contains(string.Join(" / ", await LoadErrorsAsync(Classes, Modules, Program(body))), message);

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
    public async Task WhatMsVbaRaisesAtRunTime_IsRaised(string body, string error)
        => Assert.AreEqual(error, (await RunAsync(Classes, Modules, Program(body))).Last());

    // MS-VBA: what MS-VBA compiles and runs to a value.
    [TestMethod]
    [DataRow("Debug.Print Globals.NoArgs(1)", "20", DisplayName = "a function that takes no argument, named through its module, is indexed")]
    [DataRow("Dim v As Variant ;; Set v = New Collection ;; v.Add 1 ;; With v ;; Debug.Print .Count ;; End With", "1", DisplayName = "a With target that is a Variant holding an object")]
    public async Task WhatMsVbaRuns_IsRun(string body, string printed)
        => Assert.AreEqual(printed, (await RunAsync(Classes, Modules, Program(body))).Last());
}
