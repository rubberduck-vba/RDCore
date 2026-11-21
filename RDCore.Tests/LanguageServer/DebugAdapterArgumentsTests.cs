using OmniSharp.Extensions.LanguageServer.Protocol;
using RDCore.LanguageServer.Debugging;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The workspace a debug adapter is started with: a relative one is read against the current directory, the folder the adapter was started in.
/// </summary>
[TestClass]
public sealed class DebugAdapterArgumentsTests
{
    // the root the components of the platform are told: a URI's path, with the drive of a Windows path in lower case.
    private static string Root(string path) => ((DocumentUri)Path.GetFullPath(path)).GetFileSystemPath();

    [TestMethod]
    [DataRow(".", DisplayName = "the current folder")]
    [DataRow("Ledger", DisplayName = "a folder in the current one")]
    public void ARelativeWorkspace_IsTheFolderItNamesInTheCurrentDirectory(string workspace)
        // read as a URI, "." would be the root of the drive, and "Ledger" a folder there.
        => Assert.AreEqual(Root(workspace), DebugAdapterHost.Arguments(["--workspace", workspace]).WorkspaceUri);

    [TestMethod]
    public void AWorkspaceURI_IsTheFolderItNames()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Ledger");

        Assert.AreEqual(Root(folder), DebugAdapterHost.Arguments(["--workspace", new Uri(folder).AbsoluteUri]).WorkspaceUri);
    }
}
