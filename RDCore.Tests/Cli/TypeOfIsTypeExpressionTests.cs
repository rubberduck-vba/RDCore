using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.6.7</strong> TypeOf...Is: the type it tests against is a type expression (<strong>§5.6.16.7</strong>), refused as MS-VBA refuses it, and
/// what it tests is an object, of which Nothing is error 91.
/// </summary>
/// <remarks>
/// Each expected outcome is what MS-VBA did with the same code, in Excel (64-bit, en-US): the compile error it reported, or what the probe printed.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.6.7 TypeOf...Is Expressions")]
public sealed class TypeOfIsTypeExpressionTests
{
    private static readonly IReadOnlyList<(string Name, string Source)> Classes =
        [("Counter", ClassModule("Counter", "Public Value As Long"))];

    private static readonly IReadOnlyList<(string Name, string Source)> Modules =
        [("Globals", StandardModule("Globals", "Public Count As Long", "Public Enum Color", "    Red = 1", "End Enum", "Public Sub SomeSub()", "End Sub"))];

    // the probe prints each value it is given, and Err.Number when it is asked to; an error skips the statement that raised it.
    private static string Program(params string[] body) => string.Join("\r\n",
        "Attribute VB_Name = \"Program\"",
        "Option Explicit",
        "Private Type Pair", "    X As Long", "End Type",
        "Public Sub Main()",
        "    On Error Resume Next",
        "    " + string.Join("\r\n    ", body),
        "End Sub",
        string.Empty);

    [TestMethod]
    [DataRow("Is Long", "Syntax error")]
    [DataRow("Is Variant", "Syntax error")]
    [DataRow("Is String", "Syntax error")]
    [DataRow("Is Date", "Syntax error")]
    [DataRow("Is Decimal", "Syntax error")]
    [DataRow("Is LongPtr", "Syntax error")]
    [DataRow("Is Nothing", "Syntax error")]
    [DataRow("Is Empty", "Syntax error")]
    [DataRow("Is String * 2", "Syntax error")]
    [DataRow("Is Object()", "Syntax error")]
    [DataRow("Is Strings", "Automation type not supported in Visual Basic")]
    [DataRow("Is VBA.Strings", "Automation type not supported in Visual Basic")]
    [DataRow("Is VBA", "Expected user-defined type, not project")]
    [DataRow("Is Globals", "A module is not a valid type")]
    [DataRow("Is Program", "A module is not a valid type")]
    [DataRow("Is Count", "User-defined type (UDT) not defined")]
    [DataRow("Is SomeSub", "User-defined type (UDT) not defined")]
    [DataRow("Is Nope", "User-defined type (UDT) not defined")]
    [DataRow("Is VBA.Object", "User-defined type (UDT) not defined")]
    public async Task ATypeThatIsNoClass_IsTheCompileErrorMSVBAReports(string type, string expected)
    {
        var errors = await LoadErrorsAsync(Classes, Modules, Program("Dim o As Object", $"Debug.Print TypeOf o {type}"));

        Assert.HasCount(1, errors, string.Join("; ", errors));
        StringAssert.StartsWith(errors[0], expected);
    }

    [TestMethod]
    [DataRow("Is Collection", "True")]
    [DataRow("Is VBA.Collection", "True")]
    [DataRow("Is Object", "True")]
    [DataRow("Is Counter", "False")]
    [DataRow("Is Pair", "False")]
    [DataRow("Is Color", "False")]
    public async Task AnObject_IsOfItsClass_AndOfObject_AndOfNoOtherType(string type, string expected)
    {
        var printed = await RunAsync(Classes, Modules, Program("Dim o As Object", "Set o = New Collection", $"Debug.Print TypeOf o {type}", "Debug.Print Err.Number"));

        CollectionAssert.AreEqual(new[] { expected, "0" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    [DataRow("Is Collection")]
    [DataRow("Is Object")]
    [DataRow("Is Counter")]
    [DataRow("Is Pair")]
    [DataRow("Is Color")]
    public async Task Nothing_IsError91_WhateverTheType(string type)
    {
        // MS-VBAL 5.6.7: "If the value type of <expression> is Nothing, runtime error 91 ... is raised."
        var printed = await RunAsync(Classes, Modules, Program("Dim o As Object", $"Debug.Print TypeOf o {type}", "Debug.Print Err.Number"));

        CollectionAssert.AreEqual(new[] { "91" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task AValueThatIsNoObject_IsOfNoType()
    {
        var printed = await RunAsync(Classes, Modules, Program(
            "Dim v As Variant", "v = 5", "Debug.Print TypeOf v Is Collection",
            "Dim q As Pair", "Debug.Print TypeOf q Is Collection",
            "Debug.Print Err.Number"));

        CollectionAssert.AreEqual(new[] { "False", "False", "0" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    [DataRow("Dim x As Variant", "x = Long", "Syntax error")]
    [DataRow("Dim x As Variant", "Debug.Print Variant", "Syntax error")]
    [DataRow("Dim x As Variant", "Debug.Print Long + 1", "Syntax error")]
    [DataRow("Dim x As Variant", "Debug.Print Object", "Variable not defined")]
    public async Task AnIntrinsicTypeWhereAValueIs_IsAnError(string declaration, string statement, string expected)
    {
        // a type is no value (MS-VBA: "Syntax error", Excel-verified, oracle\s29 X1-X5); Object is read as a name, as MS-VBA reads it.
        var errors = await LoadErrorsAsync(Classes, Modules, Program(declaration, statement));

        Assert.HasCount(1, errors, string.Join("; ", errors));
        StringAssert.StartsWith(errors[0], expected);
    }
}
