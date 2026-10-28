using RDCore.SDK.Workspace;

namespace RDCore.Tests.Workspace;

/// <summary>
/// The folders of a workspace are its projects and its bounds: a file is the project's whose folder holds it most closely, and a file no
/// folder holds - a <c>..\</c> path that climbs out of every one - is outside the workspace.
/// </summary>
[TestClass]
public sealed class WorkspaceFoldersTests
{
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "rdcore-folders");
    private static readonly string App = Path.Combine(Base, "App");
    private static readonly string Shared = Path.Combine(Base, "Shared");

    [TestMethod]
    public void AFile_IsTheProjectsWhoseFolderHoldsIt()
    {
        var folders = new WorkspaceFolders([App, Shared]);

        Assert.AreEqual(App, folders.RootOf(Path.Combine(App, "src", "Main.bas")));
        Assert.AreEqual(Shared, folders.RootOf(Path.Combine(Shared, "Util.cls")));
    }

    [TestMethod]
    public void APathThatClimbsOutOfEveryFolder_IsOutsideTheWorkspace()
    {
        var folders = new WorkspaceFolders([App]);

        // the .vbp's ..\Shared\Util.cls, when the client opened App alone.
        Assert.IsNull(folders.RootOf(Path.Combine(App, "..", "Shared", "Util.cls")));
        Assert.IsFalse(folders.Contains(Path.Combine(Base, "Elsewhere.bas")));
    }

    [TestMethod]
    public void APathThatClimbsIntoAnotherFolder_IsThatFoldersProject()
        => Assert.AreEqual(Shared, new WorkspaceFolders([App, Shared]).RootOf(Path.Combine(App, "..", "Shared", "Util.cls")));

    [TestMethod]
    public void AFolderNamedLikeTheStartOfAnother_DoesNotHoldIt()
        => Assert.IsNull(new WorkspaceFolders([App]).RootOf(Path.Combine(Base, "Application", "X.bas")));

    [TestMethod]
    public void AFolderInsideAnother_IsAProjectOfItsOwn_WhenBothAreGiven()
    {
        var tools = Path.Combine(App, "Tools");
        var folders = new WorkspaceFolders([App, tools]);

        Assert.AreEqual(tools, folders.RootOf(Path.Combine(tools, "Build.bas")));
        Assert.AreEqual(App, folders.RootOf(Path.Combine(App, "Main.bas")));
    }

    [TestMethod]
    public void TheFolders_AreInTheOrderTheClientGaveThem_EachOnce()
    {
        var folders = new WorkspaceFolders([Shared, App + Path.DirectorySeparatorChar, Shared]);

        CollectionAssert.AreEqual(new[] { Shared, App }, folders.Roots.ToArray());
    }
}
