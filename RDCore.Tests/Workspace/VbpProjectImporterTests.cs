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

        CollectionAssert.AreEqual(new[] { "VBA", "stdole", "Scripting" }, references.Select(reference => reference.Name).ToArray());
        var scripting = references.Single(reference => reference.Name == "Scripting");
        Assert.AreEqual(new Guid("420B2830-E718-11CF-893D-00A0C9054228"), scripting.Guid);
        Assert.AreEqual((1, 0), (scripting.Major, scripting.Minor));
        Assert.AreEqual(@"C:\Windows\System32\scrrun.dll", scripting.AbsolutePath);
    }

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

    // as vb6.exe reads CondComp, measured with /make (HexIDE, 2026-10-27): name = n, colon-separated, spaces optional, n a whole number -32768..32767.
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
                RDCoreReference.VBStandardLibrary,
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
        Assert.AreEqual(new ProjectFile(@"C:\ws").Version, project.Version, "the running platform's, as of every project file it writes");
        CollectionAssert.AreEqual(new[] { "rdcore.settings.json" }, project.Configuration, "the platform's own settings");

        var info = project.ProjectInfo;
        Assert.AreEqual("Battleship", info.Name);
        CollectionAssert.AreEqual(new[] { "frmMain.frm", "modMain.bas", "Classes/clsThing.cls" }, info.Modules.Select(module => module.RelativeUri).ToArray());
        Assert.AreEqual(DocClassType.ExcelWorkbook, info.Modules.Single(module => module.RelativeUri == "modMain.bas").Super, "kept by its path, in any case");
        CollectionAssert.AreEqual(new[] { "VBA", "stdole", "Scripting" }, info.References.Select(reference => reference.Name).ToArray(), "ADODB is no longer referenced");
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
    public void WhatAProjectFileCannotSay_IsNoted_AndNotCarriedOver()
    {
        var import = Import();

        CollectionAssert.AreEqual(new[]
        {
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

    // a ..\ file is a module of this project, as in VB6, in a source folder the client adds to the workspace; not carried over until the platform takes such
    // folders in (author, 2026-10-27, reversing the morning's "a project of its own"; HexIDE's desk #30 and #31).
    [TestMethod]
    public void AFileOutsideTheProjectsFolder_IsSaidToBeAModuleOfThisProjectInASourceFolder()
    {
        var note = new VbpImportNote(VbpImportNoteKind.OutsideProjectFolder, @"..\Shared\Utils.bas");

        Assert.AreEqual(@"'..\Shared\Utils.bas' is not carried over yet: it is in a folder outside the project's. It will be a module of this project once its folder is part of the workspace, as a source folder; the platform does not take in such a folder yet.",
            note.ToMessage(CultureInfo.InvariantCulture));
        StringAssert.Contains(note.ToMessage(CultureInfo.GetCultureInfo("fr-CA")), "un module de ce projet");
        StringAssert.DoesNotMatch(note.ToMessage(CultureInfo.GetCultureInfo("fr-CA")), new System.Text.RegularExpressions.Regex("autre projet"));
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
