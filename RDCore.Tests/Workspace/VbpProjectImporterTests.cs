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

    // VB6 writes its project files in the ANSI code page of the machine.
    [TestMethod]
    public void TheVbp_IsReadInTheAnsiCodePage()
    {
        var ansi = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

        Assert.AreEqual("Name=\"Bataille navale à deux\"", VbpProjectImporter.Decode(ansi.GetBytes("Name=\"Bataille navale à deux\""), ansi));
        Assert.AreEqual("Bataille navale à deux", VbpProjectImporter.Import(VbpProjectImporter.Decode(ansi.GetBytes("Name=\"Bataille navale à deux\""), ansi), "x.vbp", Libraries).Project.Name);
    }
}
