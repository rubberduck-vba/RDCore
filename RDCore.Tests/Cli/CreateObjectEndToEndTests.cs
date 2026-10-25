using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>CreateObject</c> and <c>GetObject</c> against the servers of the machine, in a project that references no library: the objects are late-bound, as MS-VBA
/// has them (recorded in Excel, oracle\s25). The servers are a Windows machine's; elsewhere there are none, and a program that asks for one is told so.
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
    public async Task ADictionaryNoLibraryDeclares_IsCreated_AndCalledByName()
        => CollectionAssert.AreEqual(new[] { "Dictionary", "9", "2", "10", "True", "False" }, await Run(
            "Dim d As Object",
            "Set d = CreateObject(\"scripting.dictionary\")",
            "Debug.Print TypeName(d)",
            "Debug.Print VarType(d)",
            "d.Add \"a\", 10",
            "d.Item(\"b\") = 20",
            "Debug.Print d.Count",
            "Debug.Print d(\"a\")",
            "Debug.Print d.Exists(\"b\")",
            "Debug.Print d.Exists(\"c\")"));

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task ForEach_OverALateBoundDictionary_IsOverItsKeys()
        => CollectionAssert.AreEqual(new[] { "a", "b" }, await Run(
            "Dim d As Object",
            "Dim key As Variant",
            "Set d = CreateObject(\"Scripting.Dictionary\")",
            "d.Add \"a\", 1",
            "d.Add \"b\", 2",
            "For Each key In d",
            "Debug.Print key",
            "Next"));

    // the error numbers MS-VBA raises (oracle\s25 grid-co): a class there is no server of, a member the object has not, an object assigned without Set.
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task WhatCannotBeCreatedOrCalled_IsTheErrorMSVBARaises()
        => CollectionAssert.AreEqual(new[] { "429", "429", "429", "438", "450" }, await Run(
            "Dim d As Object",
            "Dim v As Variant",
            "On Error Resume Next",
            "Set d = CreateObject(\"No.Such\")",
            "Debug.Print Err.Number",
            "Err.Clear",
            "Set d = CreateObject(\"\")",
            "Debug.Print Err.Number",
            "Err.Clear",
            "Set d = CreateObject(\"Dictionary\")",
            "Debug.Print Err.Number",
            "Err.Clear",
            "Set d = CreateObject(\"Scripting.Dictionary\")",
            "d.Nope",
            "Debug.Print Err.Number",
            "Err.Clear",
            "v = CreateObject(\"Scripting.Dictionary\")",
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
            "Set o = GetObject(, \"Scripting.Dictionary\")",
            "Debug.Print Err.Number",
            "Err.Clear",
            $"Set o = GetObject(\"{missing}\")",
            "Debug.Print Err.Number"));
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public async Task OnAPlatformWithNoAutomationServers_CreateObjectCannotCreateTheObject()
        => CollectionAssert.AreEqual(new[] { "429" }, await Run(
            "Dim d As Object",
            "On Error Resume Next",
            "Set d = CreateObject(\"Scripting.Dictionary\")",
            "Debug.Print Err.Number"));
}
