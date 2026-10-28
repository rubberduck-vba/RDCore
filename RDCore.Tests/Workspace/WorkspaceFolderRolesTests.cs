using RDCore.SDK.Workspace;

namespace RDCore.Tests.Workspace;

/// <summary>
/// Which folders of a workspace are projects and which hold the sources a project lists: the first folder is a project; a folder that an imported project lists files
/// in is a source folder, whatever it holds; a folder with a project of its own is a project; and a folder that holds none is judged by what the projects before it list.
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

    private static WorkspaceFolderRoles Roles(string[] folders, string[]? imported = null, string[]? withProject = null)
        => new(new WorkspaceFolders(folders), imported ?? [], withProject ?? []);

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
    public void AFolderWithAProjectOfItsOwn_IsAProject_WhateverAProjectBeforeItLists()
        => CollectionAssert.AreEqual(
            new[] { WorkspaceFolderRole.Project, WorkspaceFolderRole.Project }, RolesOf(Roles([App, Shared], withProject: [Shared]), AppListsShared),
            "a native project that a project lists a file of is still a project");

    [TestMethod]
    public void AFolderAnImportedProjectListsFilesIn_IsASourceFolder_EvenWhenItHoldsAProjectOfItsOwn()
        => CollectionAssert.AreEqual(
            new[] { WorkspaceFolderRole.Project, WorkspaceFolderRole.Source }, RolesOf(Roles([App, Shared], imported: [App], withProject: [App, Shared]), AppListsShared),
            "its .rdproj is ignored");

    [TestMethod]
    public void ANativeProjectFolder_AfterAnImportedProjectsFolder_StaysAProject()
    {
        // App was imported and lists nothing in Other; Other has a project of its own.
        IEnumerable<string> Listed(string root) => root == App ? [Path.Combine(App, "Main.bas")] : [];

        CollectionAssert.AreEqual(
            new[] { WorkspaceFolderRole.Project, WorkspaceFolderRole.Project }, RolesOf(Roles([App, Other], imported: [App], withProject: [App, Other]), Listed));
    }

    [TestMethod]
    public void AFolderWithNoProject_AnImportedProjectListsNothingIn_IsJudgedByOrder()
    {
        IEnumerable<string> Listed(string root) => root == App ? [Path.Combine(App, "Main.bas")] : [];

        CollectionAssert.AreEqual(
            new[] { WorkspaceFolderRole.Project, WorkspaceFolderRole.Project }, RolesOf(Roles([App, Other], imported: [App], withProject: [App]), Listed),
            "it is not a source folder because a project was imported elsewhere");
    }

    [TestMethod]
    public void TheFirstFolder_IsAlwaysAProject()
        => CollectionAssert.AreEqual(
            new[] { WorkspaceFolderRole.Project, WorkspaceFolderRole.Project },
            RolesOf(Roles([App, Other], imported: [Other], withProject: [Other]), root => root == Other ? [Path.Combine(App, "X.bas")] : []),
            "an imported project that comes after it lists files in it, and it stays the folder the server runs");

    [TestMethod]
    public void AnImportedProject_IsTheFolderOfTheWorkspaceThatHoldsIt()
    {
        // the .vbp is in a subfolder of the workspace folder the client opened: the folder that holds it is the imported project's folder.
        var roles = Roles([App, Shared, Other], imported: [Path.Combine(Shared, "vbp")], withProject: [Shared]);

        CollectionAssert.AreEqual(
            new[] { WorkspaceFolderRole.Project, WorkspaceFolderRole.Project, WorkspaceFolderRole.Source },
            RolesOf(roles, root => root == Shared ? [Path.Combine(Other, "X.bas")] : []),
            "Other is listed by the imported project in Shared");
    }

    [TestMethod]
    public void AProjectNoFolderOfTheWorkspaceHolds_IsIgnored()
        => CollectionAssert.AreEqual(
            new[] { WorkspaceFolderRole.Project, WorkspaceFolderRole.Source }, RolesOf(Roles([App, Shared], imported: [Path.Combine(Base, "Elsewhere")]), AppListsShared));

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
