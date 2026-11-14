using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A late-bound object is called through what it answers to, <c>IDispatch</c>, as MS-VBA calls it: an error value it holds or returns is an error value,
/// a named argument reaches the parameter of its name, and a failure is the error the server describes. The servers are a Windows machine's.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.13.2 Simple Name Expressions")]
[TestCategory("MS-VBAL 5.6.16.8 Named Arguments")]
[OSCondition(OperatingSystems.Windows)]
public sealed class LateBoundDispatchEndToEndTests
{
    private static Task<string[]> Run(params string[] lines)
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

    [TestMethod]
    public async Task AnErrorValueAServerHolds_IsReadBackAsAnErrorValue()
        => CollectionAssert.AreEqual(new[] { "True", "10" }, await Run(
            "Dim d As Object",
            "Set d = CreateObject(\"Scripting.Dictionary\")",
            "d.Add \"k\", CVErr(2007)",
            "Debug.Print IsError(d(\"k\"))",
            "Debug.Print VarType(d(\"k\"))"));

    [TestMethod]
    public async Task AnErrorValueInAnArrayAServerReturns_IsAnErrorValue()
        => CollectionAssert.AreEqual(new[] { "True", "False", "10" }, await Run(
            "Dim d As Object",
            "Dim v As Variant",
            "Set d = CreateObject(\"Scripting.Dictionary\")",
            "d.Add \"a\", CVErr(2042)",
            "d.Add \"b\", 1",
            "v = d.Items",
            "Debug.Print IsError(v(0))",
            "Debug.Print IsError(v(1))",
            "Debug.Print VarType(v(0))"));

    // a dictionary enumerates its keys through IEnumVARIANT, one of which is an error value.
    [TestMethod]
    public async Task ForEach_OverALateBoundObject_GivesTheErrorValuesItEnumerates()
        => CollectionAssert.AreEqual(new[] { "True", "False" }, await Run(
            "Dim d As Object",
            "Dim key As Variant",
            "Set d = CreateObject(\"Scripting.Dictionary\")",
            "d.Add CVErr(2042), 1",
            "d.Add \"b\", 2",
            "For Each key In d",
            "Debug.Print IsError(key)",
            "Next"));

    [TestMethod]
    public async Task ANamedArgument_ReachesTheParameterOfItsName_InAnyOrder()
        => CollectionAssert.AreEqual(new[] { "2", "1", "2", "3" }, await Run(
            "Dim d As Object",
            "Set d = CreateObject(\"Scripting.Dictionary\")",
            "d.Add Key:=\"a\", Item:=1",
            "d.Add Item:=2, Key:=\"b\"",
            "Debug.Print d.Count",
            "Debug.Print d(\"a\")",
            "Debug.Print d(\"b\")",
            "d.Add \"c\", Item:=3",
            "Debug.Print d(\"c\")"));

    [TestMethod]
    public async Task ANameTheMemberHasNoParameterOf_IsAnError()
        => CollectionAssert.AreEqual(new[] { "True" }, await Run(
            "Dim d As Object",
            "Set d = CreateObject(\"Scripting.Dictionary\")",
            "On Error Resume Next",
            "d.Add Nope:=\"a\", Item:=1",
            "Debug.Print Err.Number <> 0"));

    // the server describes its failure (EXCEPINFO): its number, and a description the program reads.
    [TestMethod]
    public async Task TheFailureAServerDescribes_IsTheErrorTheProgramHandles()
        => CollectionAssert.AreEqual(new[] { "457", "True" }, await Run(
            "Dim d As Object",
            "Set d = CreateObject(\"Scripting.Dictionary\")",
            "d.Add \"a\", 1",
            "On Error Resume Next",
            "d.Add \"a\", 2",
            "Debug.Print Err.Number",
            "Debug.Print Len(Err.Description) > 0"));
}
