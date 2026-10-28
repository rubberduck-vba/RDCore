using RDCore.SDK.Workspace;

namespace RDCore.Tests.Workspace;

/// <summary>
/// Which folders of a workspace are projects and which hold the sources a project lists: the first folder is a project, and so is a folder with a <c>.rdproj</c> of its
/// own, whatever lists files in it; a folder with none is a source folder when a project before it lists files in it.
/// </summary>
[TestClass]
public sealed class WorkspaceFolderRolesTests
{
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "rdcore-roles");
    private static readonly string App = Path.Combine(Base, "App");
    private static readonly string Shared = Path.Combine(Base, "Shared");
    private static readonly string Other = Path.Combine(Base, "Other");

    // App lists a file of Shared, as a .vbp that lists ..\Shared\Util.bas does.
    private static IEnumerable<string> AppListsShared(string root)
        => root == App ? [Path.Combine(App, "Main.bas"), Path.Combine(Shared, "Util.bas")] : [];

    private static WorkspaceFolderRole[] RolesOf(WorkspaceFolderRoles roles, Func<string, IEnumerable<string>> listed)
        => [.. roles.Roles(listed).Select(entry => entry.Role)];

    private static WorkspaceFolderRoles Roles(string[] folders, string[]? withProject = null) => new(new WorkspaceFolders(folders), withProject ?? []);

    [TestMethod]
    public void ALaterFolderAProjectListsFilesIn_IsASourceFolder_WhenItHoldsNoProject()
        => CollectionAssert.AreEqual(new[] { WorkspaceFolderRole.Project, WorkspaceFolderRole.Source }, RolesOf(Roles([App, Shared]), AppListsShared));

    [TestMethod]
    public void ALaterFolderNoProjectListsFilesIn_IsAProject()
        => CollectionAssert.AreEqual(new[] { WorkspaceFolderRole.Project, WorkspaceFolderRole.Project }, RolesOf(Roles([App, Other]), AppListsShared));

    [TestMethod]
    public void OnlyAProjectThatComesBeforeAFolder_SaysWhatItIs()
        => CollectionAssert.AreEqual(new[] { WorkspaceFolderRole.Project, WorkspaceFolderRole.Project }, RolesOf(Roles([Shared, App]), AppListsShared));

    [TestMethod]
    public void ASourceFolder_ListsNothingOfAnother()
    {
        // Shared is a source folder; what it lists, if it held a .rdproj, is nobody's: the third folder is a project.
        IEnumerable<string> Listed(string root) => root == App ? [Path.Combine(Shared, "Util.bas")] : root == Shared ? [Path.Combine(Other, "X.bas")] : [];

        CollectionAssert.AreEqual(
            new[] { WorkspaceFolderRole.Project, WorkspaceFolderRole.Source, WorkspaceFolderRole.Project }, RolesOf(Roles([App, Shared, Other]), Listed));
    }

    [TestMethod]
    public void AFolderWithAProjectOfItsOwn_IsAProject_EvenWhenAProjectBeforeItListsFilesIn()
        => CollectionAssert.AreEqual(
            new[] { WorkspaceFolderRole.Project, WorkspaceFolderRole.Project }, RolesOf(Roles([App, Shared], withProject: [Shared]), AppListsShared),
            "an imported project or a native one: it stays a project, and the file is a module of both");

    [TestMethod]
    public void TheFirstFolder_IsAlwaysAProject()
        => CollectionAssert.AreEqual(
            new[] { WorkspaceFolderRole.Project, WorkspaceFolderRole.Project },
            RolesOf(Roles([App, Other]), root => root == Other ? [Path.Combine(App, "X.bas")] : []),
            "a project that comes after it lists files in it, and it stays the folder the server runs");

    [TestMethod]
    public void AFolderNoFolderOfTheWorkspaceHolds_IsIgnored()
        => CollectionAssert.AreEqual(
            new[] { WorkspaceFolderRole.Project, WorkspaceFolderRole.Source }, RolesOf(Roles([App, Shared], withProject: [Path.Combine(Base, "Elsewhere")]), AppListsShared));

    [TestMethod]
    public void AGroup_OfTwoProjectsEachWithItsOutsideFolder_IsReadFolderByFolder()
    {
        // a client sends each project's folder before that project's outside folders.
        var second = Path.Combine(Base, "Second");
        var secondShared = Path.Combine(Base, "SecondShared");
        IEnumerable<string> Listed(string root)
            => root == App ? [Path.Combine(Shared, "Util.bas")] : root == second ? [Path.Combine(secondShared, "Util.bas")] : [];

        CollectionAssert.AreEqual(
            new[] { WorkspaceFolderRole.Project, WorkspaceFolderRole.Source, WorkspaceFolderRole.Project, WorkspaceFolderRole.Source },
            RolesOf(Roles([App, Shared, second, secondShared]), Listed));
    }
}
