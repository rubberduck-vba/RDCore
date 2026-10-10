using RDCore.Tests.Runtime.Libraries;
using static RDCore.Tests.Cli.ModuleWorkspace;
using static RDCore.Tests.Runtime.Libraries.LibraryFixtures;

namespace RDCore.Tests.Cli;

/// <summary>
/// The members of an enumeration that a library or the standard library states are values a program reads, as it reads its own constants
/// (<strong>MS-VBAL §5.2.3.4</strong>).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.3.4 Enum Declarations")]
public sealed class LibraryEnumValueTests
{
    private static string Program(params string[] lines)
        => $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n";

    [TestMethod]
    public async Task AMemberOfALibrarysEnumeration_HasTheValueTheLibraryStates()
    {
        var output = await RunAsync([], Program("Debug.Print wgOn", "Debug.Print wgOff", "Debug.Print wgOn + 1"),
            new WorkspaceLibraries(["Widgets"], new InMemoryLibrarySource(Widgets)));

        CollectionAssert.AreEqual(new[] { "1", "0", "2" }, output);
    }

    [TestMethod]
    [DataRow("WidgetMode.wgOn", DisplayName = "by the name of its enumeration")]
    [DataRow("Widgets.wgOn", DisplayName = "by the name of its library")]
    [DataRow("Widgets.WidgetMode.wgOn", DisplayName = "by both")]
    public async Task AMemberOfALibrarysEnumeration_HasItsValue_Qualified(string expression)
    {
        var output = await RunAsync([], Program($"Debug.Print {expression}"), new WorkspaceLibraries(["Widgets"], new InMemoryLibrarySource(Widgets)));

        CollectionAssert.AreEqual(new[] { "1" }, output);
    }

    [TestMethod]
    public async Task AMemberOfTheStandardLibrarysEnumeration_HasItsValue()
        => CollectionAssert.AreEqual(new[] { "1", "5" }, await RunAsync([], Program("Debug.Print vbSunday", "Debug.Print vbThursday")));
}
