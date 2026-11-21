using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.4.3.8, §5.4.3.9, §5.4.3.4</strong>: a function - of the library, of the workspace, a <c>Declare</c> - or a property of a module with no
/// accessor for it, named as the target of a <c>Let</c>, a <c>Set</c>, or an element of an <c>Erase</c> outside its own body, is a call of it: what it returns
/// is assigned through, or erased. What cannot be one is the compile error MS-VBA reports, and what can is called when it runs (observed in Excel).
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
    [DataRow("", "Erase Left", "Argument not optional", DisplayName = "Erase of a library function that takes an argument")]
    [DataRow("", "Left = \"x\"", "Argument not optional", DisplayName = "Let")]
    [DataRow("", "Strings.Left = \"x\"", "Argument not optional", DisplayName = "Let, qualified by its module")]
    [DataRow("", "Environ = \"x\"", "Argument not optional", DisplayName = "Let of another library function, Environ")]
    [DataRow("", "Set Left = Nothing", "Argument not optional", DisplayName = "Set")]
    [DataRow("", "Set Left$ = Nothing", "Argument not optional", DisplayName = "Set: the argument before the type")]
    [DataRow("", "Erase Left$", "Argument not optional", DisplayName = "Erase: the argument before the type")]
    [DataRow("", "Left$ = \"x\"", "Function call on left-hand side of assignment must return Variant or Object",
        DisplayName = "Let: the type before the argument")]
    [DataRow("", "Strings.Left$ = \"x\"", "Function call on left-hand side of assignment must return Variant or Object", DisplayName = "qualified by its module")]
    [DataRow(LongFunction, "F = 6", "Function call on left-hand side of assignment must return Variant or Object", DisplayName = "a function of the workspace")]
    [DataRow("Private Declare PtrSafe Function GetTickCount Lib \"kernel32\" () As Long", "GetTickCount = 5", "Function call on left-hand side of assignment must return Variant or Object",
        DisplayName = "a Declare")]
    [DataRow(LongFunction, "Erase F", "Expected array", DisplayName = "Erase of a function of the workspace")]
    [DataRow("", "Erase Err", "Expected array", DisplayName = "Erase of the library's Err")]
    [DataRow(LongFunction, "Set F = Nothing", "Invalid use of property", DisplayName = "Set of a function of the workspace")]
    [DataRow("", "Set Err = Nothing", "Invalid use of property", DisplayName = "the library's Err, a property in MS-VBA")]
    [DataRow("", "Timer = 5", "Can't assign to read-only property", DisplayName = "a property of the library")]
    [DataRow(LongProperty, "PL = 5", "Can't assign to read-only property", DisplayName = "a property of the workspace")]
    [DataRow(LongProperty, "Set PL = Nothing", "Invalid use of property", DisplayName = "Set of a property of the workspace")]
    [DataRow("", "Erase Timer", "Expected array", DisplayName = "Erase of a property of the library")]
    public async Task WhatCannotBeCalledSo_IsTheCompileErrorMSVBAReports(string declarations, string body, string error)
    {
        var errors = await LoadErrorsAsync([], Program(declarations, body));

        Assert.HasCount(1, errors, string.Join(" | ", errors));
        StringAssert.StartsWith(errors[0], error);
    }

    [TestMethod]
    [DataRow(VariantFunction, "Err.Clear ;; G = 6 ;; Debug.Print Err.Number", "424", DisplayName = "what returns no object has nothing to assign through")]
    [DataRow(VariantFunction, "Err.Clear ;; Set G = New Collection ;; Debug.Print Err.Number", "424", DisplayName = "Set")]
    [DataRow(VariantFunction, "Err.Clear ;; G() = 6 ;; Debug.Print Err.Number", "424", DisplayName = "called with its parentheses")]
    [DataRow(ObjectFunction, "Err.Clear ;; H = 6 ;; Debug.Print Err.Number", "438", DisplayName = "an object with no default member")]
    [DataRow(ObjectFunction, "Err.Clear ;; Set H = New Collection ;; Debug.Print Err.Number", "438", DisplayName = "Set of an object with no default member")]
    [DataRow("", "Err.Clear ;; Command = \"x\" ;; Debug.Print Err.Number", "424", DisplayName = "a function of the library")]
    [DataRow("", "Err.Clear ;; Command() = \"x\" ;; Debug.Print Err.Number", "424", DisplayName = "a function of the library, called with its parentheses")]
    [DataRow("", "Err.Clear ;; CurDir = \"x\" ;; Debug.Print Err.Number", "424", DisplayName = "another function of the library, CurDir")]
    [DataRow("", "Err.Clear ;; Set Command = Nothing ;; Debug.Print Err.Number", "424", DisplayName = "Set of a function of the library")]
    [DataRow("", "Err.Clear ;; Left(\"ab\", 1) = \"x\" ;; Debug.Print Err.Number", "424", DisplayName = "given its arguments")]
    [DataRow("", "Err.Clear ;; Now = 5 ;; Debug.Print Err.Number", "424", DisplayName = "a property of the library")]
    [DataRow(VariantProperty, "Err.Clear ;; PV = 5 ;; Debug.Print Err.Number", "424", DisplayName = "a property of the workspace")]
    [DataRow(VariantProperty, "Err.Clear ;; Set PV = Nothing ;; Debug.Print Err.Number", "424", DisplayName = "Set of a property of the workspace")]
    [DataRow("", "Err.Clear ;; Set Now = Nothing ;; Debug.Print Err.Number", "424", DisplayName = "Set of a property of the library")]
    [DataRow("", "Err.Clear ;; Err = 5 ;; Debug.Print Err.Number", "5", DisplayName = "an object's default member: Err.Number")]
    [DataRow("", "Err.Clear ;; Erase Now ;; Debug.Print Err.Number ;; Err.Clear ;; Erase Command ;; Debug.Print Err.Number", "13|13",
        DisplayName = "Erase of no array")]
    [DataRow(VariantFunction, "Err.Clear ;; Erase G ;; Debug.Print Err.Number", "13", DisplayName = "Erase of what returns no array")]
    [DataRow(ArrayFunction, "Err.Clear ;; Erase G ;; Debug.Print Err.Number", "0", DisplayName = "an array it returns is a copy of its own")]
    public async Task WhatCanBeCalledSo_IsCalledWhenItRuns(string declarations, string body, string printed)
        => Assert.AreEqual(printed, string.Join("|", await RunAsync([], Program(declarations, body))));

    // a class whose default member has a Property Set
    private static readonly (string, string) Settable = ("Settable", ClassModule("Settable",
        "Private mInner As Object",
        "Public Property Get Inner() As Object", "Attribute Inner.VB_UserMemId = 0", "    Set Inner = mInner", "End Property",
        "Public Property Set Inner(ByVal o As Object)", "    Set mInner = o", "End Property"));

    // MS-VBAL §5.4.3.9: what the call returns is Set through its default member, which is Set-coerced to what that member's Property Set takes, and not to
    // what the call returns (MS-VBA, observed in Excel: the default member is set, error 0).
    [TestMethod]
    [DataRow("Set One = New Collection", DisplayName = "a function of the class")]
    [DataRow("Set AsVariant = New Collection", DisplayName = "a Variant function that returns one")]
    [DataRow("Set OneProperty = New Collection", DisplayName = "a property of the class with no Property Set")]
    [DataRow("Set One() = New Collection", DisplayName = "called with its parentheses")]
    public async Task ASetOfACallOfAClassWhoseDefaultMemberHasAPropertySet_SetsIt(string statement)
        => Assert.AreEqual("0|Collection", string.Join("|", await RunAsync([Settable], Program(
            "Private mOne As Settable ;; Public Function One() As Settable ;; If mOne Is Nothing Then Set mOne = New Settable ;; Set One = mOne ;; End Function"
                + " ;; Public Function AsVariant() As Variant ;; Set AsVariant = One() ;; End Function"
                + " ;; Public Property Get OneProperty() As Settable ;; Set OneProperty = One() ;; End Property",
            $"Err.Clear ;; {statement} ;; Debug.Print Err.Number ;; Debug.Print TypeName(One.Inner)"))));

    // C01-C05: "Constant expression required", whether or not anything reads the constant.
    [TestMethod]
    [DataRow("Const A As Long = B ;; Const B As Long = A", "Debug.Print A", DisplayName = "two constants of the module")]
    [DataRow("", "Const A = B ;; Const B = A ;; Debug.Print A", DisplayName = "of the procedure")]
    [DataRow("Const A = A", "Debug.Print 1", DisplayName = "itself")]
    [DataRow("Const A = B ;; Const B = A", "Debug.Print 1", DisplayName = "that nothing reads")]
    [DataRow("Const A = B + 1 ;; Const B = C ;; Const C = A", "Debug.Print 1", DisplayName = "through an operator, and three of them")]
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
