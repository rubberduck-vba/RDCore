using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// An Enum named in an <c>As</c> clause means the declared type <c>Long</c> (<strong>MS-VBAL §5.2.3.4</strong>), so where two declared types are matched
/// an Enum and <c>Long</c>, or two Enums, are the same one: an implemented name declaration and its interface member (§5.3.1.9), an event handler and its
/// event (§5.3.1.8), and a <c>ByRef</c> parameter and the variable it is given (§5.3.1.11), by a call or by a <c>RaiseEvent</c> (§5.4.2.20). Run through
/// <see cref="ModuleWorkspace"/>, so the declared types are the ones the host makes.
/// </summary>
/// <remarks>
/// <c>VbDayOfWeek</c> and <c>VbCompareMethod</c> are the standard library's Enums; <c>Colours</c>, <c>Colour</c> and <c>Kind</c> are the workspace's.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.2.3.4 Enum Declarations")]
public sealed class EnumDeclaredTypeMatchingTests
{
    private static string Program(string declarations, params string[] body)
        => $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n{declarations}\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    #region Implements

    private static (string Name, string Source)[] Implementing(string interfaceBody, params string[] shapeBody)
        => [("IShape", ClassModule("IShape", "Option Explicit", interfaceBody)), ("Shape", ClassModule("Shape", ["Option Explicit", "Implements IShape", .. shapeBody]))];

    [TestMethod]
    [DataRow("Public Sub Paint(ByVal d As VbDayOfWeek)\r\nEnd Sub", "Private Sub IShape_Paint(ByVal d As Long)\r\nDebug.Print d\r\nEnd Sub", "s.Paint 2",
        DisplayName = "A parameter declared Long, of an Enum")]
    [DataRow("Public Sub Paint(ByVal n As Long)\r\nEnd Sub", "Private Sub IShape_Paint(ByVal n As VbDayOfWeek)\r\nDebug.Print n\r\nEnd Sub", "s.Paint 2",
        DisplayName = "A parameter declared an Enum, of Long")]
    [DataRow("Public Sub Paint(ByVal d As VbDayOfWeek)\r\nEnd Sub", "Private Sub IShape_Paint(ByVal d As VbCompareMethod)\r\nDebug.Print d\r\nEnd Sub", "s.Paint 2",
        DisplayName = "A parameter declared an Enum, of another Enum")]
    [DataRow("Public Function Pick() As VbDayOfWeek\r\nEnd Function", "Private Function IShape_Pick() As Long\r\nIShape_Pick = 2\r\nEnd Function", "Debug.Print s.Pick",
        DisplayName = "A result declared Long, of an Enum")]
    [DataRow("Public Day As VbDayOfWeek", "Private Property Get IShape_Day() As Long\r\nIShape_Day = 2\r\nEnd Property\r\nPrivate Property Let IShape_Day(ByVal v As Long)\r\nEnd Property", "Debug.Print s.Day",
        DisplayName = "A variable implemented by properties declared Long, of an Enum")]
    public async Task AnImplementation_ThatDeclaresLongWhereTheInterfaceDeclaresAnEnum_OrTheOtherWayRound_ImplementsIt(string interfaceBody, string implementation, string call)
    {
        // the implemented name declaration was refused when the module loaded (VBC09330): its declared type was compared by name.
        CollectionAssert.AreEqual(new[] { "2" }, await RunAsync(Implementing(interfaceBody, implementation), Program("", "Dim s As IShape", "Set s = New Shape", call)));
    }

    // the interface class declares the Enum, so the host has defined it before the class that implements the interface: ModuleWorkspace defines the
    // class modules first, and a class's member of a standard module's Enum would have no type yet.
    private const string InterfaceEnums = "Public Enum Colours\r\ncRed = 1\r\ncBlue = 2\r\nEnd Enum\r\nPublic Enum Kind\r\nkOne = 1\r\nEnd Enum";

    [TestMethod]
    [DataRow("Public Sub Paint(ByVal c As Colours)\r\nEnd Sub", "Private Sub IShape_Paint(ByVal c As Long)\r\nDebug.Print c\r\nEnd Sub", "s.Paint 2",
        DisplayName = "A parameter declared Long, of the workspace's Enum")]
    [DataRow("Public Sub Paint(ByVal n As Long)\r\nEnd Sub", "Private Sub IShape_Paint(ByVal n As Colours)\r\nDebug.Print n\r\nEnd Sub", "s.Paint 2",
        DisplayName = "A parameter declared the workspace's Enum, of Long")]
    [DataRow("Public Sub Paint(ByVal c As Colours)\r\nEnd Sub", "Private Sub IShape_Paint(ByVal c As Kind)\r\nDebug.Print c\r\nEnd Sub", "s.Paint 2",
        DisplayName = "A parameter declared the workspace's Enum, of another of its Enums")]
    [DataRow("Public Function Pick() As Colours\r\nEnd Function", "Private Function IShape_Pick() As Long\r\nIShape_Pick = 2\r\nEnd Function", "Debug.Print s.Pick",
        DisplayName = "A result declared Long, of the workspace's Enum")]
    [DataRow("Public Col As Colours", "Private Property Get IShape_Col() As Long\r\nIShape_Col = 2\r\nEnd Property\r\nPrivate Property Let IShape_Col(ByVal v As Long)\r\nEnd Property", "Debug.Print s.Col",
        DisplayName = "A variable implemented by properties declared Long, of the workspace's Enum")]
    public async Task AnImplementation_ThatDeclaresLongWhereTheInterfaceDeclaresAnEnumOfTheWorkspace_OrTheOtherWayRound_ImplementsIt(
        string interfaceBody, string implementation, string call)
    {
        // the host gives an Enum of the workspace its type, and comparing it by name with Long refused each of these when the module loaded.
        CollectionAssert.AreEqual(new[] { "2" }, await RunAsync(Implementing($"{InterfaceEnums}\r\n{interfaceBody}", implementation),
            Program("", "Dim s As IShape", "Set s = New Shape", call)));
    }

    [TestMethod]
    public async Task AnImplementation_ThatDeclaresIntegerWhereTheInterfaceDeclaresAnEnum_IsInvalid()
    {
        // an Enum is a Long, and an Integer is not.
        var errors = await LoadErrorsAsync(
            Implementing("Public Sub Paint(ByVal d As VbDayOfWeek)\r\nEnd Sub", "Private Sub IShape_Paint(ByVal d As Integer)\r\nEnd Sub"),
            Program("", "Dim s As IShape"));

        StringAssert.Contains(errors.Single(), "parameter 1 is a Integer and a VbDayOfWeek is required");
    }

    [TestMethod]
    [DataRow("Integer", DisplayName = "Declared Integer, of the workspace's Enum")]
    [DataRow("String", DisplayName = "Declared String, of the workspace's Enum")]
    public async Task AnImplementation_ThatDeclaresAnotherTypeWhereTheInterfaceDeclaresAnEnumOfTheWorkspace_IsInvalid(string type)
    {
        // an Enum of the workspace is a Long here, as a library's Enum is, and an Integer or a String is not.
        var errors = await LoadErrorsAsync(
            Implementing($"{InterfaceEnums}\r\nPublic Sub Paint(ByVal c As Colours)\r\nEnd Sub", $"Private Sub IShape_Paint(ByVal c As {type})\r\nEnd Sub"),
            Program("", "Dim s As IShape"));

        StringAssert.Contains(errors.Single(), $"parameter 1 is a {type} and a Colours is required");
    }

    #endregion

    #region Events

    private static (string Name, string Source)[] Handling(string eventParameter, string handlerParameter)
        =>
        [
            ("Widget", ClassModule("Widget", "Option Explicit", InterfaceEnums, $"Public Event Changed({eventParameter})", "Public Sub Fire()", "RaiseEvent Changed(2)", "End Sub")),
            ("Listener", ClassModule("Listener", "Option Explicit", "Private WithEvents w As Widget", "Public Sub Go()", "Set w = New Widget", "w.Fire", "End Sub",
                $"Private Sub w_Changed({handlerParameter})", "Debug.Print v", "End Sub")),
        ];

    [TestMethod]
    [DataRow("ByVal d As VbDayOfWeek", "ByVal v As Long", DisplayName = "Declared Long, of an Enum")]
    [DataRow("ByVal n As Long", "ByVal v As VbDayOfWeek", DisplayName = "Declared an Enum, of Long")]
    [DataRow("ByVal d As VbDayOfWeek", "ByVal v As VbCompareMethod", DisplayName = "Declared an Enum, of another Enum")]
    [DataRow("ByVal c As Colours", "ByVal v As Long", DisplayName = "Declared Long, of the workspace's Enum")]
    [DataRow("ByVal n As Long", "ByVal v As Colours", DisplayName = "Declared the workspace's Enum, of Long")]
    [DataRow("ByVal c As Colours", "ByVal v As Kind", DisplayName = "Declared the workspace's Enum, of another of its Enums")]
    public async Task AHandler_ThatDeclaresLongWhereTheEventDeclaresAnEnum_OrTheOtherWayRound_HandlesIt(string eventParameter, string handlerParameter)
    {
        // the handler was refused when the module loaded (VBC09326): its parameter's declared type was compared by name.
        CollectionAssert.AreEqual(new[] { "2" }, await RunAsync(Handling(eventParameter, handlerParameter), Program("", "Dim l As New Listener", "l.Go")));
    }

    [TestMethod]
    public async Task AHandler_ThatDeclaresIntegerWhereTheEventDeclaresAnEnum_IsInvalid()
    {
        var errors = await LoadErrorsAsync(Handling("ByVal d As VbDayOfWeek", "ByVal v As Integer"), Program("", "Dim l As New Listener"));

        StringAssert.Contains(errors.Single(), "parameter 1 is a Integer and a VbDayOfWeek is required");
    }

    [TestMethod]
    [DataRow("Integer", DisplayName = "Declared Integer, of the workspace's Enum")]
    [DataRow("String", DisplayName = "Declared String, of the workspace's Enum")]
    public async Task AHandler_ThatDeclaresAnotherTypeWhereTheEventDeclaresAnEnumOfTheWorkspace_IsInvalid(string type)
    {
        var errors = await LoadErrorsAsync(Handling("ByVal c As Colours", $"ByVal v As {type}"), Program("", "Dim l As New Listener"));

        StringAssert.Contains(errors.Single(), $"parameter 1 is a {type} and a Colours is required");
    }

    #endregion

    #region ByRef

    private const string SetLong = "Private Sub SetL(ByRef n As Long)\r\nn = 2\r\nEnd Sub";

    private const string SetDay = "Private Sub SetD(ByRef d As VbDayOfWeek)\r\nd = 2\r\nEnd Sub";

    private const string ProgramEnums = "Private Enum Colour\r\ncRed = 1\r\ncBlue = 2\r\nEnd Enum\r\nPrivate Enum Kind\r\nkOne = 1\r\nEnd Enum";

    private const string SetColour = "Private Sub SetC(ByRef c As Colour)\r\nc = 2\r\nEnd Sub";

    [TestMethod]
    [DataRow(SetLong, "Dim d As VbDayOfWeek\r\nSetL d\r\nDebug.Print d", DisplayName = "A variable of an Enum, given to a Long parameter")]
    [DataRow(SetDay, "Dim n As Long\r\nSetD n\r\nDebug.Print n", DisplayName = "A Long variable, given to a parameter of an Enum")]
    [DataRow(SetDay, "Dim m As VbCompareMethod\r\nSetD m\r\nDebug.Print m", DisplayName = "A variable of an Enum, given to a parameter of another Enum")]
    [DataRow($"{ProgramEnums}\r\n{SetLong}", "Dim c As Colour\r\nSetL c\r\nDebug.Print c", DisplayName = "A variable of the workspace's Enum, given to a Long parameter")]
    [DataRow($"{ProgramEnums}\r\n{SetColour}", "Dim n As Long\r\nSetC n\r\nDebug.Print n", DisplayName = "A Long variable, given to a parameter of the workspace's Enum")]
    [DataRow($"{ProgramEnums}\r\n{SetColour}", "Dim k As Kind\r\nSetC k\r\nDebug.Print k", DisplayName = "A variable of the workspace's Enum, given to a parameter of another of its Enums")]
    public async Task AVariable_PassedToAByRefParameterOfTheOtherOfAnEnumAndLong_IsTheVariable(string procedure, string body)
    {
        // the variable was copied, and what the procedure assigned was lost (0 was printed).
        CollectionAssert.AreEqual(new[] { "2" }, await RunAsync([], Program(procedure, body)));
    }

    [TestMethod]
    [DataRow(SetLong, "Public d As VbDayOfWeek", "SetL o.d\r\nDebug.Print o.d", DisplayName = "An object's variable of an Enum, given to a Long parameter")]
    [DataRow(SetDay, "Public n As Long", "SetD o.n\r\nDebug.Print o.n", DisplayName = "An object's Long variable, given to a parameter of an Enum")]
    public async Task AnObjectsVariable_PassedToAByRefParameterOfTheOtherOfAnEnumAndLong_IsTheVariable(string procedure, string field, string body)
        => CollectionAssert.AreEqual(new[] { "2" }, await RunAsync(
            [("Class1", ClassModule("Class1", "Option Explicit", field))], Program(procedure, $"Dim o As New Class1\r\n{body}")));

    [TestMethod]
    public async Task AValueOutsideALong_AssignedThroughTheParameter_IsAnOverflow_AndLeavesTheVariableAsItWas()
        => CollectionAssert.AreEqual(new[] { "6", "3" }, await RunAsync([], Program(
            "Private Sub SetL(ByRef n As Long)\r\nn = 2147483648#\r\nEnd Sub",
            "Dim d As VbDayOfWeek", "d = 3", "On Error Resume Next", "SetL d", "Debug.Print Err.Number", "Debug.Print d")));

    [TestMethod]
    public async Task AnIntegerVariable_PassedToAByRefLongParameter_IsAByRefArgumentTypeMismatch()
        // only an Enum and Long are one declared type: an Integer variable is no variable a ByRef Long parameter can be a name for (MS-VBA, Excel-verified).
        => StringAssert.Contains(string.Join(" | ", await LoadErrorsAsync([], Program(SetLong, "Dim i As Integer", "SetL i", "Debug.Print i"))),
            "ByRef argument type mismatch");

    [TestMethod]
    public async Task AVariableOfAnEnum_PassedToAByRefVariantParameter_IsAssignedAsTheEnumItIs()
        // a Variant parameter is bound to the variable and assigned as its declared type (MS-VBAL §5.3.1.11): a Long, so 2.5 rounds to the even 2.
        => CollectionAssert.AreEqual(new[] { "2" }, await RunAsync([], Program(
            "Private Sub Assign(ByRef v As Variant)\r\nv = 2.5\r\nEnd Sub", "Dim d As VbDayOfWeek", "Assign d", "Debug.Print d")));

    // the event's ByRef parameter is bound to the variable the RaiseEvent gives it, and the handler assigns 2 through its own parameter.
    private static (string Name, string Source)[] Raising(string parameterType, string variableType)
        =>
        [
            ("Widget", ClassModule("Widget", "Option Explicit", InterfaceEnums, $"Public Event Changed(ByRef n As {parameterType})", "Public Sub Fire()",
                $"Dim x As {variableType}", "RaiseEvent Changed(x)", "Debug.Print x", "End Sub")),
            ("Listener", ClassModule("Listener", "Option Explicit", "Private WithEvents w As Widget", "Public Sub Go()", "Set w = New Widget", "w.Fire", "End Sub",
                $"Private Sub w_Changed(ByRef v As {parameterType})", "v = 2", "End Sub")),
        ];

    [TestMethod]
    [DataRow("Long", "VbDayOfWeek", DisplayName = "A variable of an Enum, raised to a Long parameter")]
    [DataRow("VbDayOfWeek", "Long", DisplayName = "A Long variable, raised to a parameter of an Enum")]
    [DataRow("VbDayOfWeek", "VbCompareMethod", DisplayName = "A variable of an Enum, raised to a parameter of another Enum")]
    [DataRow("Long", "Colours", DisplayName = "A variable of the workspace's Enum, raised to a Long parameter")]
    [DataRow("Colours", "Long", DisplayName = "A Long variable, raised to a parameter of the workspace's Enum")]
    [DataRow("Colours", "Kind", DisplayName = "A variable of the workspace's Enum, raised to a parameter of another of its Enums")]
    public async Task AVariable_RaisedToAByRefEventParameterOfTheOtherOfAnEnumAndLong_IsTheVariable(string parameterType, string variableType)
    {
        // the RaiseEvent was refused when the module loaded (VBC09323): the variable's declared type was compared by name with the parameter's.
        CollectionAssert.AreEqual(new[] { "2" }, await RunAsync(Raising(parameterType, variableType), Program("", "Dim l As New Listener", "l.Go")));
    }

    [TestMethod]
    public async Task AnIntegerVariable_RaisedToAByRefLongEventParameter_IsInvalid()
    {
        var errors = await LoadErrorsAsync(Raising("Long", "Integer"), Program("", "Dim l As New Listener"));

        StringAssert.Contains(errors.Single(), "a ByRef parameter of type Long takes a variable of exactly that type, and the argument is a Integer");
    }

    #endregion
}
