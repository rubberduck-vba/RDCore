namespace RDCore.Tests;

/// <summary>
/// Static helper to simplify building a valid workspace <c>Uri</c> for a <c>Symbol</c>.
/// </summary>
internal static class TestUri
{
    private const string _workspaceRoot = "file://rdcore-test";
    /// <summary>
    /// Gets the top-level (project/library) <c>Uri</c> for a test workspace.
    /// </summary>
    /// <returns></returns>
    public static Uri WorkspaceRoot() => new(_workspaceRoot);

    private const string TestModuleName = "TestModule1";
    private static readonly string _testModuleUri = $"{_workspaceRoot}#{TestModuleName}";
    /// <summary>
    /// Gets a <c>Uri</c> for a test module.
    /// </summary>
    /// <param name="name">An optional name for the module.</param>
    public static Uri TestModuleUri(string name = TestModuleName) 
        => new(_testModuleUri.Replace(TestModuleName, name));

    private const string TestSubProcName = "TestMethod1";
    private static readonly string _testSubProcUri = $"{_testModuleUri.Replace('#','/')}#{TestSubProcName}";
    /// <summary>
    /// Gets a <c>Uri</c> for a test <c>Sub</c> procedure.
    /// </summary>
    /// <param name="name">An optional name for the procedure.</param>
    /// <param name="parentName">An optional name for the parent module.</param>
    public static Uri TestSubProcUri(string name = TestSubProcName, string parentName = TestModuleName) 
        => new(_testSubProcUri.Replace(TestModuleName, parentName).Replace(TestSubProcName, name));

    private const string TestFunctionProcName = "TestFunction1";
    private static readonly string _testFunctionProcUri = $"{_testModuleUri.Replace('#','/')}#{TestFunctionProcName}";
    /// <summary>
    /// Gets a <c>Uri</c> for a test <c>Function</c> procedure.
    /// </summary>
    /// <param name="name">An optional name for the procedure.</param>
    /// <param name="parentName">An optional name for the parent module.</param>
    public static Uri TestFunctionProcUri(string name = TestFunctionProcName, string parentName = TestModuleName) 
        => new(_testFunctionProcUri.Replace(TestModuleName, parentName).Replace(TestFunctionProcName, name));

    private const string TestPropertyName = "TestProperty1";
    private static readonly string _testPropertyGetProcUri = $"{_testModuleUri.Replace('#', '/')}#{TestPropertyName}-get";
    /// <summary>
    /// Gets a <c>Uri</c> for a test <c>Property Get</c> procedure.
    /// </summary>
    /// <param name="name">An optional name for the procedure.</param>
    /// <param name="parentName">An optional name for the parent module.</param>
    public static Uri TestPropertyGetProcUri(string name = TestPropertyName, string parentName = TestModuleName)
        => new(_testPropertyGetProcUri.Replace(TestModuleName, parentName).Replace(TestPropertyName, name));

    private static readonly string _testPropertyLetProcUri = $"{_testModuleUri.Replace('#', '/')}#{TestPropertyName}-let";
    /// <summary>
    /// Gets a <c>Uri</c> for a test <c>Property Let</c> procedure.
    /// </summary>
    /// <param name="name">An optional name for the procedure.</param>
    /// <param name="parentName">An optional name for the parent module.</param>
    public static Uri TestPropertyLetProcUri(string name = TestPropertyName, string parentName = TestModuleName)
        => new(_testPropertyLetProcUri.Replace(TestModuleName, parentName).Replace(TestPropertyName, name));

    private static readonly string _testPropertySetProcUri = $"{_testModuleUri.Replace('#', '/')}#{TestPropertyName}-set";
    /// <summary>
    /// Gets a <c>Uri</c> for a test <c>Property Set</c> procedure.
    /// </summary>
    /// <param name="name">An optional name for the procedure.</param>
    /// <param name="parentName">An optional name for the parent module.</param>
    public static Uri TestPropertySetProcUri(string name = TestPropertyName, string parentName = TestModuleName)
        => new(_testPropertySetProcUri.Replace(TestModuleName, parentName).Replace(TestPropertyName, name));

    private const string TestModuleVariableName = "TestField1";
    private static readonly string _testModuleVariableUri = $"{_testModuleUri.Replace('#', '/')}#{TestModuleVariableName}";
    /// <summary>
    /// Gets a <c>Uri</c> for a test module-level variable (field).
    /// </summary>
    /// <param name="name">An optional name for the variable.</param>
    /// <param name="parentName">An optional name for the parent module.</param>
    public static Uri TestModuleVariableUri(string name = TestModuleVariableName, string parentName = TestModuleName)
        => new(_testModuleVariableUri.Replace(TestModuleName, parentName).Replace(TestModuleVariableName, name));

    private const string TestModuleConstName = "TestConst1";
    private static readonly string _testModuleConstUri = $"{_testModuleUri.Replace('#', '/')}#{TestModuleConstName}";
    /// <summary>
    /// Gets a <c>Uri</c> for a test module-level variable (field).
    /// </summary>
    /// <param name="name">An optional name for the variable.</param>
    /// <param name="parentName">An optional name for the parent module.</param>
    public static Uri TestModuleConstUri(string name = TestModuleConstName, string parentName = TestModuleName)
        => new(_testModuleConstUri.Replace(TestModuleName, parentName).Replace(TestModuleConstName, name));

    private const string TestModuleEventName = "TestEvent1";
    private static readonly string _testModuleEventUri = $"{_testModuleUri.Replace('#', '/')}#{TestModuleEventName}";
    /// <summary>
    /// Gets a <c>Uri</c> for a test <c>Event</c> declaration.
    /// </summary>
    /// <param name="name">An optional name for the module member.</param>
    /// <param name="parentName">An optional name for the parent module.</param>
    public static Uri TestModuleEventUri(string name = TestModuleEventName, string parentName = TestModuleName)
        => new(_testModuleEventUri.Replace(TestModuleName, parentName).Replace(TestModuleEventName, name));

    private const string TestModuleEnumName = "TestEnum1";
    private static readonly string _testModuleEnumUri = $"{_testModuleUri.Replace('#', '/')}#{TestModuleEnumName}";
    /// <summary>
    /// Gets a <c>Uri</c> for a test <c>Enum</c> declaration.
    /// </summary>
    /// <param name="name">An optional name for the module member.</param>
    /// <param name="parentName">An optional name for the parent module.</param>
    public static Uri TestModuleEnumUri(string name = TestModuleEnumName, string parentName = TestModuleName)
        => new(_testModuleEnumUri.Replace(TestModuleName, parentName).Replace(TestModuleEnumName, name));

    private const string TestEnumMemberName = "TestEnumMember1";
    private static readonly string _testEnumMemberUri = $"{_testModuleEnumUri.Replace('#', '/')}#{TestEnumMemberName}";
    /// <summary>
    /// Gets a <c>Uri</c> for a test <c>Enum</c> member (constant) declaration.
    /// </summary>
    /// <param name="name">An optional name for the <c>Enum</c> member.</param>
    /// <param name="parentName">An optional name for the parent <c>Enum</c> declaration.</param>
    /// <param name="moduleName">An optional name for the parent module.</param>
    public static Uri TestEnumMemberUri(string name = TestEnumMemberName, string parentName = TestModuleEnumName, string moduleName = TestModuleName)
        => new(_testEnumMemberUri.Replace(TestModuleName, moduleName).Replace(TestModuleEnumName, parentName).Replace(TestEnumMemberName, name));

    private const string TestModuleUserDefinedTypeName = "TestUserDefinedType1";
    private static readonly string _testModuleUserDefinedTypeUri = $"{_testModuleUri.Replace('#', '/')}#{TestModuleUserDefinedTypeName}";
    /// <summary>
    /// Gets a <c>Uri</c> for a test <c>Type</c> (UDT) declaration.
    /// </summary>
    /// <param name="name">An optional name for the module member.</param>
    /// <param name="parentName">An optional name for the parent module.</param>
    public static Uri TestModuleUserDefinedTypeUri(string name = TestModuleUserDefinedTypeName, string parentName = TestModuleName)
        => new(_testModuleUserDefinedTypeUri.Replace(TestModuleName, parentName).Replace(TestModuleUserDefinedTypeName, name));

    private const string TestUserDefinedTypeMemberName = "TestUserDefinedTypeMember1";
    private static readonly string _testUserDefinedTypeMemberUri = $"{_testModuleUserDefinedTypeUri.Replace('#', '/')}#{TestUserDefinedTypeMemberName}";
    /// <summary>
    /// Gets a <c>Uri</c> for a test <c>Type</c> (UDT) member (field) declaration.
    /// </summary>
    /// <param name="name">An optional name for the <c>Type</c> (UDT) member.</param>
    /// <param name="parentName">An optional name for the parent <c>Type</c> (UDT) declaration.</param>
    /// <param name="moduleName">An optional name for the parent module.</param>
    public static Uri TestUserDefinedTypeMemberUri(string name = TestUserDefinedTypeMemberName, string parentName = TestModuleUserDefinedTypeName, string moduleName = TestModuleName)
        => new(_testUserDefinedTypeMemberUri.Replace(TestModuleName, moduleName).Replace(TestModuleUserDefinedTypeName, parentName).Replace(TestUserDefinedTypeMemberName, name));

    private const string TestParameterName = "TestParameter1";
    private static readonly string _testParameterUri = $"{_testSubProcUri.Replace('#', '/')}#{TestParameterName}";
    /// <summary>
    /// Gets a <c>Uri</c> for a test parameter declaration.
    /// </summary>
    /// <param name="name">An optional name for the parameter.</param>
    /// <param name="parentName">An optional name for the parent module member.</param>
    /// <param name="moduleName">An optional name for the parent module.</param>
    public static Uri TestParameterUri(string name = TestParameterName, string parentName = TestSubProcName, string moduleName = TestModuleName)
        => new(_testParameterUri.Replace(TestModuleName, moduleName).Replace(TestSubProcName, parentName).Replace(TestParameterName, name));

    private const string TestVariableName = "TestVariable1";
    private static readonly string _testVariableUri = $"{_testSubProcUri.Replace('#', '/')}#{TestVariableName}";
    /// <summary>
    /// Gets a <c>Uri</c> for a test local variable declaration.
    /// </summary>
    /// <param name="name">An optional name for the variable.</param>
    /// <param name="parentName">An optional name for the parent module member.</param>
    /// <param name="moduleName">An optional name for the parent module.</param>
    public static Uri TestVariableUri(string name = TestVariableName, string parentName = TestSubProcName, string moduleName = TestModuleName)
        => new(_testVariableUri.Replace(TestModuleName, moduleName).Replace(TestSubProcName, parentName).Replace(TestVariableName, name));

    private const string TestLocalConstName = "TestLocalConst1";
    private static readonly string _testLocalConstUri = $"{_testSubProcUri.Replace('#', '/')}#{TestLocalConstName}";
    /// <summary>
    /// Gets a <c>Uri</c> for a test local const declaration.
    /// </summary>
    /// <param name="name">An optional name for the constant.</param>
    /// <param name="parentName">An optional name for the parent module member.</param>
    /// <param name="moduleName">An optional name for the parent module.</param>
    public static Uri TestLocalConstUri(string name = TestLocalConstName, string parentName = TestSubProcName, string moduleName = TestModuleName)
        => new(_testLocalConstUri.Replace(TestModuleName, moduleName).Replace(TestSubProcName, parentName).Replace(TestLocalConstName, name));

    private const string TestUnaryOpName = "TestUnaryOp";
    private static readonly string _testUnaryOpUri = $"{_testSubProcUri.Replace('#', '/')}#{TestUnaryOpName}";
    /// <summary>
    /// Gets a <c>Uri</c> for a test unary operation.
    /// </summary>
    /// <param name="name">An optional name for the unary operation.</param>
    /// <param name="parentName">An optional name for the parent module member.</param>
    /// <param name="moduleName">An optional name for the parent module.</param>
    public static Uri TestUnaryOpUri(string name = TestUnaryOpName, string parentName = TestSubProcName, string moduleName = TestModuleName)
        => new(_testUnaryOpUri.Replace(TestModuleName, moduleName).Replace(TestSubProcName, parentName).Replace(TestUnaryOpName, name));

    private const string TestUnaryOperandName = "LHS";
    private static readonly string _testUnaryOperandUri = $"{_testUnaryOpUri.Replace('#', '/')}#{TestUnaryOperandName}";
    /// <summary>
    /// Gets a <c>Uri</c> for the operand of a test unary operation.
    /// </summary>
    /// <param name="name">An optional name for the unary operand.</param>
    /// <param name="parentName">An optional name for the parent module member.</param>
    /// <param name="moduleName">An optional name for the parent module.</param>
    public static Uri TestUnaryOpLhsUri(string name = TestUnaryOperandName, string parentName = TestSubProcName, string moduleName = TestModuleName)
        => new(_testUnaryOperandUri.Replace(TestModuleName, moduleName).Replace(TestSubProcName, parentName).Replace(TestUnaryOperandName, name));

    private const string TestBinaryOpName = "TestBinaryOp";
    private static readonly string _testBinaryOpUri = $"{_testSubProcUri.Replace('#', '/')}#{TestBinaryOpName}";
    /// <summary>
    /// Gets a <c>Uri</c> for a test binary operation.
    /// </summary>
    /// <param name="name">An optional name for the binary operation.</param>
    /// <param name="parentName">An optional name for the parent module member.</param>
    /// <param name="moduleName">An optional name for the parent module.</param>
    public static Uri TestBinaryOpUri(string name = TestBinaryOpName, string parentName = TestSubProcName, string moduleName = TestModuleName)
        => new(_testBinaryOpUri.Replace(TestModuleName, moduleName).Replace(TestSubProcName, parentName).Replace(TestBinaryOpName, name));

    private const string TestBinaryOpLhsName = "LHS";
    private static readonly string _testBinaryOpLhsUri = $"{_testBinaryOpUri.Replace('#', '/')}#{TestBinaryOpLhsName}";
    /// <summary>
    /// Gets a <c>Uri</c> for the LHS operand of a test binary operation.
    /// </summary>
    /// <param name="name">An optional name for the operand.</param>
    /// <param name="parentName">An optional name for the parent module member.</param>
    /// <param name="scopeName">An optional name for the parent module member.</param>
    /// <param name="moduleName">An optional name for the parent module.</param>
    public static Uri TestBinaryOpLhsUri(string name = TestBinaryOpLhsName, string parentName = TestBinaryOpName, string scopeName = TestSubProcName, string moduleName = TestModuleName)
        => new(_testBinaryOpLhsUri.Replace(TestModuleName, moduleName).Replace(TestSubProcName, scopeName).Replace(TestBinaryOpName, parentName).Replace(TestBinaryOpLhsName, name));

    private const string TestBinaryOpRhsName = "RHS";
    private static readonly string _testBinaryOpRhsUri = $"{_testBinaryOpUri.Replace('#', '/')}#{TestBinaryOpRhsName}";
    /// <summary>
    /// Gets a <c>Uri</c> for the RHS operand of a test binary operation.
    /// </summary>
    /// <param name="name">An optional name for the operand.</param>
    /// <param name="parentName">An optional name for the parent symbol.</param>
    /// <param name="scopeName">An optional name for the parent module member.</param>
    /// <param name="moduleName">An optional name for the parent module.</param>
    public static Uri TestBinaryOpRhsUri(string name = TestBinaryOpRhsName, string parentName = TestBinaryOpName, string scopeName = TestSubProcName, string moduleName = TestModuleName)
        => new(_testBinaryOpRhsUri.Replace(TestModuleName, moduleName).Replace(TestSubProcName, scopeName).Replace(TestBinaryOpName, parentName).Replace(TestBinaryOpLhsName, name));
}
