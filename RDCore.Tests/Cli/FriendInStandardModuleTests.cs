using RDCore.Runtime.LanguageCore;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.3.1.1</strong>: <c>Friend</c> declares a procedure of a class module. A standard module's procedure declared <c>Friend</c> is the
/// compile error MS-VBA gives it, <em>Only valid in object module</em>.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.3.1.1 Procedure Scope")]
public sealed class FriendInStandardModuleTests
{
    [TestMethod]
    [DataRow("Friend Sub S()\r\nEnd Sub", DisplayName = "a Friend Sub")]
    [DataRow("Friend Function F() As Long\r\nEnd Function", DisplayName = "a Friend Function")]
    [DataRow("Friend Property Get P() As Long\r\nEnd Property", DisplayName = "a Friend Property Get")]
    [DataRow("Friend Property Let P(ByVal v As Long)\r\nEnd Property", DisplayName = "a Friend Property Let")]
    [DataRow("Friend Property Set P(ByVal v As Object)\r\nEnd Property", DisplayName = "a Friend Property Set")]
    public async Task AFriendProcedureOfAStandardModule_IsOnlyValidInAnObjectModule(string declaration)
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([], "Attribute VB_Name = \"Program\"\r\n" + declaration + "\r\n");

        var error = Assert.ContainsSingle(errors);
        StringAssert.Contains(error, Exceptions.VBCompileError_OnlyValidInObjectModule);
    }

    [TestMethod]
    [DataRow("Friend Sub S()\r\nEnd Sub", DisplayName = "a Friend Sub")]
    [DataRow("Friend Function F() As Long\r\nEnd Function", DisplayName = "a Friend Function")]
    [DataRow("Friend Property Get P() As Long\r\nEnd Property", DisplayName = "a Friend Property Get")]
    public async Task AFriendProcedureOfAClassModule_IsNoError(string declaration)
        => CollectionAssert.AreEqual(Array.Empty<string>(), await ModuleWorkspace.LoadErrorsAsync(
            [("Widget", ModuleWorkspace.ClassModule("Widget", declaration))], "Attribute VB_Name = \"Program\"\r\n"));

    [TestMethod]
    [DataRow("Public Sub S()\r\nEnd Sub", DisplayName = "a Public Sub")]
    [DataRow("Private Function F() As Long\r\nEnd Function", DisplayName = "a Private Function")]
    [DataRow("Sub S()\r\nEnd Sub", DisplayName = "a Sub with no modifier")]
    public async Task AProcedureOfAStandardModule_NotDeclaredFriend_IsNoError(string declaration)
        => CollectionAssert.AreEqual(
            Array.Empty<string>(), await ModuleWorkspace.LoadErrorsAsync([], "Attribute VB_Name = \"Program\"\r\n" + declaration + "\r\n"));
}
