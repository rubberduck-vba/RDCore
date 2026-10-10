using RDCore.SDK.Runtime.Libraries;
using System.IO.Abstractions;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A program that references a library runs against the real thing: the objects of <c>Scripting</c> are the machine's own, created, called and let go of
/// through the whole pipeline. These need the servers of a Windows machine, and are skipped everywhere else.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §2.3.1.2 References")]
[OSCondition(OperatingSystems.Windows)]
public sealed class AutomationEndToEndTests
{
    private static WorkspaceLibraries Scripting
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RDCore.slnx")))
            {
                directory = directory.Parent;
            }

            return new(["Scripting"], new DirectoryLibrarySource(new FileSystem(), Path.Combine(directory!.FullName, "Symbols")));
        }
    }

    private static Task<string[]> Run(params string[] lines)
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n", Scripting);

    [TestMethod]
    public async Task ADictionary_IsCreated_AddedTo_AndRead()
        => CollectionAssert.AreEqual(new[] { "2", "10", "True", "False" }, await Run(
            "Dim d As New Scripting.Dictionary",
            "d.Add \"a\", 10",
            "d.Add \"b\", 20",
            "Debug.Print d.Count",
            "Debug.Print d.Item(\"a\")",
            "Debug.Print d.Exists(\"b\")",
            "Debug.Print d.Exists(\"c\")"));

    [TestMethod]
    public async Task ADictionary_IsCreatedByNew_AndAnItemIsAssigned()
        => CollectionAssert.AreEqual(new[] { "7" }, await Run(
            "Dim d As Scripting.Dictionary",
            "Set d = New Scripting.Dictionary",
            "d.Item(\"k\") = 7",
            "Debug.Print d(\"k\")"));

    [TestMethod]
    public async Task TheErrorOfAServer_IsAnErrorTheProgramCanHandle()
        => CollectionAssert.AreEqual(new[] { "457" }, await Run(
            "Dim d As New Scripting.Dictionary",
            "d.Add \"a\", 1",
            "On Error Resume Next",
            "d.Add \"a\", 2",
            "Debug.Print Err.Number"));
}
