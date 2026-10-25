using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.4.3.8, §5.4.3.9, §5.4.3.4</strong>: a function - of the library, of the workspace, a <c>Declare</c> - or a property of a module with no
/// accessor for it, named as the target of a <c>Let</c>, a <c>Set</c>, or an element of an <c>Erase</c> outside its own body, is a call of it: what it returns
/// is assigned through, or erased. What cannot be one is the compile error MS-VBA reports, and what can is called when it runs (Excel-verified, oracle\s28).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.8 Let Statement")]
[TestCategory("MS-VBAL 5.4.3.9 Set Statement")]
[TestCategory("MS-VBAL 5.4.3.4 Erase Statement")]
public sealed class FunctionTargetTests
{
    private static string Program(string declarations, string body)
        => $"Attribute VB_Name = \"Program\"\r\n{declarations.Replace(" ;; ", "\r\n")}\r\nPublic Sub Main()\r\nOn Error Resume Next\r\n{body.Replace(" ;; ", "\r\n")}\r\nEnd Sub\r\n";

    private const string LongFunction = "Function F() As Long ;; F = 1 ;; End Function";
    private const string VariantFunction = "Function G() As Variant ;; End Function";
    private const string ArrayFunction = "Function G() As Variant ;; G = Array(1, 2) ;; End Function";
    private const string ObjectFunction = "Function H() As Object ;; Set H = New Collection ;; End Function";
    private const string LongProperty = "Property Get PL() As Long ;; PL = 1 ;; End Property";
    private const string VariantProperty = "Property Get PV() As Variant ;; PV = 1 ;; End Property";

    [TestMethod]
    [DataRow("", "Erase Left", "Argument not optional", DisplayName = "A01 Erase of a library function that takes an argument")]
    [DataRow("", "Left = \"x\"", "Argument not optional", DisplayName = "A02 Let")]
    [DataRow("", "Strings.Left = \"x\"", "Argument not optional", DisplayName = "A05 qualified by its module")]
    [DataRow("", "Environ = \"x\"", "Argument not optional", DisplayName = "B18")]
    [DataRow("", "Set Left = Nothing", "Argument not optional", DisplayName = "B07 Set")]
    [DataRow("", "Set Left$ = Nothing", "Argument not optional", DisplayName = "B24 Set: the argument before the type")]
    [DataRow("", "Erase Left$", "Argument not optional", DisplayName = "B26 Erase: the argument before the type")]
    [DataRow("", "Left$ = \"x\"", "Function call on left-hand side of assignment must return Variant or Object", DisplayName = "A12 Let: the type before the argument")]
    [DataRow("", "Strings.Left$ = \"x\"", "Function call on left-hand side of assignment must return Variant or Object", DisplayName = "qualified by its module")]
    [DataRow(LongFunction, "F = 6", "Function call on left-hand side of assignment must return Variant or Object", DisplayName = "A06 a function of the workspace")]
    [DataRow("Private Declare PtrSafe Function GetTickCount Lib \"kernel32\" () As Long", "GetTickCount = 5", "Function call on left-hand side of assignment must return Variant or Object", DisplayName = "A07 a Declare")]
    [DataRow(LongFunction, "Erase F", "Expected array", DisplayName = "A10")]
    [DataRow("", "Erase Err", "Expected array", DisplayName = "B17")]
    [DataRow(LongFunction, "Set F = Nothing", "Invalid use of property", DisplayName = "B23")]
    [DataRow("", "Set Err = Nothing", "Invalid use of property", DisplayName = "A14 the library's Err, a property in MS-VBA")]
    [DataRow("", "Timer = 5", "Can't assign to read-only property", DisplayName = "B02 a property of the library")]
    [DataRow(LongProperty, "PL = 5", "Can't assign to read-only property", DisplayName = "W1 a property of the workspace")]
    [DataRow(LongProperty, "Set PL = Nothing", "Invalid use of property", DisplayName = "W3")]
    [DataRow("", "Erase Timer", "Expected array", DisplayName = "W6")]
    public async Task WhatCannotBeCalledSo_IsTheCompileErrorMSVBAReports(string declarations, string body, string error)
    {
        var errors = await LoadErrorsAsync([], Program(declarations, body));

        Assert.HasCount(1, errors, string.Join(" | ", errors));
        StringAssert.StartsWith(errors[0], error);
    }

    [TestMethod]
    [DataRow(VariantFunction, "Err.Clear ;; G = 6 ;; Debug.Print Err.Number", "424", DisplayName = "A08 what returns no object has nothing to assign through")]
    [DataRow(VariantFunction, "Err.Clear ;; Set G = New Collection ;; Debug.Print Err.Number", "424", DisplayName = "A13 Set")]
    [DataRow(VariantFunction, "Err.Clear ;; G() = 6 ;; Debug.Print Err.Number", "424", DisplayName = "B14 called with its parentheses")]
    [DataRow(ObjectFunction, "Err.Clear ;; H = 6 ;; Debug.Print Err.Number", "438", DisplayName = "A09 an object with no default member")]
    [DataRow(ObjectFunction, "Err.Clear ;; Set H = New Collection ;; Debug.Print Err.Number", "438", DisplayName = "B25")]
    [DataRow("", "Err.Clear ;; Command = \"x\" ;; Debug.Print Err.Number", "424", DisplayName = "B03 a function of the library")]
    [DataRow("", "Err.Clear ;; Command() = \"x\" ;; Debug.Print Err.Number", "424", DisplayName = "B10")]
    [DataRow("", "Err.Clear ;; CurDir = \"x\" ;; Debug.Print Err.Number", "424", DisplayName = "B04")]
    [DataRow("", "Err.Clear ;; Set Command = Nothing ;; Debug.Print Err.Number", "424", DisplayName = "B08")]
    [DataRow("", "Err.Clear ;; Left(\"ab\", 1) = \"x\" ;; Debug.Print Err.Number", "424", DisplayName = "B20 given its arguments")]
    [DataRow("", "Err.Clear ;; Now = 5 ;; Debug.Print Err.Number", "424", DisplayName = "B01 a property of the library")]
    [DataRow(VariantProperty, "Err.Clear ;; PV = 5 ;; Debug.Print Err.Number", "424", DisplayName = "W2 a property of the workspace")]
    [DataRow(VariantProperty, "Err.Clear ;; Set PV = Nothing ;; Debug.Print Err.Number", "424", DisplayName = "W4")]
    [DataRow("", "Err.Clear ;; Set Now = Nothing ;; Debug.Print Err.Number", "424", DisplayName = "W5")]
    [DataRow("", "Err.Clear ;; Err = 5 ;; Debug.Print Err.Number", "5", DisplayName = "A03 an object's default member: Err.Number")]
    [DataRow("", "Err.Clear ;; Erase Now ;; Debug.Print Err.Number ;; Err.Clear ;; Erase Command ;; Debug.Print Err.Number", "13|13", DisplayName = "B19 Erase of no array")]
    [DataRow(VariantFunction, "Err.Clear ;; Erase G ;; Debug.Print Err.Number", "13", DisplayName = "B21")]
    [DataRow(ArrayFunction, "Err.Clear ;; Erase G ;; Debug.Print Err.Number", "0", DisplayName = "B22 an array it returns is a copy of its own")]
    public async Task WhatCanBeCalledSo_IsCalledWhenItRuns(string declarations, string body, string printed)
        => Assert.AreEqual(printed, string.Join("|", await RunAsync([], Program(declarations, body))));

    // C01-C05: "Constant expression required", whether or not anything reads the constant.
    [TestMethod]
    [DataRow("Const A As Long = B ;; Const B As Long = A", "Debug.Print A", DisplayName = "C01 two constants of the module")]
    [DataRow("", "Const A = B ;; Const B = A ;; Debug.Print A", DisplayName = "C02 of the procedure")]
    [DataRow("Const A = A", "Debug.Print 1", DisplayName = "C03 itself")]
    [DataRow("Const A = B ;; Const B = A", "Debug.Print 1", DisplayName = "C04 that nothing reads")]
    [DataRow("Const A = B + 1 ;; Const B = C ;; Const C = A", "Debug.Print 1", DisplayName = "C05 through an operator, and three of them")]
    public async Task AConstantWhoseValueNamesItself_IsAConstantExpressionRequired(string declarations, string body)
    {
        var errors = await LoadErrorsAsync([], Program(declarations, body));

        Assert.IsNotEmpty(errors);
        Assert.IsTrue(errors.All(error => error.StartsWith("Constant expression required", StringComparison.Ordinal)), string.Join(" | ", errors));
    }

    // C06: a constant named by another is not a cycle.
    [TestMethod]
    public async Task AConstantThatNamesAnother_IsNoCycle()
        => CollectionAssert.AreEqual(new[] { "2" }, await RunAsync([], Program("Const A = 1 ;; Const B = A + 1", "Debug.Print B")));
}
