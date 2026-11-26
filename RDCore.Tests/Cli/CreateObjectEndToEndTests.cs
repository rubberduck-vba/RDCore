using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>CreateObject</c> and <c>GetObject</c> against the servers of the machine, in a project that references no library: the objects are late-bound, as MS-VBA
/// has them (observed in Excel). The servers are a Windows machine's; elsewhere there are none, and a program that asks for one is told so. The classes are
/// ADO's and WMI's, which every Windows machine has.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.8.1.4 CreateObject")]
[TestCategory("MS-VBAL 6.1.2.8.1.9 GetObject")]
public sealed class CreateObjectEndToEndTests
{
    private static Task<string[]> Run(params string[] lines)
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task AnObjectNoLibraryDeclares_IsCreated_AndCalledByName()
        => CollectionAssert.AreEqual(new[] { "Connection", "True", "0", "20" }, await Run(
            "Dim c As Object",
            "Set c = CreateObject(\"adodb.connection\")",
            "Debug.Print TypeName(c)",
            "Debug.Print IsObject(c)",
            "Debug.Print c.State",
            "c.ConnectionTimeout = 20",
            "Debug.Print c.ConnectionTimeout"));

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task ForEach_OverALateBoundCollection_IsOverWhatItEnumerates()
        => CollectionAssert.AreEqual(new[] { "a", "b" }, await Run(
            "Dim r As Object",
            "Dim f As Variant",
            "Set r = CreateObject(\"ADODB.Recordset\")",
            "r.Fields.Append \"a\", 200, 10",
            "r.Fields.Append \"b\", 200, 10",
            "For Each f In r.Fields",
            "Debug.Print f.Name",
            "Next"));

    // the error numbers MS-VBA raises (observed in Excel): a class there is no server of, a member the object has not, an object assigned without Set whose
    // default member (WMI's Item) takes an argument.
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task WhatCannotBeCreatedOrCalled_IsTheErrorMSVBARaises()
        => CollectionAssert.AreEqual(new[] { "429", "429", "429", "438", "450" }, await Run(
            "Dim o As Object",
            "Dim v As Variant",
            "On Error Resume Next",
            "Set o = CreateObject(\"No.Such\")",
            "Debug.Print Err.Number",
            "Err.Clear",
            "Set o = CreateObject(\"\")",
            "Debug.Print Err.Number",
            "Err.Clear",
            "Set o = CreateObject(\"Dictionary\")",
            "Debug.Print Err.Number",
            "Err.Clear",
            "Set o = CreateObject(\"ADODB.Recordset\")",
            "o.Nope",
            "Debug.Print Err.Number",
            "Err.Clear",
            "v = CreateObject(\"WbemScripting.SWbemNamedValueSet\")",
            "Debug.Print Err.Number"));

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task GetObject_OfAMoniker_IsTheObjectItNames()
        => CollectionAssert.AreEqual(new[] { "SWbemServicesEx" }, await Run(
            "Debug.Print TypeName(GetObject(\"winmgmts:\"))"));

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task GetObject_OfAClassThatIsNotRunning_OrAFileThatIsNotThere_IsTheErrorMSVBARaises()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");
        CollectionAssert.AreEqual(new[] { "429", "432" }, await Run(
            "Dim o As Object",
            "On Error Resume Next",
            "Set o = GetObject(, \"ADODB.Connection\")",
            "Debug.Print Err.Number",
            "Err.Clear",
            $"Set o = GetObject(\"{missing}\")",
            "Debug.Print Err.Number"));
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public async Task OnAPlatformWithNoAutomationServers_CreateObjectCannotCreateTheObject()
        => CollectionAssert.AreEqual(new[] { "429" }, await Run(
            "Dim o As Object",
            "On Error Resume Next",
            "Set o = CreateObject(\"ADODB.Connection\")",
            "Debug.Print Err.Number"));
}
