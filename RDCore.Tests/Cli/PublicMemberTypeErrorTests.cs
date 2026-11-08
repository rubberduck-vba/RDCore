using RDCore.Runtime.LanguageCore;

namespace RDCore.Tests.Cli;

/// <summary>
/// What a module may expose (<strong>MS-VBAL §5.2.3.1</strong>, <strong>§5.2.3.3</strong>, <strong>§5.3.1</strong>): a public member is not declared with a
/// private <c>Enum</c>, nor in a class module with a private user-defined type; a class module exposes no constant, fixed-length string, array,
/// user-defined type or <c>Declare</c>, and defines no public user-defined type. Each case is what MS-VBA does, recorded in Excel with a compile of the
/// whole project (2026-10-17) - which is not everything MS-VBAL says, nor everything its messages say.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.3.1 Module Variable Declaration Lists")]
public sealed class PublicMemberTypeErrorTests
{
    private const string PrivateType = "Private Type T\r\n X As Long\r\nEnd Type\r\n";
    private const string PrivateEnum = "Private Enum E\r\n One\r\nEnd Enum\r\n";
    private const string PublicType = "Public Type PT\r\n X As Long\r\nEnd Type\r\n";

    private static Task<string[]> InAStandardModule(string declarations)
        => ModuleWorkspace.LoadErrorsAsync([], "Attribute VB_Name = \"Program\"\r\n" + declarations + "\r\n");

    private static Task<string[]> InAClassModule(string declarations, string program = "")
        => ModuleWorkspace.LoadErrorsAsync([("Widget", ModuleWorkspace.ClassModule("Widget", declarations))], "Attribute VB_Name = \"Program\"\r\n" + program + "\r\n");

    private static void AssertTheError(string message, string[] errors)
    {
        var error = Assert.ContainsSingle(errors);
        StringAssert.Contains(error, message);
    }

    [TestMethod]
    [DataRow(PrivateEnum + "Public V As E", DisplayName = "A Public variable")]
    [DataRow(PrivateEnum + "Global V As E", DisplayName = "A Global variable")]
    [DataRow(PrivateEnum + "Public V() As E", DisplayName = "A Public array")]
    [DataRow(PrivateEnum + "Public Function F() As E\r\nEnd Function", DisplayName = "The result of a Public Function")]
    [DataRow(PrivateEnum + "Function F() As E\r\nEnd Function", DisplayName = "The result of a Function with no modifier, which is public")]
    [DataRow(PrivateEnum + "Public Property Get P() As E\r\nEnd Property", DisplayName = "The result of a Public Property Get")]
    [DataRow(PrivateEnum + "Public Property Let P(ByVal v As E)\r\nEnd Property", DisplayName = "A parameter of a Public Property Let")]
    [DataRow(PrivateEnum + "Public Sub S(ByVal x As E)\r\nEnd Sub", DisplayName = "A parameter of a Public Sub")]
    [DataRow(PrivateEnum + "Public Sub S(x() As E)\r\nEnd Sub", DisplayName = "An array parameter of a Public Sub")]
    [DataRow(PrivateEnum + "Public Declare PtrSafe Sub Sleep Lib \"kernel32\" (ByVal ms As E)", DisplayName = "A parameter of a Public Declare")]
    public async Task APublicMemberOfAStandardModule_DeclaredWithAPrivateEnum_IsAnError(string declarations)
        => AssertTheError(Exceptions.VBCompileError_PrivateTypeInPublicMember, await InAStandardModule(declarations));

    // the message names user-defined types and fields of a public type too, but MS-VBA refuses neither in a standard module.
    [TestMethod]
    [DataRow(PrivateType + "Public V As T", DisplayName = "A Public variable of a private type")]
    [DataRow(PrivateType + "Public Function F() As T\r\nEnd Function", DisplayName = "The result of a Public Function, a private type")]
    [DataRow(PrivateType + "Public Function F() As T()\r\nEnd Function", DisplayName = "The result of a Public Function, an array of a private type")]
    [DataRow(PrivateType + "Public Sub S(ByRef x As T)\r\nEnd Sub", DisplayName = "A parameter of a Public Sub, a private type")]
    [DataRow(PrivateType + "Public Property Get P() As T\r\nEnd Property", DisplayName = "The result of a Public Property Get, a private type")]
    [DataRow(PrivateType + "Public Type U\r\n f As T\r\nEnd Type", DisplayName = "A field of a Public Type, a private type")]
    [DataRow(PrivateEnum + "Public Type U\r\n f As E\r\nEnd Type", DisplayName = "A field of a Public Type, a private Enum")]
    [DataRow(PrivateEnum + "Dim V As E", DisplayName = "A variable declared with Dim, which is private")]
    [DataRow(PrivateEnum + "Private Function F() As E\r\nEnd Function", DisplayName = "A Private Function")]
    [DataRow("Enum E\r\n One\r\nEnd Enum\r\nPublic V As E", DisplayName = "An Enum with no modifier, which is public")]
    public async Task APublicMemberOfAStandardModule_DeclaredWithAPrivateType_OrAFieldOfAPublicType_IsNoError(string declarations)
        => CollectionAssert.AreEqual(Array.Empty<string>(), await InAStandardModule(declarations));

    [TestMethod]
    [DataRow(PrivateType + "Public Function F() As T\r\nEnd Function", DisplayName = "The result of a Public Function, a private type")]
    [DataRow(PrivateType + "Function F() As T\r\nEnd Function", DisplayName = "The result of a Function with no modifier, a private type")]
    [DataRow(PrivateType + "Public Property Get P() As T\r\nEnd Property", DisplayName = "The result of a Public Property Get, a private type")]
    [DataRow(PrivateType + "Public Sub S(ByRef x As T)\r\nEnd Sub", DisplayName = "A parameter of a Public Sub, a private type")]
    [DataRow(PrivateType + "Public Event Moved(ByRef x As T)", DisplayName = "A parameter of an Event, a private type")]
    [DataRow(PrivateEnum + "Public V As E", DisplayName = "A Public variable, a private Enum")]
    [DataRow(PrivateEnum + "Public Function F() As E\r\nEnd Function", DisplayName = "The result of a Public Function, a private Enum")]
    [DataRow(PrivateEnum + "Public Sub S(ByVal x As E)\r\nEnd Sub", DisplayName = "A parameter of a Public Sub, a private Enum")]
    [DataRow(PrivateEnum + "Public Event Moved(ByVal x As E)", DisplayName = "A parameter of an Event, a private Enum")]
    public async Task APublicMemberOfAClassModule_DeclaredWithAPrivateTypeOrEnum_IsAnError(string declarations)
        => AssertTheError(Exceptions.VBCompileError_PrivateTypeInPublicMember, await InAClassModule(declarations));

    [TestMethod]
    [DataRow("Public Const K As Long = 1", DisplayName = "A Public Const")]
    [DataRow("Public S As String * 4", DisplayName = "A Public fixed-length String")]
    [DataRow("Public A() As Long", DisplayName = "A Public array")]
    [DataRow("Public A(3) As Long", DisplayName = "A Public fixed-size array")]
    [DataRow("Public Declare PtrSafe Sub Sleep Lib \"kernel32\" (ByVal ms As Long)", DisplayName = "A Public Declare")]
    [DataRow("Declare PtrSafe Sub Sleep Lib \"kernel32\" (ByVal ms As Long)", DisplayName = "A Declare with no modifier, which is public")]
    [DataRow("Global V As Long", DisplayName = "A Global variable, whatever its type")]
    [DataRow(PrivateType + "Public V As T", DisplayName = "A Public variable of a private type: this error, and not the private type's")]
    [DataRow(PrivateEnum + "Public V() As E", DisplayName = "A Public array of a private Enum: this error, and not the private Enum's")]
    [DataRow(PrivateEnum + "Public Const K As Long = 1", DisplayName = "A Public Const beside a private Enum")]
    public async Task APublicMemberOfAClassModule_ThatAnObjectCannotExpose_IsAnError(string declarations)
        => AssertTheError(Exceptions.VBCompileError_NotAllowedAsPublicMemberOfObjectModule, await InAClassModule(declarations));

    [TestMethod]
    public async Task APublicVariableOfAClassModule_OfAStandardModulesPublicType_IsAnError()
        => AssertTheError(Exceptions.VBCompileError_NotAllowedAsPublicMemberOfObjectModule, await InAClassModule("Public V As PT", PublicType));

    [TestMethod]
    [DataRow("Public Type T\r\n X As Long\r\nEnd Type", DisplayName = "A Public Type")]
    [DataRow("Type T\r\n X As Long\r\nEnd Type", DisplayName = "A Type with no modifier, which is public")]
    public async Task APublicTypeOfAClassModule_IsAnError(string declarations)
        => AssertTheError(Exceptions.VBCompileError_PublicUserDefinedTypeInObjectModule, await InAClassModule(declarations));

    [TestMethod]
    [DataRow(PrivateType + "Private V As T\r\nPrivate Function F() As T\r\nEnd Function", DisplayName = "Private members of a private type")]
    [DataRow(PrivateType + "Friend Function F() As T\r\nEnd Function", DisplayName = "A Friend Function of a private type")]
    [DataRow(PrivateEnum + "Friend Sub S(ByVal x As E)\r\nEnd Sub", DisplayName = "A Friend Sub with a parameter of a private Enum")]
    [DataRow(PrivateEnum + "Private Type U\r\n f As E\r\nEnd Type", DisplayName = "A field of a Private Type, a private Enum")]
    [DataRow(PrivateEnum + "Private Declare PtrSafe Sub Sleep Lib \"kernel32\" (ByVal ms As E)", DisplayName = "A Private Declare with a parameter of a private Enum")]
    [DataRow("Private Const K As Long = 1\r\nPrivate S As String * 4\r\nPrivate A() As Long\r\nPrivate Declare PtrSafe Sub Sleep Lib \"kernel32\" (ByVal ms As Long)", DisplayName = "Private constants, strings, arrays and Declares")]
    [DataRow("Const K As Long = 1", DisplayName = "A Const with no modifier, which is private")]
    [DataRow("Dim A() As Long", DisplayName = "An array declared with Dim, which is private")]
    [DataRow("Public V As Variant", DisplayName = "A Public Variant")]
    [DataRow("Public Enum PE\r\n One\r\nEnd Enum\r\nPublic V As PE", DisplayName = "A Public Enum, and a Public variable of it")]
    public async Task AClassModule_ThatExposesNothingItCannot_HasNoError(string declarations)
        => CollectionAssert.AreEqual(Array.Empty<string>(), await InAClassModule(declarations));

    // MS-VBAL §5.3.1.5 refuses a standard module's public type as a parameter of a public procedure or an event of a class; MS-VBA does not.
    [TestMethod]
    [DataRow("Public Sub S(ByRef x As PT)\r\nEnd Sub", DisplayName = "A parameter of a Public Sub")]
    [DataRow("Public Event Moved(ByRef x As PT)", DisplayName = "A parameter of an Event")]
    [DataRow("Public Function F() As PT\r\nEnd Function", DisplayName = "The result of a Public Function")]
    public async Task APublicMemberOfAClassModule_DeclaredWithAStandardModulesPublicType_IsNoError(string declarations)
        => CollectionAssert.AreEqual(Array.Empty<string>(), await InAClassModule(declarations, PublicType));
}
