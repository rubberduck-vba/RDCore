using RDCore.SDK.Runtime.Libraries;
using RDCore.Tests.Cli;
using System.IO.Abstractions;

namespace RDCore.Tests.Runtime.Fonts;

/// <summary>
/// stdole's <c>StdFont</c>, which <c>New StdFont</c> makes, as VB6 and Excel (64-bit) have it: its defaults, the sizes it snaps to, what it refuses and the
/// error it raises for it, its default member, its name for <c>TypeName</c>, and the <c>FontChanged</c> event it raises to a <c>WithEvents</c> variable.
/// </summary>
/// <remarks>
/// The font is the platform's own on every platform: the machine of these tests has no automation server, as one that is not Windows has none, and an
/// environment that allows no automation still has the font.
/// </remarks>
[TestClass]
public sealed class StdFontTests
{
    private static ModuleWorkspace.WorkspaceLibraries Stdole => new(
        ["stdole"], new DirectoryLibrarySource(new FileSystem(), RDCore.Tests.Runtime.Libraries.ShippedLibrariesTests.Symbols),
        RDCore.External.Automation.UnavailableAutomationServer.Instance);

    private static Task<string[]> RunAsync(params string[] lines) => ModuleWorkspace.RunAsync([], Source(lines), Stdole);

    private static string Source(string[] lines) => string.Join("\r\n",
    [
        "Attribute VB_Name = \"Program\"",
        "Option Explicit",
        "Public Sub Main()",
        "    Dim f As StdFont, g As StdFont, o As Object",
        .. lines.Select(line => "    " + line),
        "End Sub",
        "Private Function Shown(ByVal f As StdFont) As String",
        "    Shown = f.Name & \" \" & f.Size & \" \" & f.Weight & \" \" & f.Charset",
        "End Function",
        "",
    ]);

    // what the program printed, all of it in the message when it is not what was expected.
    private static void Printed(string[] expected, string[] printed)
        => Assert.IsTrue(expected.SequenceEqual(printed), $"printed [{string.Join(" | ", printed)}], expected [{string.Join(" | ", expected)}]");

    [TestMethod]
    public async Task ANewStdFont_IsMSSansSerif_At825Points_NormalAndPlain()
        => Printed(["MS Sans Serif 8.25 400 0 False False False False"], await RunAsync(
            "Set f = New StdFont",
            "Debug.Print Shown(f) & \" \" & f.Bold & \" \" & f.Italic & \" \" & f.Underline & \" \" & f.Strikethrough"));

    [TestMethod]
    public async Task AVariableDeclaredAsNewStdFont_IsMadeAFont()
        => Printed(["MS Sans Serif 8.25 400 0"], await RunAsync("Dim h As New StdFont", "Debug.Print Shown(h)"));

    [TestMethod]
    public async Task AFontsTypeName_IsFont_AndItsDefaultMember_ItsName()
        => Printed(["Font 8 MS Sans Serif", "Arial"], await RunAsync(
            "Set f = New StdFont",
            "Debug.Print TypeName(f) & \" \" & VarType(f) & \" \" & f",
            "f = \"Arial\": Debug.Print f.Name"));

    [TestMethod]
    public async Task AFont_IsAStdFontAndAFont_AndNoIFont()
        => Printed(["True True False"], await RunAsync(
            "Set f = New StdFont",
            "Debug.Print (TypeOf f Is StdFont) & \" \" & (TypeOf f Is Font) & \" \" & (TypeOf f Is IFont)"));

    [TestMethod]
    public async Task AFont_IsOneObject_ThroughEveryReference()
        => Printed(["True True", "False"], await RunAsync(
            "Set f = New StdFont: Set g = f",
            "g.Bold = True: Debug.Print (g Is f) & \" \" & f.Bold",
            "Set g = New StdFont: Debug.Print g Is f"));

    [TestMethod]
    public async Task AFontsMembers_AreReachedLateBound()
        => Printed(["Arial 12 True"], await RunAsync(
            "Set o = New StdFont",
            "o.Name = \"Arial\": o.Size = 12: o.Italic = True",
            "Debug.Print o.Name & \" \" & o.Size & \" \" & o.Italic"));

    [TestMethod]
    [DataRow("f.Size = 14", "MS Sans Serif 14.25 400 0", DisplayName = "a font never named, 14: a scalable face's")]
    [DataRow("f.Name = \"MS Sans Serif\": f.Size = 14", "MS Sans Serif 13.5 400 0", DisplayName = "MS Sans Serif named, 14: its own bitmap sizes")]
    [DataRow("f.Name = \"Arial\": f.Size = 13", "Arial 12.75 400 0", DisplayName = "Arial, 13")]
    [DataRow("f.Name = \"Arial\": f.Size = 10.5", "Arial 10.5 400 0", DisplayName = "Arial, 10.5, which it is drawn at")]
    [DataRow("f.Size = 14: f.Name = \"Courier\"", "Courier 12 400 0", DisplayName = "a name after a size not read: the size snaps to the new face")]
    public async Task ASize_ReadsAsTheSizeTheFaceIsDrawnAt(string statements, string expected)
        => Printed([expected], await RunAsync("Set f = New StdFont", statements, "Debug.Print Shown(f)"));

    [TestMethod]
    [DataRow("f.Weight = 551", "700 True", DisplayName = "a weight past 550 is bold")]
    [DataRow("f.Weight = 550", "400 False", DisplayName = "a weight of 550 is normal")]
    [DataRow("f.Weight = 900: f.Bold = False", "400 False", DisplayName = "Bold False is 400, whatever the weight was")]
    [DataRow("f.Bold = True", "700 True", DisplayName = "Bold True is 700")]
    public async Task AWeight_IsNormalOrBold(string statements, string expected)
        => Printed([expected], await RunAsync("Set f = New StdFont", statements, "Debug.Print f.Weight & \" \" & f.Bold"));

    [TestMethod]
    [DataRow("f.Size = 0", "380", DisplayName = "a size of 0")]
    [DataRow("f.Size = -1", "380", DisplayName = "a size below 0")]
    [DataRow("f.Size = 24576", "6", DisplayName = "a size of 24576")]
    [DataRow("f.Weight = 1001", "380", DisplayName = "a weight past 1000")]
    [DataRow("f.Weight = -1", "380", DisplayName = "a weight below 0")]
    [DataRow("f.Charset = 256", "380", DisplayName = "a character set past 255")]
    [DataRow("f.Charset = -1", "380", DisplayName = "a character set below 0")]
    [DataRow("f.Name = String(32, \"a\")", "380", DisplayName = "a name of 32 characters")]
    public async Task WhatAFontRefuses_RaisesWhatMSVBARaises_AndChangesNothing(string statement, string number)
        => Printed([number, "MS Sans Serif 8.25 400 0"], await RunAsync(
            "Set f = New StdFont", "On Error Resume Next", statement, "Debug.Print Err.Number", "Debug.Print Shown(f)"));

    [TestMethod]
    public async Task ANameOf31Characters_AndASizeJustBelowTheOverflow_AreTaken()
        => Printed(["0 31", "0 24575.25"], await RunAsync(
            "Set f = New StdFont", "On Error Resume Next",
            "f.Name = String(31, \"a\"): Debug.Print Err.Number & \" \" & Len(f.Name)",
            "f.Name = \"Arial\": f.Size = 24575.5: Debug.Print Err.Number & \" \" & f.Size"));

    // RD-VBA keeps the name and the character set as they are set: MS-VBA reads them back as Windows' font mapper chose them (Arial for a name it has no
    // face of, and once a never-named font's size is set).
    [TestMethod]
    public async Task ANameAndACharset_ReadBackAsTheyWereSet()
        => Printed(["No Such Face", "MS Sans Serif 12 400 128"], await RunAsync(
            "Set f = New StdFont: f.Name = \"No Such Face\": Debug.Print f.Name",
            "Set f = New StdFont: f.Size = 12: f.Charset = 128: Debug.Print Shown(f)"));

    [TestMethod]
    public async Task ANewStdFont_IsMade_WhereNoAutomationIsAllowed()
        => Printed(["0 Arial 12"], await ModuleWorkspace.RunAsync([], Source([
            "On Error Resume Next",
            "Set f = New StdFont: f.Name = \"Arial\": f.Size = 12",
            "Debug.Print Err.Number & \" \" & f.Name & \" \" & f.Size",
        ]), Stdole with { References = ["stdole", "Scripting"], AllowAutomation = false }));

    // a change raises FontChanged with the member's name, and a value that changes nothing raises nothing: Bold set twice, and a weight of 700 when bold
    // already.
    [TestMethod]
    public async Task AChange_RaisesFontChanged_WithTheMembersName()
        => Printed(["Bold", "Size", "Name", "Italic", "Charset"], await ModuleWorkspace.RunAsync(
            [("Watcher", ModuleWorkspace.ClassModule("Watcher", "Option Explicit",
                "Private WithEvents f As StdFont",
                "Public Sub Watch()",
                "    Set f = New StdFont",
                "    f.Bold = True: f.Bold = True: f.Weight = 700",
                "    f.Size = 12: f.Name = \"Arial\": f.Italic = True: f.Charset = 2",
                "End Sub",
                "Private Sub f_FontChanged(ByVal PropertyName As String)",
                "    Debug.Print PropertyName",
                "End Sub"))],
            string.Join("\r\n",
                "Attribute VB_Name = \"Program\"", "Option Explicit", "Public Sub Main()", "    Dim w As New Watcher", "    w.Watch", "End Sub", ""),
            Stdole));
}
