using RDCore.SDK.Runtime.Libraries;
using System.IO.Abstractions;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Runtime.StdLib.Information;

/// <summary>
/// <strong>MS-VBAL §6.1.2.7.1.12 TypeName</strong> and <strong>§6.1.2.7.1.13 VarType</strong> of an object of a referenced library: the
/// name its library gives its class, and the subtype of an object whose default member takes an argument it is not given. These need the
/// servers of a Windows machine, and are skipped everywhere else.
/// </summary>
/// <remarks>
/// Each expected value is what MS-VBA printed for the same objects, in Excel.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.7 Information")]
[OSCondition(OperatingSystems.Windows)]
public sealed class AutomationObjectTypeTests
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
    [DataRow("Scripting.Dictionary", "Dictionary")]
    [DataRow("Scripting.FileSystemObject", "FileSystemObject")]
    public async Task OfALibraryObject_IsItsClassName_AndAnObject(string className, string typeName)
        => CollectionAssert.AreEqual(new[] { typeName, "9", "True", "False", "False" }, await Run(
            "Dim o As Object",
            $"Set o = New {className}",
            "Debug.Print TypeName(o)",
            // a Dictionary's default member is Item, which takes the key it reads: VarType cannot call it, and the object is an object.
            "Debug.Print VarType(o)",
            "Debug.Print IsObject(o)",
            "Debug.Print IsNumeric(o)",
            "Debug.Print IsEmpty(o)"));
}
