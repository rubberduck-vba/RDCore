using RDCore.External;
using RDCore.External.Interaction;
using RDCore.External.Settings;
using RDCore.SDK.Runtime.Abstract.StdLib;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.External;

/// <summary>
/// The platform-agnostic half of what a program reaches outside the platform without a server or a library: the dialogs a <c>MsgBox</c> is made of, what answers
/// them when nobody can, the syntax of <c>SendKeys</c>, and the workspace application settings.
/// </summary>
[TestClass]
public sealed class InteractionTests
{
    // ---- MessageBoxRequest ----

    [TestMethod]
    [DataRow(VBMsgBoxStyle.VBOKOnly, new[] { VBMsgBoxResult.VBOK })]
    [DataRow(VBMsgBoxStyle.VBOKCancel, new[] { VBMsgBoxResult.VBOK, VBMsgBoxResult.VBCancel })]
    [DataRow(VBMsgBoxStyle.VBAbortRetryIgnore, new[] { VBMsgBoxResult.VBAbort, VBMsgBoxResult.VBRetry, VBMsgBoxResult.VBIgnore })]
    [DataRow(VBMsgBoxStyle.VBYesNoCancel, new[] { VBMsgBoxResult.VBYes, VBMsgBoxResult.VBNo, VBMsgBoxResult.VBCancel })]
    [DataRow(VBMsgBoxStyle.VBYesNo, new[] { VBMsgBoxResult.VBYes, VBMsgBoxResult.VBNo })]
    [DataRow(VBMsgBoxStyle.VBRetryCancel, new[] { VBMsgBoxResult.VBRetry, VBMsgBoxResult.VBCancel })]
    public void TheButtonsOfAMessage_AreThoseItsStyleNames_InOrder(VBMsgBoxStyle style, VBMsgBoxResult[] choices)
        => CollectionAssert.AreEqual(choices, new MessageBoxRequest("x", "t", style | VBMsgBoxStyle.VBExclamation).Choices.ToArray());

    [TestMethod]
    public void TheIcon_AndTheDefaultButton_AreReadOffTheStyle()
    {
        var request = new MessageBoxRequest("x", "t", VBMsgBoxStyle.VBYesNoCancel | VBMsgBoxStyle.VBQuestion | VBMsgBoxStyle.VBDefaultButton3);
        Assert.AreEqual(VBMsgBoxStyle.VBQuestion, request.Icon);
        Assert.AreEqual(2, request.DefaultChoice);
    }

    [TestMethod]
    public void ADefaultButtonTheMessageDoesNotHave_IsTheFirst()
        => Assert.AreEqual(0, new MessageBoxRequest("x", "t", VBMsgBoxStyle.VBOKOnly | VBMsgBoxStyle.VBDefaultButton2).DefaultChoice);

    [TestMethod]
    public void AStyleThatNamesNoButtons_IsOKOnly()
        => CollectionAssert.AreEqual(new[] { VBMsgBoxResult.VBOK }, new MessageBoxRequest("x", "t", (VBMsgBoxStyle)7).Choices.ToArray());

    [TestMethod]
    [DataRow(VBMsgBoxStyle.VBOKOnly, VBMsgBoxResult.VBOK)]
    [DataRow(VBMsgBoxStyle.VBOKCancel, VBMsgBoxResult.VBCancel)]
    [DataRow(VBMsgBoxStyle.VBYesNoCancel, VBMsgBoxResult.VBCancel)]
    [DataRow(VBMsgBoxStyle.VBRetryCancel, VBMsgBoxResult.VBCancel)]
    public void EscapeClosesAMessage_AsItsCloseButtonWould(VBMsgBoxStyle style, VBMsgBoxResult expected)
        => Assert.AreEqual(expected, new MessageBoxRequest("x", "t", style).EscapeResult);

    [TestMethod]
    [DataRow(VBMsgBoxStyle.VBYesNo)]
    [DataRow(VBMsgBoxStyle.VBAbortRetryIgnore)]
    public void AMessageThatHasNoWayToBeClosedButToChoose_IsNotClosedByEscape(VBMsgBoxStyle style)
        => Assert.IsNull(new MessageBoxRequest("x", "t", style).EscapeResult);

    // ---- nobody to ask, no desktop, no settings ----

    [TestMethod]
    public void WithNobodyToAsk_ADialogIsConfirmedAtOnce()
    {
        Assert.AreEqual(VBMsgBoxResult.VBNo, NoUserInteraction.Instance.ShowMessage(new MessageBoxRequest("x", "t", VBMsgBoxStyle.VBYesNo | VBMsgBoxStyle.VBDefaultButton2)));
        Assert.AreEqual("42", NoUserInteraction.Instance.Prompt(new InputBoxRequest("x", "t", "42")));
    }

    [TestMethod]
    public void WithNoDesktop_NothingHappens()
    {
        NoDesktop.Instance.DoEvents();
        NoDesktop.Instance.Beep();
        NoDesktop.Instance.SendKeys("{NOT A KEY", wait: true);
        Assert.AreEqual(0, NoDesktop.Instance.Shell("notepad.exe", VBAppWinStyle.VBNormalFocus));
        Assert.IsTrue(NoDesktop.Instance.AppActivate("anything", wait: false));
    }

    [TestMethod]
    public void TheWorldOfNone_ReachesNothing()
    {
        Assert.AreSame(NoUserInteraction.Instance, ExternalWorld.None.Interaction);
        Assert.AreSame(NoDesktop.Instance, ExternalWorld.None.Desktop);
        Assert.AreSame(NoSettings.Instance, ExternalWorld.None.ProgramSettings);
        Assert.AreSame(NoSettings.Instance, ExternalWorld.None.WorkspaceSettings);
        Assert.AreEqual(ExternalWorld.DefaultDialogTitle, ExternalWorld.None.DialogTitle);
    }

    [TestMethod]
    public void WithNoSettings_WhatIsSavedIsNotKept()
    {
        IProgramSettings settings = NoSettings.Instance;
        settings.Save("App", "Section", "Key", "value");
        Assert.IsNull(settings.Get("App", "Section", "Key"));
        Assert.IsFalse(settings.Delete("App"));
    }

    // ---- SendKeys ----

    private static string Strokes(string keys) => string.Join(" ", SendKeysParser.Parse(keys));

    [TestMethod]
    [DataRow("abc", "a b c")]
    [DataRow("~", "{ENTER}")]
    [DataRow("{ENTER}{tab}{BS}{Del}", "{ENTER} {TAB} {BACKSPACE} {DELETE}")]
    [DataRow("+a^b%c", "+a ^b %c")]
    [DataRow("+^%{F4}", "+^%{F4}")]
    [DataRow("+(ec)d", "+e +c d")]
    [DataRow("^(+(ab))", "+^a +^b")]
    [DataRow("{LEFT 3}", "{LEFT} {LEFT} {LEFT}")]
    [DataRow("{h 2}", "h h")]
    [DataRow("{+}{^}{%}{~}{(}{)}{{}{}}{[}{]}", "+ ^ % ~ ( ) { } [ ]")]
    [DataRow("{} 2}", "} }")]
    [DataRow("{~}~", "~ {ENTER}")]
    public void SendKeys_IsReadAsMsVbaDocumentsIt(string keys, string strokes)
        => Assert.AreEqual(strokes, Strokes(keys));

    [TestMethod]
    [DataRow("{NOTAKEY}")]
    [DataRow("{ENTER")]
    [DataRow("}")]
    [DataRow("(ab")]
    [DataRow("ab)")]
    [DataRow("+")]
    [DataRow("{}")]
    [DataRow("{LEFT x}")]
    public void SendKeys_ThatIsNotInItsSyntax_IsRefused(string keys)
        => Assert.ThrowsExactly<FormatException>(() => SendKeysParser.Parse(keys));

    // ---- the workspace application settings ----

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-json-settings");

    private static (JsonWorkspaceSettings Settings, MockFileSystem Files) Settings(string? appsettings = null)
    {
        var files = new MockFileSystem();
        files.Directory.CreateDirectory(Root);
        if (appsettings is not null)
        {
            files.File.WriteAllText(Path.Combine(Root, "appsettings.json"), appsettings);
        }

        return (new JsonWorkspaceSettings(files, Root), files);
    }

    [TestMethod]
    public void AFileThatIsNotThere_HasNoSettings_AndIsNotCreatedByReadingIt()
    {
        var (settings, files) = Settings();
        Assert.IsNull(settings.Get("A:B"));
        Assert.IsNull(settings.GetAll("A"));
        Assert.IsFalse(settings.Delete("A"));
        Assert.IsFalse(files.File.Exists(Path.Combine(Root, "appsettings.json")));
    }

    [TestMethod]
    public void AnEmptyFile_HasNoSettings_AndTakesTheFirstOne()
    {
        var (settings, _) = Settings("   ");
        Assert.IsNull(settings.Get("A:B"));
        settings.Save("A:B", "c");
        Assert.AreEqual("c", settings.Get("a:b"));
    }

    [TestMethod]
    public void AnArray_IsASectionOfIndexedSettings()
    {
        var (settings, _) = Settings("""{ "Hosts": [ "alpha", "beta" ] }""");
        Assert.AreEqual("beta", settings.Get("Hosts:1"));
        CollectionAssert.AreEqual(new[] { "0=alpha", "1=beta" }, settings.GetAll("Hosts")!.Select(pair => $"{pair.Key}={pair.Value}").ToArray());

        settings.Save("Hosts:0", "gamma");
        Assert.AreEqual("gamma", settings.Get("Hosts:0"));
        Assert.IsTrue(settings.Delete("Hosts:1"));
        Assert.IsNull(settings.Get("Hosts:1"));
    }

    [TestMethod]
    public void ASectionIsDeleted_WithEverythingUnderIt()
    {
        var (settings, _) = Settings("""{ "A": { "B": { "C": "1", "D": "2" }, "E": "3" } }""");
        Assert.IsTrue(settings.Delete("a:b"));
        Assert.IsNull(settings.Get("A:B:C"));
        Assert.AreEqual("3", settings.Get("A:E"));
    }

    [TestMethod]
    public void APathWithNothingInIt_IsNotASettingThatCanBeSaved()
    {
        var (settings, _) = Settings();
        Assert.ThrowsExactly<ArgumentException>(() => settings.Save(string.Empty, "x"));
    }

    [TestMethod]
    public void AFileThatIsNotAnObject_IsNotAConfigurationFile()
    {
        var (settings, _) = Settings("[ 1, 2 ]");
        Assert.ThrowsExactly<FormatException>(() => settings.Save("A", "x"));
    }

    [TestMethod]
    [DataRow("../elsewhere.json")]
    [DataRow("sub/../../elsewhere.json")]
    [DataRow("notes.txt")]
    public void AFileThatIsNotAJsonFileOfTheWorkspace_IsRefused(string file)
    {
        var (settings, _) = Settings();
        Assert.ThrowsExactly<ArgumentException>(() => settings.Get("A", file));
    }

    [TestMethod]
    public void AFileOfASubfolder_IsAFileOfTheWorkspace()
    {
        var (settings, _) = Settings();
        Assert.AreEqual(Path.Combine(Root, "config", "app.json"), settings.PathOf("config/app.json"));
        Assert.AreEqual(Path.Combine(Root, "appsettings.json"), settings.PathOf(null));
    }

    [TestMethod]
    public void TheLegacySettings_AreUnderProgramSettings()
    {
        var (workspace, _) = Settings();
        var settings = new JsonProgramSettings(workspace);

        settings.Save("App", "Section", "Key", "value");

        Assert.AreEqual("value", workspace.Get("ProgramSettings:App:Section:Key"));
        CollectionAssert.AreEqual(new[] { "Key" }, settings.GetAll("app", "section")!.Select(pair => pair.Key).ToArray());
    }

    [TestMethod]
    [DataRow("", "Section", "Key")]
    [DataRow("App", "Sec:tion", "Key")]
    [DataRow("App", "Section", " ")]
    public void ALegacyName_ThatIsEmptyOrHasAColon_IsRefused(string app, string section, string key)
    {
        var settings = new JsonProgramSettings(Settings().Settings);
        Assert.ThrowsExactly<ArgumentException>(() => settings.Save(app, section, key, "x"));
    }
}
