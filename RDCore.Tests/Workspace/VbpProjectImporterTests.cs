using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Workspace;
using RDCore.Tests.Runtime.Libraries;
using System.Globalization;
using System.Text;

namespace RDCore.Tests.Workspace;

/// <summary>
/// A VB6 project file (<c>.vbp</c>) makes a <c>.rdproj</c> project: its name, its modules, classes and forms, the libraries it references by their
/// type library's identifier, and its conditional compilation arguments; what a <c>.rdproj</c> cannot say is noted.
/// </summary>
[TestClass]
public sealed class VbpProjectImporterTests
{
    private const string Vbp = """
        Type=Exe
        Reference=*\G{00020430-0000-0000-C000-000000000046}#2.0#0#C:\Windows\System32\stdole2.tlb#OLE Automation
        Reference=*\G{420B2830-E718-11CF-893D-00A0C9054228}#1.0#0#C:\Windows\System32\scrrun.dll#Microsoft Scripting Runtime
        Reference=*\G{00000000-1111-2222-3333-444444444444}#1.0#0#C:\Libraries\custom.tlb#Custom Library
        Reference=*\A..\Shared\Shared.vbp
        Object={831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0; MSCOMCTL.OCX
        Form=frmMain.frm
        Module=modMain; modMain.bas
        Class=clsThing; Classes\clsThing.cls
        Module=modShared; ..\Shared\modShared.bas
        Module=modAbsolute; C:\Elsewhere\modAbsolute.bas
        UserControl=ctlGauge.ctl
        RelatedDoc=README.txt
        ResFile32="Battleship.RES"
        Startup="Sub Main"
        Name="Battleship"
        CondComp="DEBUG = 1 : TRACE = -1"
        ExeName32="Battleship.exe"

        [MS Transaction Server]
        AutoRefresh=1
        Module=modAddIn; modAddIn.bas
        """;

    private static readonly InMemoryLibrarySource Libraries = new(
        new LibraryDescription { Name = "stdole", Origin = new LibraryOrigin { Guid = "{00020430-0000-0000-C000-000000000046}" } },
        new LibraryDescription { Name = "Scripting", Origin = new LibraryOrigin { Guid = "{420B2830-E718-11CF-893D-00A0C9054228}" } });

    private static VbpImport Import(string vbp = Vbp) => VbpProjectImporter.Import(vbp.Replace("\n", "\r\n"), "Battleship.vbp", Libraries);

    [TestMethod]
    public void TheProject_IsNamed_AsTheVbpNamesIt()
        => Assert.AreEqual("Battleship", Import().Project.Name);

    [TestMethod]
    public void AProjectThatNamesItselfNothing_IsNamedForItsFile()
        => Assert.AreEqual("Battleship", Import("Module=modMain; modMain.bas").Project.Name);

    // the .vbp's folder, as the language server and rdc import know it: an absolute path the CI's Linux runner has too.
    private static readonly string MyProj = Path.Combine(Path.GetTempPath(), "src", "MyProj");

    private static string[] ModulesOf(string vbp, string? folder)
        => [.. VbpProjectImporter.Import(vbp, "MyProj.vbp", Libraries, folder).Project.Modules.Select(module => module.RelativeUri)];

    // a path is resolved against the .vbp's folder before it is judged: one that climbs out and comes back in is the project's own file.
    [TestMethod]
    [DataRow(@"..\MyProj\Util.bas", "Util.bas", DisplayName = "out of the folder, and back in by its name")]
    [DataRow(@"..\myproj\Sub\Util.bas", "Sub/Util.bas", DisplayName = "back in, in any case, to a sub-folder")]
    [DataRow(@"..\..\src\MyProj\Util.bas", "Util.bas", DisplayName = "two folders out, and back in")]
    [DataRow(@"Sub\..\Util.bas", "Util.bas", DisplayName = "a sub-folder's ..")]
    [DataRow(@".\Util.bas", "Util.bas", DisplayName = "a .")]
    public void APathThatComesBackIntoTheFolder_IsTheProjectsOwnFile(string path, string expected)
    {
        var import = VbpProjectImporter.Import($"Module=Util; {path}", "MyProj.vbp", Libraries, MyProj);

        CollectionAssert.AreEqual(new[] { expected }, import.Project.Modules.Select(module => module.RelativeUri).ToArray());
        Assert.IsEmpty(import.Notes);
    }

    [TestMethod]
    [DataRow(@"..\Shared\Util.bas", DisplayName = "a sibling folder")]
    [DataRow(@"..\MyProjOld\Util.bas", DisplayName = "a sibling whose name starts as the folder's")]
    [DataRow(@"Sub\..\..\Util.bas", DisplayName = "out through a sub-folder")]
    public void APathThatLeavesTheFolder_IsNoted(string path)
    {
        var import = VbpProjectImporter.Import($"Module=Util; {path}", "MyProj.vbp", Libraries, MyProj);

        Assert.IsEmpty(import.Project.Modules);
        Assert.AreEqual((VbpImportNoteKind.OutsideProjectFolder, path), import.Notes.Select(note => (note.Kind, note.Subject)).Single());
    }

    [TestMethod]
    public void APathThatClimbsOutOfAFolderThatIsNotKnown_IsNoted_ASubFoldersIsResolved()
    {
        Assert.IsEmpty(ModulesOf(@"Module=Util; ..\MyProj\Util.bas", folder: null), "whether it comes back in is not known");
        CollectionAssert.AreEqual(new[] { "Util.bas" }, ModulesOf(@"Module=Util; Sub\..\Util.bas", folder: null));
    }

    [TestMethod]
    public void TheModules_ClassesAndForms_AreTheProjectsModules_InTheFoldersTheyAreIn()
    {
        var project = Import().Project;

        CollectionAssert.AreEqual(new[] { "frmMain.frm", "modMain.bas", "Classes/clsThing.cls" }, project.Modules.Select(module => module.RelativeUri).ToArray());
        CollectionAssert.Contains(project.Folders, "Classes");
    }

    [TestMethod]
    public void WhatIsNotAModule_IsAFileOfTheProject()
        => CollectionAssert.AreEqual(new[] { "ctlGauge.ctl", "README.txt", "Battleship.RES" }, Import().Project.OtherFiles.Select(file => file.RelativeUri).ToArray());

    [TestMethod]
    public void ALibrary_IsReferencedByTheNameThePlatformShipsItUnder_ForItsIdentifier()
    {
        var references = Import().Project.References;

        CollectionAssert.AreEqual(new[] { "stdole", "Scripting" }, references.Select(reference => reference.Name).ToArray());
        var scripting = references.Single(reference => reference.Name == "Scripting");
        Assert.AreEqual(new Guid("420B2830-E718-11CF-893D-00A0C9054228"), scripting.Guid);
        Assert.AreEqual((1, 0), (scripting.Major, scripting.Minor));
        Assert.AreEqual(@"C:\Windows\System32\scrrun.dll", scripting.AbsolutePath);
    }

    // the standard library is every project's own: no project file lists it, and a .vbp line that references VBA's type library adds none.
    [TestMethod]
    public void TheStandardLibrary_IsNoReference_EvenOfAVbpThatReferencesIt()
        => Assert.IsEmpty(Import(@"Reference=*\G{000204EF-0000-0000-C000-000000000046}#4.2#0#C:\Program Files\Microsoft Office\VBE7.DLL#Visual Basic For Applications").Project.References);

    // a .vbp writes a type library's version in hexadecimal.
    [TestMethod]
    public void AVersion_IsReadInHexadecimal()
    {
        var stdole = Import(@"Reference=*\G{00020430-0000-0000-C000-000000000046}#2.a#0#stdole2.tlb#OLE Automation").Project.References
            .Single(reference => reference.Name == "stdole");

        Assert.AreEqual((2, 10), (stdole.Major, stdole.Minor));
    }

    [TestMethod]
    public void TheConditionalCompilationArguments_AreTheProjectsConstants()
    {
        var constants = Import().Project.PrecompilerConstants;

        Assert.AreEqual("1", constants["DEBUG"]);
        Assert.AreEqual("-1", constants["TRACE"]);
    }

    // as vb6.exe reads CondComp, measured with /make: name = n, colon-separated, spaces optional, n a whole number -32768..32767.
    [TestMethod]
    [DataRow("A=1:B=-2", "A=1;B=-2")]
    [DataRow("debug = +5 : Trace=0", "debug=5;Trace=0")]
    [DataRow("Low = -32768 : High = 32767", "Low=-32768;High=32767")]
    [DataRow("", "")]
    public void ConditionalCompilationArguments_ThatVB6Reads_AreTheProjectsConstants(string condComp, string expected)
    {
        var import = Import($"CondComp=\"{condComp}\"");

        Assert.AreEqual(expected, string.Join(";", import.Project.PrecompilerConstants.Select(constant => $"{constant.Key}={constant.Value}")));
        Assert.IsEmpty(import.Notes);
    }

    [TestMethod]
    [DataRow("A = \"text\"", DisplayName = "a string")]
    [DataRow("A = &H10", DisplayName = "a hexadecimal number")]
    [DataRow("A = True", DisplayName = "True")]
    [DataRow("A = 1.5", DisplayName = "a fraction")]
    [DataRow("A = 32768", DisplayName = "a number too large")]
    [DataRow("A = --1", DisplayName = "two signs")]
    [DataRow("A", DisplayName = "a name without a value")]
    [DataRow("A =", DisplayName = "an empty value")]
    [DataRow("A = 1 :", DisplayName = "a colon at the end")]
    [DataRow("1A = 1", DisplayName = "a name that is no identifier")]
    public void ConditionalCompilationArguments_ThatVB6Refuses_AreRefusedWhole_AndNoted(string condComp)
    {
        var import = Import($"CondComp=\"B = 2 : {condComp}\"");

        Assert.IsEmpty(import.Project.PrecompilerConstants, "vb6.exe refuses the whole line, B = 2 with it");
        Assert.AreEqual((VbpImportNoteKind.InvalidConditionalCompilation, $"B = 2 : {condComp}"), import.Notes.Select(note => (note.Kind, note.Subject)).Single());
    }

    [TestMethod]
    public void AVbpThatStartsWithAByteOrderMark_IsReadInItsEncoding()
    {
        var ansi = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

        Assert.AreEqual("Name=\"Café\"", VbpProjectImporter.Decode([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("Name=\"Café\"")], ansi));
    }

    // a client that keeps a .vbp and a .rdproj in step imports the .vbp again after each change: the platform's own settings survive it.
    [TestMethod]
    public void AnImportOverAProjectFile_ReplacesWhatTheVbpDescribes_AndKeepsTheRest()
    {
        var existing = new ProjectFile(@"C:\old", new RDCoreProject
        {
            Name = "Old",
            References =
            [
                new RDCoreReference { Name = "VBA", Guid = RDCoreReference.StandardLibraryGuid, Major = 4, Minor = 2 },
                new RDCoreReference { Name = "Scripting", TypeLibInfoPath = "scrrun.json" },
                new RDCoreReference { Name = "ADODB" },
            ],
            Modules = [new RDCoreModule { RelativeUri = "MODMAIN.BAS", Super = DocClassType.ExcelWorkbook }, new RDCoreModule { RelativeUri = "gone.bas" }],
            OtherFiles = [new RDCoreFile { RelativeUri = "old.txt" }],
            PrecompilerConstants = new() { ["OLD"] = "1" },
        }) with
        {
            Version = "0.0.9",
            Configuration = ["rdcore.settings.json"],
        };

        var project = Import().Into(existing, @"C:\ws");

        Assert.AreEqual(@"C:\ws", project.Uri);
        Assert.AreEqual("0.0.9", project.Version, "the version that wrote it, until the platform writes it");
        CollectionAssert.AreEqual(new[] { "rdcore.settings.json" }, project.Configuration, "the platform's own settings");

        var info = project.ProjectInfo;
        Assert.AreEqual("Battleship", info.Name);
        CollectionAssert.AreEqual(new[] { "frmMain.frm", "modMain.bas", "Classes/clsThing.cls" }, info.Modules.Select(module => module.RelativeUri).ToArray());
        Assert.AreEqual(DocClassType.ExcelWorkbook, info.Modules.Single(module => module.RelativeUri == "modMain.bas").Super, "kept by its path, in any case");
        CollectionAssert.AreEqual(new[] { "stdole", "Scripting" }, info.References.Select(reference => reference.Name).ToArray(),
            "ADODB is no longer referenced, and VBA is no reference: every project has it");
        Assert.AreEqual("scrrun.json", info.References.Single(reference => reference.Name == "Scripting").TypeLibInfoPath);
        Assert.AreEqual(new Guid("420B2830-E718-11CF-893D-00A0C9054228"), info.References.Single(reference => reference.Name == "Scripting").Guid, "the .vbp's");
        CollectionAssert.AreEqual(new[] { "ctlGauge.ctl", "README.txt", "Battleship.RES" }, info.OtherFiles.Select(file => file.RelativeUri).ToArray());
        CollectionAssert.AreEquivalent(new[] { "DEBUG", "TRACE" }, info.PrecompilerConstants.Keys.ToArray());
    }

    [TestMethod]
    public void AnImportIntoAFolderWithNoProjectFile_IsANewOne()
    {
        var project = Import().Into(null, @"C:\ws");

        Assert.AreEqual(@"C:\ws", project.Uri);
        Assert.AreEqual("Battleship", project.ProjectInfo.Name);
        Assert.IsEmpty(project.Configuration);
    }

    [TestMethod]
    public void TheImport_RecordsItsVbp_ByARelativePath_AndAnotherWriteOfTheProjectFileKeepsIt()
    {
        var imported = Import().Into(null, @"C:\ws");

        Assert.AreEqual("Battleship.vbp", imported.ProjectInfo.SourceProject, "the .vbp is in the folder of the .rdproj: its path relative to it is its name");
        Assert.IsTrue(imported.ProjectInfo.IsImported);
        Assert.IsFalse(Path.IsPathRooted(imported.ProjectInfo.SourceProject!));

        var edited = imported with { ProjectInfo = imported.ProjectInfo.WithModule(new RDCoreModule { RelativeUri = "Extra.bas" }).WithName("Renamed") };
        Assert.AreEqual("Battleship.vbp", edited.ProjectInfo.SourceProject, "nothing but an import changes it");
    }

    [TestMethod]
    public void AnotherImportIntoTheProjectFile_KeepsTheField_AndAProjectFileWithoutOneGetsIt()
    {
        var imported = Import().Into(null, @"C:\ws");
        var again = Import().Into(imported, @"C:\ws");
        var native = new ProjectFile(@"C:\ws", new RDCoreProject { Name = "Native" });

        Assert.AreEqual("Battleship.vbp", again.ProjectInfo.SourceProject);
        Assert.IsFalse(native.ProjectInfo.IsImported, "a project file that was written by hand, or by the platform, is no import");
        Assert.AreEqual("Battleship.vbp", Import().Into(native, @"C:\ws").ProjectInfo.SourceProject, "until a .vbp is imported into it");
    }

    [TestMethod]
    public void TheFieldIsWrittenToTheProjectFile_OnlyForAnImport()
    {
        var imported = System.Text.Json.JsonSerializer.Serialize(Import().Into(null, @"C:\ws"));
        var native = System.Text.Json.JsonSerializer.Serialize(new ProjectFile(@"C:\ws", new RDCoreProject { Name = "Native" }));

        StringAssert.Contains(imported, "\"SourceProject\":\"Battleship.vbp\"");
        Assert.DoesNotContain("SourceProject", native);
    }

    [TestMethod]
    public void WhatAProjectFileCannotSay_IsNoted_AndNotCarriedOver()
    {
        var import = Import();

        CollectionAssert.AreEqual(new[]
        {
            (VbpImportNoteKind.ProjectType, "Exe"),
            (VbpImportNoteKind.UnresolvedReference, "Custom Library"),
            (VbpImportNoteKind.ProjectReference, @"..\Shared\Shared.vbp"),
            (VbpImportNoteKind.Control, "MSCOMCTL.OCX"),
            (VbpImportNoteKind.OutsideProjectFolder, @"..\Shared\modShared.bas"),
            (VbpImportNoteKind.AbsolutePath, @"C:\Elsewhere\modAbsolute.bas"),
            (VbpImportNoteKind.NotAModule, "ctlGauge.ctl"),
            (VbpImportNoteKind.Startup, "Sub Main"),
        }, import.Notes.Select(note => (note.Kind, note.Subject)).ToArray());
        Assert.IsFalse(import.Project.Modules.Any(module => module.RelativeUri.Contains("modShared") || module.RelativeUri.Contains("modAbsolute")));
    }

    // what follows the first [section] is the settings of add-ins, none of the project's.
    [TestMethod]
    public void WhatFollowsTheFirstSection_IsNotTheProjects()
        => Assert.IsFalse(Import().Project.Modules.Any(module => module.RelativeUri == "modAddIn.bas"));

    [TestMethod]
    public void ANote_IsSaidInTheLanguageOfTheReader()
    {
        var note = new VbpImportNote(VbpImportNoteKind.Control, "MSCOMCTL.OCX");

        Assert.AreEqual("The control 'MSCOMCTL.OCX' is not carried over: the platform does not host ActiveX controls.", note.ToMessage(CultureInfo.InvariantCulture));
        Assert.AreEqual("Le contrôle « MSCOMCTL.OCX » n'est pas repris : la plateforme n'héberge pas de contrôles ActiveX.", note.ToMessage(CultureInfo.GetCultureInfo("fr-CA")));
    }

    // a ..\ file is a module of this project, as in VB6, when a folder of the workspace holds it; one that none holds is said so.
    [TestMethod]
    public void AFileOutsideEveryWorkspaceFolder_IsSaidToBeSo()
    {
        var note = new VbpImportNote(VbpImportNoteKind.OutsideProjectFolder, @"..\Shared\Utils.bas");

        Assert.AreEqual(@"'..\Shared\Utils.bas' is not carried over: it is outside every folder of the workspace. Open the folder it is in as a workspace folder too, and import the project again.",
            note.ToMessage(CultureInfo.InvariantCulture));
        StringAssert.Contains(note.ToMessage(CultureInfo.GetCultureInfo("fr-CA")), "hors de tous les dossiers de l'espace de travail");
    }

    // the folder of the .vbp, and the folders of the workspace: one beside it (Shared) the client opened too, one (Elsewhere) it did not.
    private static readonly string Projects = Path.Combine(Path.GetTempPath(), "rdcore-import-bounds");
    private static readonly string Battleship = Path.Combine(Projects, "Battleship");
    private static readonly string Shared = Path.Combine(Projects, "Shared");

    private static bool InWorkspace(string path)
        => new SDK.Workspace.WorkspaceFolders([Battleship, Shared]).Contains(path);

    private static VbpImport ImportBounded(string vbp, bool withWorkspace = true)
        => VbpProjectImporter.Import(vbp, "Battleship.vbp", Libraries, Battleship, withWorkspace ? InWorkspace : null);

    [TestMethod]
    public void AFileInAnotherWorkspaceFolder_IsAModuleOfTheProject_ByItsRelativePath()
    {
        var import = ImportBounded(@"Module=Util; ..\Shared\Util.bas" + "\r\n" + @"Class=Ship; ..\Shared\Model\Ship.cls");

        CollectionAssert.AreEqual(new[] { "../Shared/Util.bas", "../Shared/Model/Ship.cls" }, import.Project.Modules.Select(module => module.RelativeUri).ToArray());
        Assert.IsEmpty(import.Notes);
        Assert.IsEmpty(import.Project.Folders, "a folder outside the project's is the workspace folder's that holds it");
    }

    [TestMethod]
    public void AnAbsolutePathInAWorkspaceFolder_IsWrittenRelative_AndOneInTheProjectsOwnToo()
    {
        var import = ImportBounded($"Module=Util; {Path.Combine(Shared, "Util.bas")}\r\nModule=Main; {Path.Combine(Battleship, "Main.bas")}");

        CollectionAssert.AreEqual(new[] { "../Shared/Util.bas", "Main.bas" }, import.Project.Modules.Select(module => module.RelativeUri).ToArray(),
            "nothing a project file lists is absolute");
        Assert.IsEmpty(import.Notes);
    }

    [TestMethod]
    public void AFileInNoWorkspaceFolder_IsNoted_WhetherItsPathClimbsOutOrIsAbsolute()
    {
        var absolute = Path.Combine(Projects, "Elsewhere", "Util.bas");
        var import = ImportBounded($"Module=Util; ..\\Elsewhere\\Util.bas\r\nModule=Other; {absolute}");

        Assert.IsEmpty(import.Project.Modules);
        CollectionAssert.AreEqual(new[] { (VbpImportNoteKind.OutsideProjectFolder, @"..\Elsewhere\Util.bas"), (VbpImportNoteKind.AbsolutePath, absolute) },
            import.Notes.Select(note => (note.Kind, note.Subject)).ToArray());
    }

    [TestMethod]
    public void WithoutWorkspaceFolders_AFileOutsideTheProjectsFolder_IsNoted()
    {
        var import = ImportBounded(@"Module=Util; ..\Shared\Util.bas", withWorkspace: false);

        Assert.IsEmpty(import.Project.Modules, "rdc import has no workspace folders");
        Assert.AreEqual(VbpImportNoteKind.OutsideProjectFolder, import.Notes.Single().Kind);
    }

    // VB6 writes its project files in the ANSI code page of the machine.
    [TestMethod]
    public void TheVbp_IsReadInTheAnsiCodePage()
    {
        var ansi = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

        Assert.AreEqual("Name=\"Bataille navale à deux\"", VbpProjectImporter.Decode(ansi.GetBytes("Name=\"Bataille navale à deux\""), ansi));
        Assert.AreEqual("Bataille navale à deux", VbpProjectImporter.Import(VbpProjectImporter.Decode(ansi.GetBytes("Name=\"Bataille navale à deux\""), ansi), "x.vbp", Libraries).Project.Name);
    }
}
