using static RDCore.Tests.Runtime.StdLib.Information.InformationWorkspace;

namespace RDCore.Tests.Runtime.StdLib.Information;

/// <summary>
/// <strong>MS-VBAL §6.1.2.7.1.12 TypeName</strong> and <strong>§6.1.2.7.1.13 VarType</strong>, given every kind of value a program has:
/// literals, conversions, operator results, constants, variables of each declared type, arrays, objects and their default members,
/// parameters, and the results of other functions.
/// </summary>
/// <remarks>
/// Each row is what MS-VBA printed for the same source, in Excel (64-bit). A row that RD-VBA does not reproduce yet because something
/// other than these two functions is missing is a test of its own, ignored with the reason.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.7 Information")]
public sealed class TypeNameAndVarTypeTests
{
    private static async Task AssertTypeOf(string declarations, string setup, string expression, string typeName, string varType)
    {
        var (actualName, actualVarType) = await TypeOf(declarations, expression, setup.Length == 0 ? [] : [setup]);
        Assert.AreEqual(typeName, actualName, $"TypeName({expression})");
        Assert.AreEqual(varType, actualVarType, $"VarType({expression})");
    }

    #region literals

    [TestMethod]
    [DataRow("1", "Integer", "2")]
    [DataRow("32767", "Integer", "2")]
    [DataRow("32768", "Long", "3")]
    // unary minus applies to the literal 32768, which is a Long.
    [DataRow("-32768", "Long", "3")]
    [DataRow("2147483647", "Long", "3")]
    // a decimal literal that does not fit a Long is a Double, not a LongLong.
    [DataRow("2147483648", "Double", "5")]
    [DataRow("&H8000", "Integer", "2")]
    [DataRow("&HFFFF", "Integer", "2")]
    [DataRow("&H8000&", "Long", "3")]
    [DataRow("&O17", "Integer", "2")]
    [DataRow("1&", "Long", "3")]
    [DataRow("1!", "Single", "4")]
    [DataRow("1#", "Double", "5")]
    [DataRow("1@", "Currency", "6")]
    [DataRow("1^", "LongLong", "20")]
    [DataRow("1.5", "Double", "5")]
    [DataRow("1E3", "Double", "5")]
    [DataRow("\"a\"", "String", "8")]
    [DataRow("\"\"", "String", "8")]
    [DataRow("True", "Boolean", "11")]
    [DataRow("#1/2/2000#", "Date", "7")]
    [DataRow("#12:30:00 PM#", "Date", "7")]
    [DataRow("Empty", "Empty", "0")]
    [DataRow("Null", "Null", "1")]
    [DataRow("Nothing", "Nothing", "9")]
    public Task OfALiteral(string expression, string typeName, string varType)
        => AssertTypeOf(string.Empty, string.Empty, expression, typeName, varType);

    #endregion

    #region conversions

    [TestMethod]
    [DataRow("CByte(1)", "Byte", "17")]
    [DataRow("CInt(1)", "Integer", "2")]
    [DataRow("CLng(1)", "Long", "3")]
    [DataRow("CLngLng(1)", "LongLong", "20")]
    [DataRow("CSng(1)", "Single", "4")]
    [DataRow("CDbl(1)", "Double", "5")]
    [DataRow("CCur(1)", "Currency", "6")]
    [DataRow("CDec(1)", "Decimal", "14")]
    [DataRow("CDate(1)", "Date", "7")]
    [DataRow("CStr(1)", "String", "8")]
    [DataRow("CBool(1)", "Boolean", "11")]
    [DataRow("CVErr(5)", "Error", "10")]
    // CVar returns its argument: the Variant holds the Integer it was given.
    [DataRow("CVar(1)", "Integer", "2")]
    [DataRow("CVDate(1)", "Date", "7")]
    public Task OfAConversion(string expression, string typeName, string varType)
        => AssertTypeOf(string.Empty, string.Empty, expression, typeName, varType);

    [TestMethod]
    [Ignore("CLngPtr is not implemented: StdConversion.CLngPtr returns NotImplemented (nothing in the runtime models a LongPtr value yet).")]
    public Task OfCLngPtr_IsTheLongLongItIsOnA64BitPlatform()
        => AssertTypeOf(string.Empty, string.Empty, "CLngPtr(1)", "LongLong", "20");

    #endregion

    #region operator results

    [TestMethod]
    [DataRow("1 + 1", "Integer", "2")]
    [DataRow("1 / 2", "Double", "5")]
    [DataRow("5 \\ 2", "Integer", "2")]
    [DataRow("5 Mod 2", "Integer", "2")]
    [DataRow("2 ^ 2", "Double", "5")]
    [DataRow("\"a\" & 1", "String", "8")]
    [DataRow("1 & 1", "String", "8")]
    // negating a Byte cannot stay a Byte; Not of one can.
    [DataRow("-CByte(1)", "Integer", "2")]
    [DataRow("Not CByte(1)", "Byte", "17")]
    [DataRow("CByte(1) + CByte(1)", "Byte", "17")]
    [DataRow("True + True", "Integer", "2")]
    [DataRow("CInt(1) * CLng(1)", "Long", "3")]
    // a Single and a Long make a Double; a Single and an Integer stay a Single.
    [DataRow("CSng(1) + CLng(1)", "Double", "5")]
    [DataRow("CSng(1) + CInt(1)", "Single", "4")]
    [DataRow("CCur(1) + CDbl(1)", "Currency", "6")]
    [DataRow("CDec(1) + CDbl(1)", "Decimal", "14")]
    [DataRow("#1/2/2000# + 1", "Date", "7")]
    [DataRow("\"1\" + 1", "Double", "5")]
    [DataRow("\"1\" + \"1\"", "String", "8")]
    [DataRow("1 = 1", "Boolean", "11")]
    [DataRow("Null = 1", "Null", "1")]
    [DataRow("\"a\" Like \"a\"", "Boolean", "11")]
    [DataRow("Nothing Is Nothing", "Boolean", "11")]
    [DataRow("(1)", "Integer", "2")]
    public Task OfAnOperatorResult(string expression, string typeName, string varType)
        => AssertTypeOf(string.Empty, string.Empty, expression, typeName, varType);

    [TestMethod]
    [Ignore("Not of a Boolean evaluates to an Integer: the Not operator's effective type for a Boolean operand is wrong (MS-VBAL 5.6.9.8.1 keeps it Boolean).")]
    public Task OfNotTrue_IsABoolean()
        => AssertTypeOf(string.Empty, string.Empty, "Not True", "Boolean", "11");

    [TestMethod]
    [Ignore("Date - Date evaluates to a Date: the subtraction operator's effective type for two Date operands is wrong (MS-VBAL 5.6.9.3.3 makes it a Double).")]
    public Task OfADateLessADate_IsADouble()
        => AssertTypeOf(string.Empty, string.Empty, "#1/2/2000# - #1/1/2000#", "Double", "5");

    [TestMethod]
    [Ignore("Null + 1 does not load: the static semantics of + reject a Null literal operand (MS-VBAL 5.6.9.3 gives it the declared type Variant).")]
    public Task OfNullPlusOne_IsNull()
        => AssertTypeOf(string.Empty, string.Empty, "Null + 1", "Null", "1");

    [TestMethod]
    [Ignore("Empty + 1 does not load: the static semantics of + reject an Empty literal operand (MS-VBAL 5.6.9.3 gives it the declared type Variant).")]
    [DataRow("Empty + 1", "Integer", "2")]
    [DataRow("Empty + Empty", "Integer", "2")]
    [DataRow("Empty + 1.5", "Double", "5")]
    public Task OfEmptyAndANumber(string expression, string typeName, string varType)
        => AssertTypeOf(string.Empty, string.Empty, expression, typeName, varType);

    #endregion

    #region constants

    [TestMethod]
    [DataRow("Private Const C = 1", "Integer", "2")]
    [DataRow("Private Const C = 1.5", "Double", "5")]
    [DataRow("Private Const C = \"x\"", "String", "8")]
    [DataRow("Private Const C = #1/2/2000#", "Date", "7")]
    public Task OfAModuleConstant(string declaration, string typeName, string varType)
        => AssertTypeOf(declaration, string.Empty, "C", typeName, varType);

    [TestMethod]
    [Ignore("A Const declared As a type keeps the type of its value: the fold does not Let-coerce the value to the declared type (MS-VBAL 5.2.3.2).")]
    public Task OfATypedConstant_IsItsDeclaredType()
        => AssertTypeOf("Private Const C As Long = 1", string.Empty, "C", "Long", "3");

    [TestMethod]
    [DataRow("vbLong", "Long", "3")]
    [DataRow("vbTrue", "Long", "3")]
    public Task OfALibraryEnumMember(string expression, string typeName, string varType)
        => AssertTypeOf(string.Empty, string.Empty, expression, typeName, varType);

    [TestMethod]
    [Ignore("vbNullString and vbCrLf are not defined: the standard library's Constants module has no symbols yet.")]
    [DataRow("vbNullString", "String", "8")]
    [DataRow("vbCrLf", "String", "8")]
    public Task OfALibraryConstant(string expression, string typeName, string varType)
        => AssertTypeOf(string.Empty, string.Empty, expression, typeName, varType);

    #endregion

    #region variables

    [TestMethod]
    [DataRow("Private x As Byte", "", "Byte", "17")]
    [DataRow("Private x As Integer", "", "Integer", "2")]
    [DataRow("Private x As Long", "", "Long", "3")]
    [DataRow("Private x As LongLong", "", "LongLong", "20")]
    // a LongPtr is the integer a pointer is on the platform: LongLong on a 64-bit one.
    [DataRow("Private x As LongPtr", "", "LongLong", "20")]
    [DataRow("Private x As Single", "", "Single", "4")]
    [DataRow("Private x As Double", "", "Double", "5")]
    [DataRow("Private x As Currency", "", "Currency", "6")]
    [DataRow("Private x As Date", "", "Date", "7")]
    [DataRow("Private x As String", "", "String", "8")]
    [DataRow("Private x As String * 3", "", "String", "8")]
    [DataRow("Private x As Boolean", "", "Boolean", "11")]
    [DataRow("Private x As Variant", "", "Empty", "0")]
    [DataRow("Private x As Variant", "x = 1", "Integer", "2")]
    [DataRow("Private x As Variant", "x = CDec(1)", "Decimal", "14")]
    [DataRow("Private x As Variant", "x = CVErr(5)", "Error", "10")]
    [DataRow("Private x As Variant", "x = Null", "Null", "1")]
    [DataRow("Private x As Variant", "x = \"s\"", "String", "8")]
    [DataRow("Private x As Variant", "x = #1/2/2000#", "Date", "7")]
    [DataRow("Private x As Variant", "Set x = New Collection", "Collection", "9")]
    [DataRow("Private x As Variant", "Set x = Nothing", "Nothing", "9")]
    public Task OfAModuleVariable(string declaration, string setup, string typeName, string varType)
        => AssertTypeOf(declaration, setup, "x", typeName, varType);

    [TestMethod]
    [DataRow("Dim x As Integer", "Integer", "2")]
    [DataRow("Dim x As Double", "Double", "5")]
    [DataRow("Dim x As String", "String", "8")]
    [DataRow("Dim x", "Empty", "0")]
    [DataRow("Dim x As Object", "Nothing", "9")]
    public Task OfALocalVariable(string declaration, string typeName, string varType)
        => AssertTypeOf(string.Empty, declaration, "x", typeName, varType);

    [TestMethod]
    public Task OfAnEnumVariable_IsLong()
        // MS-VBAL 5.2.3.4: an enum's members are Long constants, and a variable declared as the enum holds a Long.
        => AssertTypeOf("Public Enum Colour\r\n    Red = 1\r\nEnd Enum\r\nPrivate x As Colour", string.Empty, "x", "Long", "3");

    [TestMethod]
    [Ignore("A workspace Enum's members have no value on the host: the descriptor carries no value expression for them, and reading one throws " +
        "'No runtime binding exists yet' out of SymbolAddressTable (MS-VBAL 5.2.3.4).")]
    [DataRow("Red")]
    [DataRow("Colour.Red")]
    public Task OfAWorkspaceEnumMember_IsLong(string expression)
        => AssertTypeOf("Public Enum Colour\r\n    Red = 1\r\nEnd Enum", string.Empty, expression, "Long", "3");

    #endregion

    #region objects

    [TestMethod]
    [DataRow("Private x As Object", "", "Nothing", "9")]
    [DataRow("Private x As Object", "Set x = New Collection", "Collection", "9")]
    [DataRow("Private x As Object", "Set x = New Class1", "Class1", "9")]
    [DataRow("Private x As Collection", "", "Nothing", "9")]
    [DataRow("Private x As Collection", "Set x = New Collection", "Collection", "9")]
    [DataRow("Private x As Class1", "", "Nothing", "9")]
    [DataRow("Private x As Class1", "Set x = New Class1", "Class1", "9")]
    // As New makes the object the first time the variable is referred to.
    [DataRow("Private x As New Class1", "", "Class1", "9")]
    [DataRow("Private x As IFoo", "", "Nothing", "9")]
    // an object is named by its class, whatever interface the variable holding it was declared as.
    [DataRow("Private x As IFoo", "Set x = New Impl", "Impl", "9")]
    [DataRow("Private x As Impl", "Set x = New Impl", "Impl", "9")]
    public Task OfAnObjectVariable(string declaration, string setup, string typeName, string varType)
        => AssertTypeOf(declaration, setup, "x", typeName, varType);

    [TestMethod]
    [DataRow("New Collection", "Collection", "9")]
    [DataRow("New Class1", "Class1", "9")]
    // the error object is an ErrObject, and its default member is Number: VarType reads it.
    [DataRow("Err", "ErrObject", "3")]
    public Task OfAnObjectExpression(string expression, string typeName, string varType)
        => AssertTypeOf(string.Empty, string.Empty, expression, typeName, varType);

    [TestMethod]
    [DataRow("DfltStr", "8")]
    [DataRow("DfltNum", "3")]
    [DataRow("DfltDbl", "5")]
    [DataRow("DfltNull", "1")]
    [DataRow("DfltEmpty", "0")]
    // a default member that returns an object is not read again: the value is an object.
    [DataRow("DfltObj", "9")]
    [DataRow("DfltChain", "9")]
    [DataRow("DfltNothing", "9")]
    [DataRow("DfltArr", "8195")]
    // a default member that takes an argument it is not given is not one VarType can read.
    [DataRow("DfltArgs", "9")]
    public Task OfAnObjectWithADefaultMember_TypeNameIsTheClass_VarTypeIsTheDefaultMembers(string className, string varType)
        => AssertTypeOf($"Private x As {className}", $"Set x = New {className}", "x", className, varType);

    [TestMethod]
    public Task OfAVariantHoldingAnObjectWithADefaultMember_IsTheObject()
        => AssertTypeOf("Private x As Variant", "Set x = New DfltNum", "x", "DfltNum", "3");

    [TestMethod]
    public Task OfAnUnsetVariableOfAClassWithADefaultMember_IsNothing()
        => AssertTypeOf("Private x As DfltNum", string.Empty, "x", "Nothing", "9");

    [TestMethod]
    public async Task OfMe_IsTheClass_AndVarTypeItsDefaultMembers()
        => CollectionAssert.AreEqual(new[] { "Gauge", "5", "True" }, await Run(string.Empty, "Dim g As New Gauge", "g.Report"));

    #endregion

    #region arrays

    [TestMethod]
    [DataRow("Private a() As Long", "", "Long()", "8195")]
    [DataRow("Private a(2) As Long", "", "Long()", "8195")]
    [DataRow("Private a() As Long", "ReDim a(1)", "Long()", "8195")]
    [DataRow("Private a(1, 2) As Integer", "", "Integer()", "8194")]
    [DataRow("Private a() As Byte", "", "Byte()", "8209")]
    [DataRow("Private a() As Byte", "a = \"ab\"", "Byte()", "8209")]
    [DataRow("Private a() As String", "", "String()", "8200")]
    [DataRow("Private a() As Boolean", "", "Boolean()", "8203")]
    [DataRow("Private a() As Date", "", "Date()", "8199")]
    [DataRow("Private a() As Currency", "", "Currency()", "8198")]
    [DataRow("Private a() As Double", "", "Double()", "8197")]
    [DataRow("Private a() As Single", "", "Single()", "8196")]
    [DataRow("Private a() As LongLong", "", "LongLong()", "8212")]
    [DataRow("Private a() As LongPtr", "", "LongLong()", "8212")]
    [DataRow("Private a() As Variant", "", "Variant()", "8204")]
    [DataRow("Private a(3) As Variant", "", "Variant()", "8204")]
    // an array of objects is "Object()" whatever class its elements are declared as.
    [DataRow("Private a(1) As Object", "", "Object()", "8201")]
    [DataRow("Private a() As Collection", "", "Object()", "8201")]
    [DataRow("Private a() As Class1", "", "Object()", "8201")]
    [DataRow("Private a() As IFoo", "", "Object()", "8201")]
    [DataRow("Public Enum Colour\r\n    Red = 1\r\nEnd Enum\r\nPrivate a() As Colour", "", "Long()", "8195")]
    public Task OfAnArrayVariable(string declaration, string setup, string typeName, string varType)
        => AssertTypeOf(declaration, setup, "a", typeName, varType);

    [TestMethod]
    [DataRow("Dim a() As Long", "Long()", "8195")]
    [DataRow("Dim a(2) As String", "String()", "8200")]
    [DataRow("Dim a()", "Variant()", "8204")]
    public Task OfALocalArray(string declaration, string typeName, string varType)
        => AssertTypeOf(string.Empty, declaration, "a", typeName, varType);

    [TestMethod]
    [DataRow("Array(1, 2)", "Variant()", "8204")]
    [DataRow("Array()", "Variant()", "8204")]
    public Task OfAnArrayExpression(string expression, string typeName, string varType)
        => AssertTypeOf(string.Empty, string.Empty, expression, typeName, varType);

    [TestMethod]
    public Task OfAVariantHoldingAVariantArray()
        => AssertTypeOf("Private v As Variant", "v = Array(1, \"a\")", "v", "Variant()", "8204");

    [TestMethod]
    public Task OfAVariantHoldingALongArray()
        => AssertTypeOf("Private v As Variant\r\nPrivate a(1) As Long", "v = a", "v", "Long()", "8195");

    [TestMethod]
    [Ignore("ReDim of a Variant ignores its As clause: the array it makes is a Variant() whatever type the statement names (MS-VBAL 5.4.3.3).")]
    public Task OfAVariantReDimmedAsLong_IsALongArray()
        => AssertTypeOf("Private v As Variant", "ReDim v(1) As Long", "v", "Long()", "8195");

    [TestMethod]
    public Task OfSplit_IsAStringArray()
        => AssertTypeOf(string.Empty, string.Empty, "Split(\"a,b\", \",\")", "String()", "8200");

    [TestMethod]
    [DataRow("Private a(1) As Long", "", "a(0)", "Long", "3")]
    [DataRow("Private a(3) As Variant", "", "a(0)", "Empty", "0")]
    [DataRow("Private a(3) As Variant", "a(1) = \"s\"", "a(1)", "String", "8")]
    [DataRow("Private a(3) As Variant", "a(2) = Array(1)", "a(2)", "Variant()", "8204")]
    [DataRow("Private a(3) As Variant", "Set a(3) = New Collection", "a(3)", "Collection", "9")]
    [DataRow("Private a(1) As Object", "", "a(0)", "Nothing", "9")]
    public Task OfAnArrayElement(string declaration, string setup, string expression, string typeName, string varType)
        => AssertTypeOf(declaration, setup, expression, typeName, varType);

    #endregion

    #region function results and parameters

    [TestMethod]
    [DataRow("Private Function F() As Integer\r\n    F = 1\r\nEnd Function", "Integer", "2")]
    [DataRow("Private Function F()\r\nEnd Function", "Empty", "0")]
    [DataRow("Private Function F() As Object\r\nEnd Function", "Nothing", "9")]
    [DataRow("Private Function F() As Class1\r\n    Set F = New Class1\r\nEnd Function", "Class1", "9")]
    [DataRow("Private Function F() As Long()\r\n    Dim a(1) As Long\r\n    F = a\r\nEnd Function", "Long()", "8195")]
    public Task OfAFunctionResult(string function, string typeName, string varType)
        => AssertTypeOf(function, string.Empty, "F()", typeName, varType);

    private const string ShowByValue = "Private Sub Show(ByVal v As Variant)\r\n    Debug.Print TypeName(v)\r\n    Debug.Print VarType(v)\r\nEnd Sub";
    private const string ShowByReference = "Private Sub Show(v)\r\n    Debug.Print TypeName(v)\r\n    Debug.Print VarType(v)\r\nEnd Sub";

    [TestMethod]
    [DataRow("Dim x As Integer: x = 1", "Integer", "2")]
    [DataRow("Dim x As Date: x = #1/2/2000#", "Date", "7")]
    [DataRow("Dim x As Object", "Nothing", "9")]
    [DataRow("Dim x As Collection: Set x = New Collection", "Collection", "9")]
    // an object passed to a Variant parameter is the object, not its default member's value.
    [DataRow("Dim x As DfltNum: Set x = New DfltNum", "DfltNum", "3")]
    [DataRow("Dim x: Set x = New DfltNum", "DfltNum", "3")]
    public async Task OfAByValueVariantParameter_IsTheArgument(string setup, string typeName, string varType)
        => CollectionAssert.AreEqual(new[] { typeName, varType }, await Run(ShowByValue, setup, "Show x"));

    [TestMethod]
    [DataRow("Dim x As Integer: x = 1", "Integer", "2")]
    [DataRow("Dim x As String: x = \"s\"", "String", "8")]
    [DataRow("Dim x As Object", "Nothing", "9")]
    [DataRow("Dim x As Collection: Set x = New Collection", "Collection", "9")]
    [DataRow("Dim x As DfltNum: Set x = New DfltNum", "DfltNum", "3")]
    [DataRow("Dim x() As Long", "Long()", "8195")]
    public async Task OfAByReferenceVariantParameter_IsTheVariablesType(string setup, string typeName, string varType)
        => CollectionAssert.AreEqual(new[] { typeName, varType }, await Run(ShowByReference, setup, "Show x"));

    [TestMethod]
    public async Task OfTypedParameters_IsTheirDeclaredTypes()
        => CollectionAssert.AreEqual(new[] { "Integer", "Double", "String" }, await Run(
            "Private Sub Typed(ByVal i As Integer, ByVal d As Double, ByVal s As String)\r\n" +
            "    Debug.Print TypeName(i)\r\n    Debug.Print TypeName(d)\r\n    Debug.Print TypeName(s)\r\nEnd Sub",
            "Typed 1, 1, 1"));

    [TestMethod]
    [DataRow("TypeName(VarType(1))", "Long")]
    [DataRow("TypeName(TypeName(1))", "String")]
    [DataRow("TypeName(IsArray(1))", "Boolean")]
    [DataRow("VarType(VarType(1))", "3")]
    [DataRow("TypeName(QBColor(1))", "Long")]
    [DataRow("TypeName(RGB(1, 2, 3))", "Long")]
    [DataRow("TypeName(IMEStatus)", "Long")]
    [DataRow("TypeName(Err.Number)", "Long")]
    [DataRow("TypeName(Err.Description)", "String")]
    [DataRow("TypeName(Array(1, \"a\")(1))", "String")]
    [DataRow("TypeName(Array(Array(1))(0))", "Variant()")]
    [DataRow("TypeName(Array(CDec(1)))", "Variant()")]
    [DataRow("TypeName(Array(CDec(1))(0))", "Decimal")]
    public async Task OfAnotherFunctionsResult(string expression, string expected)
        => CollectionAssert.AreEqual(new[] { expected }, await Run(string.Empty, $"Debug.Print {expression}"));

    [TestMethod]
    public async Task OfACollectionsItems_IsWhatWasAdded()
        => CollectionAssert.AreEqual(new[] { "Integer", "String", "Class1", "Integer", "Long" }, await Run(string.Empty,
            "Dim c As New Collection",
            "c.Add 1: c.Add \"a\": c.Add New Class1",
            "Debug.Print TypeName(c(1))",
            "Debug.Print TypeName(c(2))",
            "Debug.Print TypeName(c(3))",
            "Debug.Print TypeName(c.Item(1))",
            "Debug.Print TypeName(c.Count)"));

    #endregion
}
