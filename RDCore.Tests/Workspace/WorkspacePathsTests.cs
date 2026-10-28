using RDCore.SDK.Workspace;

namespace RDCore.Tests.Workspace;

/// <summary>
/// How the paths of a workspace are compared and read: without regard to case where the file system has none (Windows), and with it elsewhere.
/// </summary>
[TestClass]
public sealed class WorkspacePathsTests
{
    [TestMethod]
    public void TheComparison_AndTheComparer_AgreeOnCase()
    {
        var ignoresCase = OperatingSystem.IsWindows();

        Assert.AreEqual(ignoresCase, string.Equals("a.bas", "A.bas", WorkspacePaths.Comparison));
        Assert.AreEqual(ignoresCase, WorkspacePaths.Comparer.Equals("a.bas", "A.bas"));
    }

    [TestMethod]
    public void TwoSpellingsOfAFolder_AreTheSameFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rdcore-paths", "App");

        Assert.IsTrue(WorkspacePaths.Same(folder, folder + Path.DirectorySeparatorChar));
        Assert.IsTrue(WorkspacePaths.Same(folder, Path.Combine(folder, "..", "App")));
        Assert.IsFalse(WorkspacePaths.Same(folder, folder + "2"));
    }

    [TestMethod]
    [DataRow(@"..\Shared\Util.bas", true)]
    [DataRow("../Shared/Util.bas", true)]
    [DataRow(@"Sub\..\Util.bas", true)]
    [DataRow(@"Sub\Util.bas", false)]
    [DataRow("..Util.bas", false)]
    public void APathThatClimbs_IsOneWithADotDotSegment(string path, bool climbs) => Assert.AreEqual(climbs, WorkspacePaths.Climbs(path));
}
