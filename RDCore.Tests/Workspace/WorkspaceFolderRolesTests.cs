using RDCore.SDK.Workspace;

namespace RDCore.Tests.Workspace;

/// <summary>
/// Which folders of a workspace are projects and which hold the sources a project lists (#252, desk #30): the import names the project, and a client that never
/// imports is read by what the projects list.
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

    [TestMethod]
    public void WithoutAnImport_TheFirstFolderIsAProject_AndALaterOneAProjectListsFilesIn_IsASourceFolder()
    {
        var roles = new WorkspaceFolderRoles(new WorkspaceFolders([App, Shared]), []);

        CollectionAssert.AreEqual(new[] { WorkspaceFolderRole.Project, WorkspaceFolderRole.Source }, RolesOf(roles, AppListsShared));
    }

    [TestMethod]
    public void WithoutAnImport_ALaterFolderNoProjectListsFilesIn_IsAProject()
    {
        var roles = new WorkspaceFolderRoles(new WorkspaceFolders([App, Other]), []);

        CollectionAssert.AreEqual(new[] { WorkspaceFolderRole.Project, WorkspaceFolderRole.Project }, RolesOf(roles, AppListsShared));
    }

    [TestMethod]
    public void WithoutAnImport_AProjectListsNothingOfAFolderBeforeIt_SoItIsAProject()
    {
        // Shared comes first, and App after it: only a project that comes before a folder says what the folder is.
        var roles = new WorkspaceFolderRoles(new WorkspaceFolders([Shared, App]), []);

        CollectionAssert.AreEqual(new[] { WorkspaceFolderRole.Project, WorkspaceFolderRole.Project }, RolesOf(roles, AppListsShared));
    }

    [TestMethod]
    public void WithoutAnImport_ASourceFolderListsNothingOfAnother()
    {
        // Shared is a source folder; what it lists, if it held a .rdproj, is nobody's: the third folder is a project.
        var roles = new WorkspaceFolderRoles(new WorkspaceFolders([App, Shared, Other]), []);
        IEnumerable<string> Listed(string root) => root == App ? [Path.Combine(Shared, "Util.bas")] : root == Shared ? [Path.Combine(Other, "X.bas")] : [];

        CollectionAssert.AreEqual(new[] { WorkspaceFolderRole.Project, WorkspaceFolderRole.Source, WorkspaceFolderRole.Project }, RolesOf(roles, Listed));
    }

    [TestMethod]
    public void AnImport_NamesTheProjects_AndEveryOtherFolderIsASourceFolder()
    {
        var roles = new WorkspaceFolderRoles(new WorkspaceFolders([App, Shared, Other]), [Other]);

        CollectionAssert.AreEqual(
            new[] { WorkspaceFolderRole.Source, WorkspaceFolderRole.Source, WorkspaceFolderRole.Project },
            RolesOf(roles, _ => throw new AssertFailedException("what the projects list is not asked once the client has said which folders are projects")));
    }

    [TestMethod]
    public void AnImport_CorrectsTheGuessOfTheFirstFolderBeingAProject()
    {
        // the guess says App is a project and Other too; the import says the .vbp is in Other, and App holds sources only.
        var folders = new WorkspaceFolders([App, Other]);
        IEnumerable<string> Listed(string root) => [];

        CollectionAssert.AreEqual(new[] { WorkspaceFolderRole.Project, WorkspaceFolderRole.Project }, RolesOf(new WorkspaceFolderRoles(folders, []), Listed));
        CollectionAssert.AreEqual(new[] { WorkspaceFolderRole.Source, WorkspaceFolderRole.Project }, RolesOf(new WorkspaceFolderRoles(folders, [Other]), Listed));
    }

    [TestMethod]
    public void AnImportedProject_IsTheFolderOfTheWorkspaceThatHoldsIt()
    {
        // the .vbp is in a subfolder of the workspace folder the client opened: the folder that holds it is the project folder.
        var roles = new WorkspaceFolderRoles(new WorkspaceFolders([App, Shared]), [Path.Combine(Shared, "vbp")]);

        CollectionAssert.AreEqual(new[] { WorkspaceFolderRole.Source, WorkspaceFolderRole.Project }, RolesOf(roles, _ => []));
    }

    [TestMethod]
    public void AnImportedProject_NoFolderOfTheWorkspaceHolds_IsIgnored()
    {
        var roles = new WorkspaceFolderRoles(new WorkspaceFolders([App, Shared]), [Path.Combine(Base, "Elsewhere")]);

        CollectionAssert.AreEqual(new[] { WorkspaceFolderRole.Project, WorkspaceFolderRole.Source }, RolesOf(roles, AppListsShared),
            "it names no folder of the workspace, so the client has imported nothing that counts");
    }

    [TestMethod]
    public void AGroup_OfTwoProjectsEachWithItsOutsideFolder_IsReadFolderByFolder()
    {
        // HexIDE sends each .vbp's folder before that project's outside folders.
        var second = Path.Combine(Base, "Second");
        var secondShared = Path.Combine(Base, "SecondShared");
        var roles = new WorkspaceFolderRoles(new WorkspaceFolders([App, Shared, second, secondShared]), []);
        IEnumerable<string> Listed(string root)
            => root == App ? [Path.Combine(Shared, "Util.bas")] : root == second ? [Path.Combine(secondShared, "Util.bas")] : [];

        CollectionAssert.AreEqual(
            new[] { WorkspaceFolderRole.Project, WorkspaceFolderRole.Source, WorkspaceFolderRole.Project, WorkspaceFolderRole.Source },
            RolesOf(roles, Listed));
    }
}
