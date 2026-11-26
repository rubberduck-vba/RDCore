namespace RDCore.Tests.Cli;

/// <summary>
/// A program a test runs in a <see cref="ModuleWorkspace"/> starts in the workspace's folder on disk, as a session starts in its workspace root: a relative
/// path in its file statements is a file of that folder, and never one of the folder the tests run from.
/// </summary>
[TestClass]
public sealed class ModuleWorkspaceRootTests
{
    private static readonly (string Name, string Source)[] NoClasses = [];

    [TestMethod]
    public async Task ARelativeOpen_WritesItsFileInTheWorkspaceFolder_NotWhereTheTestsRun()
    {
        var name = "relative-" + Guid.NewGuid().ToString("N") + ".txt";
        var inWorkspace = Path.Combine(ModuleWorkspace.Root, name);
        var whereTheTestsRun = Path.Combine(Directory.GetCurrentDirectory(), name);
        try
        {
            var printed = await ModuleWorkspace.RunAsync(NoClasses, string.Join("\r\n",
                "Public Sub Main()",
                $"    Open \"{name}\" For Output As #1",
                "    Print #1, \"x\"",
                "    Close #1",
                "    Debug.Print Err.Number",
                "End Sub",
                ""));

            CollectionAssert.AreEqual(new[] { "0" }, printed, string.Join(" | ", printed));
            Assert.IsTrue(File.Exists(inWorkspace), $"the file is in the workspace's folder, {ModuleWorkspace.Root}");
            Assert.IsFalse(File.Exists(whereTheTestsRun), "the file is not in the folder the tests run from");
        }
        finally
        {
            // either may be missing, and so may the folder of the first.
            foreach (var file in new[] { inWorkspace, whereTheTestsRun }.Where(File.Exists))
            {
                File.Delete(file);
            }
        }
    }
}
