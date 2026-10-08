namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Declare</c>d procedure is external: a call to it goes through the interceptors, and the environment is not obliged to have a provider for it.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.4.3 External Procedure Declarations")]
public sealed class DeclareCallTests
{
    private static string Program(params string[] lines)
        => "Attribute VB_Name = \"Program\"\r\n"
            + "Private Declare PtrSafe Function GetTickCount Lib \"kernel32\" () As Long\r\n"
            + "Private Declare PtrSafe Sub Sleep Lib \"kernel32\" Alias \"SleepEx\" (ByVal ms As Long)\r\n"
            + "Public Sub Main()\r\n" + string.Join("\r\n", lines) + "\r\nEnd Sub\r\n";

    [TestMethod]
    public async Task AModuleThatDeclaresAnExternalProcedure_Loads()
        => CollectionAssert.AreEqual(Array.Empty<string>(), await ModuleWorkspace.LoadErrorsAsync([], Program("Debug.Print 1")));

    [TestMethod]
    public async Task ACallToAnExternalFunction_NobodyProvides_IsError48()
        => CollectionAssert.AreEqual(new[] { "48" }, await ModuleWorkspace.RunAsync([], Program(
            "On Error Resume Next",
            "Dim t As Long",
            "t = GetTickCount()",
            "Debug.Print Err.Number")));

    [TestMethod]
    public async Task ACallToAnExternalSub_NobodyProvides_IsError48()
        => CollectionAssert.AreEqual(new[] { "48" }, await ModuleWorkspace.RunAsync([], Program(
            "On Error Resume Next",
            "Sleep 10",
            "Debug.Print Err.Number")));

    [TestMethod]
    public async Task TheDescriptionOfThatError_IsTheOneOfError48()
        => CollectionAssert.AreEqual(new[] { "Error in loading DLL" }, await ModuleWorkspace.RunAsync([], Program(
            "On Error Resume Next",
            "Sleep 10",
            "Debug.Print Err.Description")));
}
