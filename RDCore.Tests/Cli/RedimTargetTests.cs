using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.4.3.3</strong> The name a <c>ReDim</c> re-dimensions "is first matched as a simple name expression": a variable or a constant of any
/// module or library is the match - an array to re-dimension, or an error - and a name that matches nothing declares a new array.
/// </summary>
/// <remarks>
/// Each expected outcome is what MS-VBA did with the same code, in Excel (64-bit, en-US, oracle\s29 R and E cases), under <c>Option Explicit</c>.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.3 ReDim Statement")]
public sealed class RedimTargetTests
{
    private static readonly IReadOnlyList<(string Name, string Source)> Modules =
        [("Globals", StandardModule("Globals",
            "Public Count As Long", "Public Items(2) As String", "Public Dyn() As Long", "Public V As Variant", "Public Const GC = 1",
            "Public Enum Color", "    Red = 1", "End Enum",
            "Public Function NoArgs() As Variant", "    NoArgs = Array(10, 20, 30)", "End Function", "Public Sub SomeSub()", "End Sub"))];

    private static string Program(params string[] body) => string.Join("\r\n",
        "Attribute VB_Name = \"Program\"",
        "Option Explicit",
        "Public Sub Main()",
        "    " + string.Join("\r\n    ", body),
        "End Sub",
        "Public Function Probe() As String",
        "    ReDim Probe(1)",
        "End Function",
        string.Empty);

    private static string ProgramWithoutProbe(params string[] body) => string.Join("\r\n",
        "Attribute VB_Name = \"Program\"",
        "Option Explicit",
        "Public Sub Main()",
        "    " + string.Join("\r\n    ", body),
        "End Sub",
        string.Empty);

    private static async Task AssertError(string expected, params string[] body)
    {
        var errors = await LoadErrorsAsync([], Modules, ProgramWithoutProbe(body));

        Assert.HasCount(1, errors, string.Join("; ", errors));
        StringAssert.StartsWith(errors[0], expected);
    }

    private static async Task AssertPrinted(string expected, params string[] body)
        => CollectionAssert.AreEqual(new[] { expected }, await RunAsync([], Modules, ProgramWithoutProbe(body)));

    [TestMethod]
    [DataRow("ReDim Dyn(3)", "Debug.Print UBound(Globals.Dyn)", "3", DisplayName = "another module's array")]
    [DataRow("ReDim V(2)", "Debug.Print UBound(Globals.V)", "2", DisplayName = "another module's Variant")]
    [DataRow("ReDim Dyn(3): ReDim Preserve Dyn(4)", "Debug.Print UBound(Globals.Dyn)", "4", DisplayName = "Preserve")]
    [DataRow("ReDim Dyn(2): Dyn(1) = 7", "Debug.Print Globals.Dyn(1)", "7", DisplayName = "an element of it")]
    [DataRow("ReDim Globals.Dyn(5)", "Debug.Print UBound(Dyn)", "5", DisplayName = "qualified")]
    public Task AVariableOfAnotherModule_IsTheArrayTheReDimReDimensions(string redim, string print, string expected)
        => AssertPrinted(expected, redim, print);

    [TestMethod]
    [DataRow("ReDim Count(1)", DisplayName = "another module's Long")]
    [DataRow("ReDim Globals.Count(1)", DisplayName = "another module's Long, qualified")]
    [DataRow("ReDim vbCrLf(1)", DisplayName = "a constant of the library")]
    [DataRow("ReDim vbNullString(1)", DisplayName = "another constant of the library")]
    [DataRow("ReDim GC(1)", DisplayName = "another module's constant")]
    [DataRow("ReDim Red(1)", DisplayName = "a member of an enumeration")]
    [DataRow("Const Limit = 10: ReDim Limit(1)", DisplayName = "a constant of the procedure")]
    public Task AVariableOrAConstantThatIsNoArray_IsExpectedArray(string redim)
        => AssertError("Expected array", redim);

    [TestMethod]
    public async Task TheResultOfTheFunctionTheReDimIsIn_IsExpectedArray()
    {
        var errors = await LoadErrorsAsync([], Modules, Program("Debug.Print 1"));

        Assert.HasCount(1, errors, string.Join("; ", errors));
        StringAssert.StartsWith(errors[0], "Expected array");
    }

    [TestMethod]
    [DataRow("ReDim Items(3)")]
    [DataRow("ReDim Globals.Items(3)")]
    public Task AFixedSizeArrayOfAnotherModule_IsAlreadyDimensioned(string redim)
        => AssertError("Array already dimensioned", redim);

    [TestMethod]
    public Task AnotherModulesArray_KeepsItsElementType()
        => AssertError("Can't change data types of array elements", "ReDim Dyn(2) As String");

    [TestMethod]
    [DataRow("ReDim Undeclared(1): Debug.Print UBound(Undeclared)", DisplayName = "a name nothing declares")]
    [DataRow("ReDim Left(1): Debug.Print UBound(Left)", DisplayName = "a function of the library")]
    [DataRow("ReDim NoArgs(1): Debug.Print UBound(NoArgs)", DisplayName = "a function of another module")]
    [DataRow("ReDim SomeSub(1): Debug.Print UBound(SomeSub)", DisplayName = "a procedure of another module")]
    [DataRow("ReDim Strings(1): Debug.Print UBound(Strings)", DisplayName = "a module of the library")]
    public Task WhatIsNoVariableAndNoConstant_IsNoMatch_TheReDimDeclaresANewArray(string statements)
        => AssertPrinted("1", statements);
}
