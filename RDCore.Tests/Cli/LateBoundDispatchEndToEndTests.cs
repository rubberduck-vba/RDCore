using System.Globalization;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A late-bound object of a server is called through what it answers to, <c>IDispatch</c>, as MS-VBA calls it: a named argument reaches the parameter of its
/// name, and a failure is the error the server describes. The servers are ADO's and WMI's, which every Windows machine has.
/// </summary>
[TestCategory(TestCategories.Wide)]
[TestClass]
[TestCategory("MS-VBAL 5.6.13.2 Simple Name Expressions")]
[TestCategory("MS-VBAL 5.6.16.8 Named Arguments")]
[OSCondition(OperatingSystems.Windows)]
public sealed class LateBoundDispatchEndToEndTests
{
    private static Task<string[]> Run(params string[] lines)
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

    // ADO's CreateParameter(Name, Type, Direction, Size, Value); a Type of 3 is adInteger.
    [TestMethod]
    public async Task ANamedArgument_ReachesTheParameterOfItsName_InAnyOrder()
        => CollectionAssert.AreEqual(new[] { "x 3", "y 3 5" }, await Run(
            "Dim c As Object",
            "Dim p As Object",
            "Set c = CreateObject(\"ADODB.Command\")",
            "Set p = c.CreateParameter(Type:=3, Name:=\"x\")",
            "Debug.Print p.Name & \" \" & p.Type",
            "Set p = c.CreateParameter(\"y\", Value:=5, Type:=3)",
            "Debug.Print p.Name & \" \" & p.Type & \" \" & p.Value"));

    [TestMethod]
    public async Task ANameTheMemberHasNoParameterOf_IsAnError()
        => CollectionAssert.AreEqual(new[] { "True" }, await Run(
            "Dim c As Object",
            "Dim p As Object",
            "Set c = CreateObject(\"ADODB.Command\")",
            "On Error Resume Next",
            "Set p = c.CreateParameter(Nope:=\"x\", Type:=3)",
            "Debug.Print Err.Number <> 0"));

    // the server describes its failure (EXCEPINFO): its number, and a description the program reads. WMI's named values have no value of a name not added:
    // WBEM_E_NOT_FOUND.
    [TestMethod]
    public async Task TheFailureAServerDescribes_IsTheErrorTheProgramHandles()
        => CollectionAssert.AreEqual(new[] { unchecked((int)0x80041002).ToString(CultureInfo.InvariantCulture), "True" }, await Run(
            "Dim s As Object",
            "Dim v As Object",
            "Set s = CreateObject(\"WbemScripting.SWbemNamedValueSet\")",
            "On Error Resume Next",
            "Set v = s.Item(\"missing\")",
            "Debug.Print Err.Number",
            "Debug.Print Len(Err.Description) > 0"));
}
