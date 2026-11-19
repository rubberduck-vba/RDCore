using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A call bound to the procedure its name resolves to is the call MS-VBAL 5.6.13 makes: its arguments passed as the parameters say - a
/// ByVal one let-coerced by its plan, a ByRef one aliased to the variable it names, a ParamArray collected, an omitted one missing - and what the function
/// returns let-coerced to the type it declares.
/// </summary>
[TestClass]
public sealed class BoundCallTests
{
    private static Task<string[]> RunModuleAsync(params string[] lines)
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\n{string.Join("\r\n", lines)}\r\n");

    // MS-VBAL 5.3.1.11: a ByVal argument is let-coerced to the parameter's type - rounded to even for a Long.
    [TestMethod]
    public async Task AByValArgument_IsLetCoercedToTheTypeOfItsParameter()
        => CollectionAssert.AreEqual(new[] { "2", "4", "Long" }, await RunModuleAsync(
            "Public Sub Main()",
            "    Debug.Print Echo(2.5)",
            "    Debug.Print Echo(3.5)",
            "    Debug.Print TypeName(Echo(1))",
            "End Sub",
            "Private Function Echo(ByVal Value As Long) As Long",
            "    Echo = Value",
            "End Function"));

    [TestMethod]
    public async Task AByValArgument_OutsideTheRangeOfItsParameter_IsAnOverflow()
        => CollectionAssert.AreEqual(new[] { "6" }, await RunModuleAsync(
            "Public Sub Main()",
            "    On Error Resume Next",
            "    Debug.Print Echo(40000)",
            "    Debug.Print Err.Number",
            "End Sub",
            "Private Function Echo(ByVal Value As Integer) As Integer",
            "    Echo = Value",
            "End Function"));

    // MS-VBAL 5.3.1: what is assigned to a function's name is let-coerced to the type it returns.
    [TestMethod]
    public async Task TheResultOfAFunction_IsLetCoercedToTheTypeItReturns()
        => CollectionAssert.AreEqual(new[] { "2", "Integer", "6" }, await RunModuleAsync(
            "Public Sub Main()",
            "    Debug.Print Half(5)",
            "    Debug.Print TypeName(Half(5))",
            "    On Error Resume Next",
            "    Debug.Print Half(100000)",
            "    Debug.Print Err.Number",
            "End Sub",
            "Private Function Half(ByVal Value As Long) As Integer",
            "    Half = Value / 2",
            "End Function"));

    [TestMethod]
    public async Task AByRefArgument_IsTheVariableItNames()
        => CollectionAssert.AreEqual(new[] { "8" }, await RunModuleAsync(
            "Public Sub Main()",
            "    Dim Value As Long",
            "    Value = 7",
            "    Debug.Print Bump(Value) + 0 * Value",
            "End Sub",
            "Private Function Bump(ByRef Target As Long) As Long",
            "    Target = Target + 1",
            "    Bump = Target",
            "End Function"));

    [TestMethod]
    public async Task AParamArray_CollectsTheArgumentsAfterTheOthers()
        => CollectionAssert.AreEqual(new[] { "3", "6" }, await RunModuleAsync(
            "Public Sub Main()",
            "    Debug.Print Count(1, 2, 3)",
            "    Debug.Print Total(1, 2, 3)",
            "End Sub",
            "Private Function Count(ParamArray Items() As Variant) As Long",
            "    Count = UBound(Items) - LBound(Items) + 1",
            "End Function",
            "Private Function Total(ParamArray Items() As Variant) As Long",
            "    Dim Item As Variant",
            "    For Each Item In Items",
            "        Total = Total + Item",
            "    Next",
            "End Function"));

    // MS-VBAL 5.3.1.11: an omitted Variant argument is missing; an omitted one of another type is its default.
    [TestMethod]
    public async Task AnOmittedArgument_IsMissing_OrItsDefault()
        => CollectionAssert.AreEqual(new[] { "True", "False", "10" }, await RunModuleAsync(
            "Public Sub Main()",
            "    Debug.Print Given()",
            "    Debug.Print Given(1)",
            "    Debug.Print Scaled(2)",
            "End Sub",
            "Private Function Given(Optional ByVal Value As Variant) As Boolean",
            "    Given = IsMissing(Value)",
            "End Function",
            "Private Function Scaled(ByVal Value As Long, Optional ByVal Factor As Long = 5) As Long",
            "    Scaled = Value * Factor",
            "End Function"));

    // a function that declares no parameter is not given what follows it in parentheses: what it returns is indexed.
    [TestMethod]
    public async Task AFunction_WithNoParameter_HasWhatItReturnsIndexed()
        => CollectionAssert.AreEqual(new[] { "20" }, await RunModuleAsync(
            "Public Sub Main()",
            "    Debug.Print Values(1)",
            "End Sub",
            "Private Function Values() As Variant",
            "    Values = Array(10, 20, 30)",
            "End Function"));

    [TestMethod]
    public async Task ANamedArgument_IsPassedToItsParameter()
        => CollectionAssert.AreEqual(new[] { "-3" }, await RunModuleAsync(
            "Public Sub Main()",
            "    Debug.Print Difference(Right:=5, Left:=2)",
            "End Sub",
            "Private Function Difference(ByVal Left As Long, ByVal Right As Long) As Long",
            "    Difference = Left - Right",
            "End Function"));
}
