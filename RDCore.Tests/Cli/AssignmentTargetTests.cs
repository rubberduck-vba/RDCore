using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// What an assignment assigns to is a variable - or a property, or a function's own result - and never a constant, nor a function of a library
/// (<strong>MS-VBAL §5.4.3.8</strong>, <strong>§5.4.3.9</strong>, and <strong>§5.6.16.6</strong> for the target of an <c>LSet</c> or an <c>RSet</c>
/// statement and the control variable of a <c>For</c> loop): a module that assigns to one does not load, so it never runs. The target of a <c>Let</c>
/// statement may be a library's function whose declared type is a class, <c>Object</c> or <c>Variant</c> (<strong>§5.4.3.8</strong>). Nor is a constant
/// what a <c>ReDim</c> or an <c>Erase</c> statement writes to (<strong>§5.4.3.3</strong>, <strong>§5.4.3.4</strong>).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.8 Let Statement")]
[TestCategory("MS-VBAL 5.4.3.9 Set Statement")]
[TestCategory("MS-VBAL 5.4.3.6 LSet Statement")]
[TestCategory("MS-VBAL 5.4.3.7 RSet Statement")]
[TestCategory("MS-VBAL 5.4.2.3 For Statement")]
[TestCategory("MS-VBAL 5.4.2.4 For Each Statement")]
[TestCategory("MS-VBAL 5.6.16.6 Bound Variable Expressions")]
public sealed class AssignmentTargetTests
{
    private static string Program(string declarations, string body, bool optionExplicit = true)
        => $"Attribute VB_Name = \"Program\"\r\n{(optionExplicit ? "Option Explicit\r\n" : string.Empty)}{declarations}\r\nPublic Sub Main()\r\n{body}\r\nEnd Sub\r\n";

    // the module does not load, for the one error that says what the target is.
    private static Task AssertVariableRequiredAsync(string program, string what) => AssertTargetErrorAsync(program, "Variable required", what);

    private static async Task AssertTargetErrorAsync(string program, string error, string what)
    {
        var errors = await LoadErrorsAsync([], program);

        Assert.HasCount(1, errors, string.Join(" | ", errors));
        StringAssert.Contains(errors[0], error);
        StringAssert.Contains(errors[0], $"and this is {what}");
    }

    // a function named as the target of a Let or a Set is a call of it, and its own errors are MS-VBA's (FunctionTargetTests).
    [TestMethod]
    [DataRow("Private Const Label = \"abc\"", "LSet Label = \"x\"", "the constant Label", DisplayName = "The target of an LSet statement")]
    [DataRow("Private Const Label = \"abc\"", "RSet Label = \"x\"", "the constant Label", DisplayName = "The target of an RSet statement")]
    [DataRow("", "LSet Err = \"x\"", "the function Err of the VBA library", DisplayName = "The target of an LSet statement, a function of the library that returns an object")]
    [DataRow("", "RSet Err = \"x\"", "the function Err of the VBA library", DisplayName = "The target of an RSet statement, a function of the library that returns an object")]
    [DataRow("", "LSet Left = \"x\"", "the function Left of the VBA library", DisplayName = "The target of an LSet statement, a function of the library that returns a Variant")]
    [DataRow("", "For vbSunday = 1 To 2\r\nNext", "the constant vbSunday", DisplayName = "The control variable of a For statement")]
    [DataRow("", "For Left = 1 To 2\r\nNext", "the function Left of the VBA library", DisplayName = "The control variable of a For statement, a function of the library that returns a Variant")]
    [DataRow("Private Items As New Collection", "For Each vbSunday In Items\r\nNext", "the constant vbSunday", DisplayName = "The control variable of a For Each statement")]
    [DataRow("", "Mid$(Left$, 1, 1) = \"x\"", "the function Left$ of the VBA library", DisplayName = "The target of a Mid statement")]
    [DataRow("", "LSet vbCrLf = \"x\"", "the constant vbCrLf", DisplayName = "A constant of the Constants module, the target of an LSet statement")]
    [DataRow("Private Items As New Collection", "For Each vbCrLf In Items\r\nNext", "the constant vbCrLf", DisplayName = "A constant of the Constants module, the control variable of a For Each statement")]
    public async Task AnAssignment_ToWhatIsNotAVariable_IsAVariableRequiredError(string declarations, string body, string what)
        => await AssertVariableRequiredAsync(Program(declarations, body), what);

    // MS-VBA: "Assignment to constant not permitted" - a Let or a Set statement whose target is a constant (Excel-verified).
    [TestMethod]
    [DataRow("", "vbSunday = 5", "the constant vbSunday", DisplayName = "A constant of a library's Enum")]
    [DataRow("", "vbTextCompare = 5", "the constant vbTextCompare", DisplayName = "Another constant of a library's Enum")]
    [DataRow("Private Const Limit = 10", "Limit = 5", "the constant Limit", DisplayName = "A constant of the module")]
    [DataRow("", "Const Limit = 10\r\nLimit = 5", "the constant Limit", DisplayName = "A constant of the procedure")]
    [DataRow("Private Enum Colour\r\nRed\r\nEnd Enum", "Red = 5", "the constant Red", DisplayName = "A constant of the workspace's Enum")]
    [DataRow("", "VbDayOfWeek.vbSunday = 5", "the constant vbSunday", DisplayName = "A constant qualified by its Enum")]
    [DataRow("", "VBA.vbSunday = 5", "the constant vbSunday", DisplayName = "A constant qualified by its library")]
    [DataRow("Private Const Limit = 10", "Program.Limit = 5", "the constant Limit", DisplayName = "A constant qualified by its module")]
    [DataRow("Private Const Limit = 10", "Set Limit = Nothing", "the constant Limit", DisplayName = "The target of a Set assignment")]
    [DataRow("Private Const Left = \"c\"", "Left$ = \"x\"", "the constant Left", DisplayName = "A constant of the module named like a function of the library, by its name with a type character")]
    [DataRow("", "vbCrLf = 5", "the constant vbCrLf", DisplayName = "A constant of the Constants module")]
    [DataRow("", "Constants.vbCrLf = 5", "the constant vbCrLf", DisplayName = "A constant of the Constants module, qualified by its module")]
    [DataRow("", "VBA.vbCrLf = 5", "the constant vbCrLf", DisplayName = "A constant of the Constants module, qualified by its library")]
    public async Task AnAssignment_ToAConstant_IsAssignmentToConstantNotPermitted(string declarations, string body, string what)
        => await AssertTargetErrorAsync(Program(declarations, body), "Assignment to constant not permitted", what);

    // a ReDim of a constant is "Expected array" (MS-VBA, Excel-verified, oracle\s29 E17, E18): RedimTargetTests.
    [TestMethod]
    [TestCategory("MS-VBAL 5.4.3.4 Erase Statement")]
    [DataRow("Private Const Limit = 10", "Erase Limit", "the constant Limit", DisplayName = "An element of an Erase statement")]
    [DataRow("", "Erase vbSunday", "the constant vbSunday", DisplayName = "An element of an Erase statement, a constant of a library's Enum")]
    [DataRow("", "Erase vbCrLf", "the constant vbCrLf", DisplayName = "An element of an Erase statement, a constant of the Constants module")]
    [DataRow("", "Erase vbTab", "the constant vbTab", DisplayName = "An element of an Erase statement, another constant of the Constants module")]
    public async Task AnArrayStatement_OnAConstant_IsAVariableRequiredError(string declarations, string body, string what)
        => await AssertVariableRequiredAsync(Program(declarations, body), what);

    [TestMethod]
    [TestCategory("MS-VBAL 5.4.3.4 Erase Statement")]
    public async Task AnErase_OfAFunctionOfTheLibrary_ThatTakesNoArgument_IsNotRefused()
        // an element of an Erase statement may be a function (MS-VBAL §5.4.3.4), called with no argument: `Erase Left` is "Argument not optional"
        // (FunctionTargetTests); only whether the module loads is asserted.
        => CollectionAssert.AreEqual(Array.Empty<string>(), await LoadErrorsAsync([], Program(string.Empty, "Erase Now")));

    [TestMethod]
    [TestCategory("MS-VBAL 5.4.3.3 ReDim Statement")]
    public async Task AReDim_OfAFunctionOfTheLibrary_IsAVariableRequiredError()
        // the redimensioned variable is a variable (MS-VBAL §5.4.3.3), whatever the function's declared type. Qualified, since an unqualified name the
        // declaration pass does not see is declared as a local of its own.
        => await AssertVariableRequiredAsync(Program(string.Empty, "ReDim Strings.Left(1)"), "the function Left of the VBA library");

    [TestMethod]
    [DataRow("vbSunday = 5", "the constant vbSunday", DisplayName = "A constant of a library's Enum")]
    [DataRow("vbCrLf = 5", "the constant vbCrLf", DisplayName = "A constant of the Constants module")]
    public async Task AnAssignment_ToALibraryConstant_WithoutOptionExplicit_IsTheSameError(string body, string what)
        // without Option Explicit an undeclared name is a variable of its own, but the constant's name is declared.
        => await AssertTargetErrorAsync(Program(string.Empty, body, optionExplicit: false), "Assignment to constant not permitted", what);

    [TestMethod]
    public async Task TheError_SaysWhatTheTargetIs_AndCitesTheSpecification()
    {
        var errors = await LoadErrorsAsync([], Program(string.Empty, "vbSunday = 5"));

        CollectionAssert.AreEqual(
            new[] { "Assignment to constant not permitted: The target of a Let assignment must be a variable, and this is the constant vbSunday (MS-VBAL §5.4.3.8)." },
            errors);
    }

    [TestMethod]
    [DataRow("Private Function Five() As Long\r\nFive = 5\r\nEnd Function", "Debug.Print Five()", "5", DisplayName = "A function's own result")]
    [DataRow("Private Function Factorial(ByVal n As Long) As Long\r\nIf n <= 1 Then\r\nFactorial = 1\r\nElse\r\nFactorial = n * Factorial(n - 1)\r\nEnd If\r\nEnd Function",
        "Debug.Print Factorial(5)", "120", DisplayName = "The own result of a function that calls itself")]
    [DataRow("Public Property Get Level() As Long\r\nLevel = 7\r\nEnd Property", "Debug.Print Level", "7", DisplayName = "A property's own result")]
    [DataRow("", "Dim Left As String\r\nLeft = \"x\"\r\nDebug.Print Left", "x", DisplayName = "A variable named like a function of the library")]
    [DataRow("Private Sub Show(ByVal Left As String)\r\nLeft = Left & \"y\"\r\nDebug.Print Left\r\nEnd Sub", "Show \"x\"", "xy", DisplayName = "A parameter named like a function of the library")]
    [DataRow("", "Dim vbSunday As Long\r\nvbSunday = 3\r\nDebug.Print vbSunday", "3", DisplayName = "A variable named like a constant of the library")]
    [DataRow("Private vbCrLf As Long", "vbCrLf = 5\r\nDebug.Print vbCrLf", "5", DisplayName = "A variable of the module named like a constant of the Constants module")]
    [DataRow("", "Err.Number = 5\r\nDebug.Print Err.Number", "5", DisplayName = "A property of the object a function of the library returns")]
    [DataRow("", "Dim Left$\r\nLeft$ = \"x\"\r\nDebug.Print Left$", "x", DisplayName = "A variable declared with a type character, named like a function of the library")]
    public async Task AnAssignment_ToAVariableOrAResult_StillRuns(string declarations, string body, string expected)
        => CollectionAssert.AreEqual(new[] { expected }, await RunAsync([], Program(declarations, body)));

    [TestMethod]
    public async Task AnAssignment_ToAPropertyOfAClass_StillRuns()
    {
        var widget = ("Widget", ClassModule("Widget", "Option Explicit", "Private m As Long",
            "Public Property Get Size() As Long", "Size = m", "End Property",
            "Public Property Let Size(ByVal value As Long)", "m = value", "End Property"));

        var output = await RunAsync([widget], Program("Private w As New Widget", "w.Size = 3\r\nDebug.Print w.Size\r\nWith w\r\n.Size = 7\r\nEnd With\r\nDebug.Print w.Size"));

        CollectionAssert.AreEqual(new[] { "3", "7" }, output);
    }

    // MS-VBAL §5.4.3.9: a Set statement assigns a property through its Property Set, whether the property can be read or not - and a Let statement
    // through its Property Let.
    [TestMethod]
    [DataRow("Set w.Thing = w", "Widget 0", DisplayName = "Set, through a variable of the class")]
    [DataRow("With w\r\nSet .Thing = w\r\nEnd With", "Widget 0", DisplayName = "Set, through a With block")]
    [DataRow("Dim o As Object\r\nSet o = w\r\nSet o.Thing = w", "Widget 0", DisplayName = "Set, through an Object")]
    [DataRow("w.Size = 3", "Nothing 3", DisplayName = "Let, through a variable of the class")]
    [DataRow("Set w.Item(1) = w", "Widget 0", DisplayName = "Set, of a property with parameters")]
    [DataRow("w.Slot(2) = 3", "Nothing 3", DisplayName = "Let, of a property with parameters")]
    public async Task AnAssignment_ToAPropertyThatCannotBeRead_IsAssignedThroughItsSetter(string body, string expected)
    {
        var widget = ("Widget", ClassModule("Widget", "Option Explicit", "Private m As Object", "Private n As Long",
            "Public Property Set Thing(ByVal value As Object)", "Set m = value", "End Property",
            "Public Property Let Size(ByVal value As Long)", "n = value", "End Property",
            "Public Property Set Item(ByVal i As Long, ByVal value As Object)", "Set m = value", "End Property",
            "Public Property Let Slot(ByVal i As Long, ByVal value As Long)", "n = value", "End Property",
            "Public Function Held() As String", "Held = TypeName(m) & \" \" & n", "End Function"));

        var output = await RunAsync([widget], Program("Private w As New Widget", body + "\r\nDebug.Print w.Held"));

        CollectionAssert.AreEqual(new[] { expected }, output);
    }

    // a Let of a property that can't be read is coerced to its Property Let's value parameter: an object with no default member, given to a Long, compiles
    // as it does for a variable declared Long, and fails only when it runs.
    [TestMethod]
    [DataRow("w.Size = w", DisplayName = "Let, an object")]
    [DataRow("w.Slot(2) = w", DisplayName = "Let, an object, of a property with parameters")]
    public async Task ALet_OfAnObject_ToAPropertyThatCannotBeRead_Compiles(string body)
    {
        var widget = ("Widget", ClassModule("Widget", "Option Explicit", "Private n As Long",
            "Public Property Let Size(ByVal value As Long)", "n = value", "End Property",
            "Public Property Let Slot(ByVal i As Long, ByVal value As Long)", "n = value", "End Property"));

        var errors = await LoadErrorsAsync([widget], Program("Private w As New Widget", body));

        Assert.IsEmpty(errors, string.Join(" | ", errors));
    }

    // what is assigned is coerced to the value parameter of the accessor that is called: a number is no object for a Property Set to be given, as it is none
    // for a variable declared Object.
    [TestMethod]
    [DataRow("Set w.Thing = 5", DisplayName = "Set, a number")]
    public async Task AnAssignment_ToAPropertyThatCannotBeRead_IsCoercedToItsSettersValue(string body)
    {
        var widget = ("Widget", ClassModule("Widget", "Option Explicit", "Private m As Object", "Private n As Long",
            "Public Property Set Thing(ByVal value As Object)", "Set m = value", "End Property",
            "Public Property Let Size(ByVal value As Long)", "n = value", "End Property"));

        var errors = await LoadErrorsAsync([widget], Program("Private w As New Widget", body));

        Assert.HasCount(1, errors, string.Join(" | ", errors));
        StringAssert.Contains(errors[0], "Type mismatch");
    }

    [TestMethod]
    public async Task AnAssignment_ToAnObject_StillAssignsItsDefaultMember()
    {
        var cell = ("Cell", ClassModule("Cell", "Option Explicit", "Private m As Long",
            "Public Property Get Value() As Long", "Attribute Value.VB_UserMemId = 0", "Value = m", "End Property",
            "Public Property Let Value(ByVal v As Long)", "Attribute Value.VB_UserMemId = 0", "m = v", "End Property"));

        var output = await RunAsync([cell], Program("Private c As New Cell", "c = 5\r\nDebug.Print c.Value"));

        CollectionAssert.AreEqual(new[] { "5" }, output);
    }

    // MS-VBAL §5.6.13: an object indexed is its default member given the arguments, and one assigned is too - through its Property Let, or its Property Set.
    [TestMethod]
    public async Task AnAssignment_ToAnIndexedObject_AssignsItsDefaultMember()
    {
        var cells = ("Cells", ClassModule("Cells", "Option Explicit", "Private m(1 To 2) As Variant",
            "Public Property Get Item(ByVal i As Long) As Variant", "Attribute Item.VB_UserMemId = 0", "If IsObject(m(i)) Then Set Item = m(i) Else Item = m(i)", "End Property",
            "Public Property Let Item(ByVal i As Long, ByVal v As Variant)", "Attribute Item.VB_UserMemId = 0", "m(i) = v", "End Property",
            "Public Property Set Item(ByVal i As Long, ByVal v As Object)", "Attribute Item.VB_UserMemId = 0", "Set m(i) = v", "End Property"));

        var output = await RunAsync([cells], Program("Private c As New Cells",
            "c(1) = 5\r\nDebug.Print c(1)\r\nSet c(2) = c\r\nDebug.Print TypeName(c(2))\r\nDim o As Object\r\nSet o = c\r\no(1) = 7\r\nDebug.Print c.Item(1)"));

        CollectionAssert.AreEqual(new[] { "5", "Cells", "7" }, output);
    }

    [TestMethod]
    [DataRow("", "Err = 5", DisplayName = "A function of the library that returns an object, whose default member is assigned")]
    [DataRow("", "Command = \"x\"", DisplayName = "A function of the library that returns a Variant, and takes no argument")]
    [DataRow("", "Array = 5", DisplayName = "Another function of the library that returns a Variant")]
    [DataRow("", "Interaction.Command = \"x\"", DisplayName = "A function of the library that returns a Variant, qualified by its module")]
    [DataRow("Private m As Long\r\nPublic Property Get Level() As Long\r\nLevel = m\r\nEnd Property\r\nPublic Property Let Level(ByVal v As Long)\r\nm = v\r\nEnd Property",
        "Level = 5", DisplayName = "A property of the module, by its bare name")]
    public async Task AnAssignment_ThatMayBeValid_IsNotRefused(string declarations, string body)
        // only whether the module loads is asserted: these are not refused for what they assign to. MS-VBAL §5.4.3.8 admits a Let to a function whose
        // declared type is a class, Object or Variant, which is called and assigned through when it runs (FunctionTargetTests); the property of the
        // module is assigned through its Property Let.
        => CollectionAssert.AreEqual(Array.Empty<string>(), await LoadErrorsAsync([], Program(declarations, body)));

    [TestMethod]
    public async Task AnAssignment_ByATypedName_WhoseIdentifierIsAmbiguous_IsNotJudgedAsTheLibrarysFunction()
    {
        // Left names two declarations, so which one Left$ is, is not known: it is not taken for the library's Left$.
        var mod2 = ("Mod2", StandardModule("Mod2", "Option Explicit", "Public Left As String"));
        var mod3 = ("Mod3", StandardModule("Mod3", "Option Explicit", "Public Left As String"));

        var errors = await LoadErrorsAsync([], [mod2, mod3], Program(string.Empty, "Left$ = \"x\""));

        Assert.IsFalse(errors.Any(error => error.Contains("Variable required", StringComparison.Ordinal)), string.Join(" | ", errors));
    }

    [TestMethod]
    [DataRow("Public Type Rect\r\nLeft As Long\r\nEnd Type\r\nPublic Strings As Rect", DisplayName = "A record of another module named like the Strings module")]
    [DataRow("Public Strings As New Widget", DisplayName = "An object of another module named like the Strings module")]
    public async Task AnAssignment_ToAMemberOfAVariableNamedLikeALibrarysModule_StillRuns(string declarations)
    {
        // the variable is what the name means (MS-VBAL §5.6.10), not the library's module, so Strings.Left is its member and not the library's function.
        var widget = ("Widget", ClassModule("Widget", "Option Explicit", "Public Left As Long"));
        var mod2 = ("Mod2", StandardModule("Mod2", "Option Explicit", declarations));

        CollectionAssert.AreEqual(new[] { "5" }, await RunAsync([widget], [mod2], Program(string.Empty, "Strings.Left = 5\r\nDebug.Print Strings.Left")));
    }

    [TestMethod]
    [DataRow("Private Function Left$(ByVal s As String)\r\nLeft$ = \"L\" & s\r\nEnd Function", "",
        DisplayName = "The own result of a function declared with a type character, named like a function of the library")]
    [DataRow("Private Function Left(ByVal s As String) As String\r\nLeft$ = \"L\" & s\r\nEnd Function", "",
        DisplayName = "The own result of a function named like a function of the library, by its name with a type character")]
    [DataRow("", "Dim Left$\r\nLeft$ = \"x\"", DisplayName = "A variable declared with a type character, named like a function of the library")]
    [DataRow("", "Dim Trim$\r\nTrim$ = \"x\"", DisplayName = "A variable declared with a type character, named like another function of the library")]
    [DataRow("", "Dim Left As String\r\nLeft$ = \"x\"", DisplayName = "A variable named like a function of the library, by its name with a type character")]
    [DataRow("Private Left$", "Left$ = \"x\"", DisplayName = "A variable of the module declared with a type character, named like a function of the library")]
    [DataRow("Private Sub Show(ByVal Left$)\r\nLeft$ = Left$ & \"y\"\r\nEnd Sub", "Show \"x\"",
        DisplayName = "A parameter declared with a type character, named like a function of the library")]
    public async Task AnAssignment_ByATypedName_ToWhatItsIdentifierDeclares_IsNotRefused(string declarations, string body)
        // a name is matched by its identifier, and its type character judged after (MS-VBAL §5.6.10): Left$ is what Left declares there, not the library's
        // member named with the character. Only whether the module loads is asserted.
        => CollectionAssert.AreEqual(Array.Empty<string>(), await LoadErrorsAsync([], Program(declarations, body)));

    [TestMethod]
    public async Task AnAssignment_ToTheOwnResultOfAFunctionOfAClass_ByItsTypedName_IsNotRefused()
    {
        var widget = ("Widget", ClassModule("Widget", "Option Explicit", "Public Function Left$()", "Left$ = \"c\"", "End Function"));

        CollectionAssert.AreEqual(Array.Empty<string>(), await LoadErrorsAsync([widget], Program("Private w As New Widget", "Debug.Print w.Left")));
    }

    [TestMethod]
    public async Task AnAssignment_ToAPropertyOfAClass_ByItsBareName_IsNotRefused()
    {
        var widget = ("Widget", ClassModule("Widget", "Option Explicit", "Private m As Long",
            "Public Property Get Size() As Long", "Size = m", "End Property",
            "Public Property Let Size(ByVal value As Long)", "m = value", "End Property",
            "Public Sub Grow()", "Size = Size + 1", "End Sub"));

        CollectionAssert.AreEqual(Array.Empty<string>(), await LoadErrorsAsync([widget], Program("Private w As New Widget", "w.Grow")));
    }
}
