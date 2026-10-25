using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RDCore.External;
using RDCore.External.Interaction;
using RDCore.External.Settings;
using RDCore.Runtime.Execution;
using RDCore.Runtime.StdLib;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.StdLib;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// The <c>VBA.Interaction</c> module (<strong>MS-VBAL §6.1.2.8</strong>), run from source against an outside world the test composes: fakes for the dialogs and the
/// desktop, and the workspace application settings on a file system in memory. Nothing here needs Windows.
/// </summary>
[TestClass]
public sealed class StdInteractionTests
{
    private static readonly string WorkspaceRoot = Path.Combine(Path.GetTempPath(), "rdcore-interaction-ws");
    private static string AppSettings => Path.Combine(WorkspaceRoot, JsonWorkspaceSettings.DefaultFileName);

    private static string[] Run(params string[] body) => Run(ExternalWorld.None, body);

    private static string[] Run(ExternalWorld outside, params string[] body)
    {
        var output = new RuntimeOutputBuffer();
        var (_, outcome) = RuntimeSourceHarness.Run(null, [], output, standardLibrary: true, arrange: null, ModuleDirectives.None, observer: null, analyze: false, outside, body);
        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        return [.. output.Lines.Select(line => line.Trim())];
    }

    private static (int Number, string Verbose) Fail(params string[] body) => Fail(ExternalWorld.None, body);

    private static (int Number, string Verbose) Fail(ExternalWorld outside, params string[] body)
    {
        var (_, outcome) = RuntimeSourceHarness.Run(null, [], new RuntimeOutputBuffer(), standardLibrary: true, arrange: null, ModuleDirectives.None, observer: null, analyze: false, outside, body);
        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind, "an error that says what it is, and not an internal one");
        return (outcome.ErrorInfo!.ErrorId, outcome.ErrorInfo.Verbose);
    }

    private static ExternalWorld With(IUserInteraction interaction) => ExternalWorld.None with { Interaction = interaction };

    private static ExternalWorld With(IDesktop desktop) => ExternalWorld.None with { Desktop = desktop };

    private static (ExternalWorld World, MockFileSystem Files) WithSettings(string? appsettings = null)
    {
        var files = new MockFileSystem();
        files.Directory.CreateDirectory(WorkspaceRoot);
        if (appsettings is not null)
        {
            files.File.WriteAllText(AppSettings, appsettings);
        }

        var settings = new JsonWorkspaceSettings(files, WorkspaceRoot);
        return (ExternalWorld.None with { WorkspaceSettings = settings, ProgramSettings = new JsonProgramSettings(settings) }, files);
    }

    // ---- DoEvents, Beep ----

    [TestMethod]
    public void DoEvents_IsAFunction_AnswersZero_WhenNothingIsOpen()
        => CollectionAssert.AreEqual(new[] { "0" }, Run("Debug.Print DoEvents()"));

    [TestMethod]
    public void DoEvents_IsAStatementToo_AsAFunctionIsCalledForItsEffect()
        => CollectionAssert.AreEqual(new[] { "done" }, Run("DoEvents", "Debug.Print \"done\""));

    [TestMethod]
    public void DoEvents_IsFoundByModuleAndByLibrary_WhateverTheCase()
        => CollectionAssert.AreEqual(new[] { "0", "0" }, Run("Debug.Print Interaction.DoEvents()", "Debug.Print VBA.doevents()"));

    [TestMethod]
    public void DoEvents_PumpsTheDesktop_ThePlatformHas()
    {
        var desktop = Substitute.For<IDesktop>();
        Run(With(desktop), "DoEvents", "DoEvents");
        desktop.Received(2).DoEvents();
    }

    [TestMethod]
    public void Beep_IsASub_ThatHasNothingToSound_AndIsNoError()
        => CollectionAssert.AreEqual(new[] { "done" }, Run("Beep", "VBA.Beep", "Debug.Print \"done\""));

    [TestMethod]
    public void Beep_SoundsTheDesktop_ThePlatformHas()
    {
        var desktop = Substitute.For<IDesktop>();
        Run(With(desktop), "Beep");
        desktop.Received(1).Beep();
    }

    [TestMethod]
    public async Task DoEventsAndBeep_AreStatementsOfTheShellToo_NotVariablesItDeclares()
    {
        var result = await RDCore.Tests.Cli.ShellHost.Compose().RunAsync([(10, "DOEVENTS"), (20, "BEEP"), (30, "PRINT DOEVENTS")]);

        Assert.AreEqual(RDCore.SDK.Platform.Protocol.ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "0" }, result.Output.Select(line => line.Trim()).ToArray());
    }

    // ---- IIf, Choose, Switch ----

    [TestMethod]
    public void IIf_ReturnsTheTruePart_OrTheFalsePart()
        => CollectionAssert.AreEqual(new[] { "yes", "no", "yes" }, Run(
            "Debug.Print IIf(1 < 2, \"yes\", \"no\")",
            "Debug.Print IIf(False, \"yes\", \"no\")",
            "Debug.Print IIf(-1, \"yes\", \"no\")"));

    [TestMethod]
    public void IIf_ANullCondition_IsNotTrue()
        => CollectionAssert.AreEqual(new[] { "no" }, Run("Debug.Print IIf(Null, \"yes\", \"no\")"));

    [TestMethod]
    public void IIf_ACondition_ThatIsNoBoolean_IsATypeMismatch()
        => Assert.AreEqual(13, Fail("Debug.Print IIf(\"maybe\", 1, 2)").Number);

    [TestMethod]
    public void IIf_ReturnsThePartAsItIs_NotAsAString()
    {
        var module = new StdInteraction(Substitute.For<IRuntimeSession>());
        var (one, half) = (new VBVariantValue(new VBIntegerValue(1)), new VBVariantValue(new VBDoubleValue(2.5)));

        Assert.AreSame(one, module.IIf(new VBVariantValue(new VBBooleanValue(true)), one, half).Result);
        Assert.AreSame(half, module.IIf(new VBVariantValue(new VBBooleanValue(false)), one, half).Result);
    }

    [TestMethod]
    public void Choose_IsOneBased_WhateverOptionBaseSays()
        => CollectionAssert.AreEqual(new[] { "a", "c" }, Run("Debug.Print Choose(1, \"a\", \"b\", \"c\")", "Debug.Print Choose(3, \"a\", \"b\", \"c\")"));

    [TestMethod]
    public void Choose_AnIndexOutOfBounds_IsNull()
        => CollectionAssert.AreEqual(new[] { "Null", "Null", "Null" }, Run(
            "Debug.Print Choose(0, \"a\", \"b\")",
            "Debug.Print Choose(3, \"a\", \"b\")",
            "Debug.Print Choose(-1, \"a\", \"b\")"));

    [TestMethod]
    public void Choose_AnIndexThatIsNoWholeNumber_IsRounded_AsASingleIsToALong()
        => CollectionAssert.AreEqual(new[] { "b", "b", "c" }, Run(
            "Debug.Print Choose(1.6, \"a\", \"b\", \"c\")",
            "Debug.Print Choose(2.5, \"a\", \"b\", \"c\")",
            "Debug.Print Choose(2.51, \"a\", \"b\", \"c\")"));

    [TestMethod]
    public void Switch_ReturnsTheValue_OfTheFirstTrueExpression()
        => CollectionAssert.AreEqual(new[] { "two" }, Run("Debug.Print Switch(1 = 2, \"one\", 2 = 2, \"two\", 3 = 3, \"three\")"));

    [TestMethod]
    public void Switch_WithNoTrueExpression_IsNull_AndANullExpressionIsNotTrue()
        => CollectionAssert.AreEqual(new[] { "Null", "b" }, Run(
            "Debug.Print Switch(False, \"a\")",
            "Debug.Print Switch(Null, \"a\", True, \"b\")"));

    [TestMethod]
    public void Switch_ExpressionsThatAreNotPaired_AreError5()
        => Assert.AreEqual(5, Fail("Debug.Print Switch(True, \"a\", False)").Number);

    // ---- MsgBox ----

    [TestMethod]
    public void MsgBox_AnswersTheButtonThatClosedIt()
    {
        var interaction = Substitute.For<IUserInteraction>();
        interaction.ShowMessage(Arg.Any<MessageBoxRequest>(), Arg.Any<CancellationToken>()).Returns(VBMsgBoxResult.VBNo);

        CollectionAssert.AreEqual(new[] { "7", "True" }, Run(With(interaction),
            "Debug.Print MsgBox(\"Sure?\", vbYesNo + vbQuestion, \"Confirm\")",
            "Dim answeredNo As Boolean",
            "answeredNo = (MsgBox(\"Again?\", vbYesNo) = vbNo)",
            "Debug.Print answeredNo"));

        interaction.Received().ShowMessage(new MessageBoxRequest("Sure?", "Confirm", VBMsgBoxStyle.VBYesNo | VBMsgBoxStyle.VBQuestion), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void MsgBox_WithNoTitle_IsTitledAsTheHostSays()
    {
        var interaction = Substitute.For<IUserInteraction>();
        Run(With(interaction) with { DialogTitle = "Project1" }, "MsgBox \"hello\"");
        interaction.Received().ShowMessage(new MessageBoxRequest("hello", "Project1", VBMsgBoxStyle.VBOkOnly), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void MsgBox_APromptThatIsNotAString_IsLetCoercedToOne()
    {
        var interaction = Substitute.For<IUserInteraction>();
        Run(With(interaction), "MsgBox 42");
        interaction.Received().ShowMessage(Arg.Is<MessageBoxRequest>(request => request.Prompt == "42"), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void MsgBox_WithNobodyToAsk_AnswersItsDefaultButton()
        => CollectionAssert.AreEqual(new[] { "1", "7", "4" }, Run(
            "Debug.Print MsgBox(\"x\")",
            "Debug.Print MsgBox(\"x\", vbYesNo + vbDefaultButton2)",
            "Debug.Print MsgBox(\"x\", vbRetryCancel)"));

    [TestMethod]
    public void MsgBox_ThatCannotBeShown_IsAnErrorOfItsOwn()
    {
        var interaction = Substitute.For<IUserInteraction>();
        interaction.ShowMessage(Arg.Any<MessageBoxRequest>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("no desktop"));
        StringAssert.Contains(Fail(With(interaction), "MsgBox \"x\"").Verbose, "no desktop");
    }

    [TestMethod]
    public void MsgBox_WhoseRunIsStopped_AnswersAsAWindowClosedWithoutAChoice()
    {
        var interaction = Substitute.For<IUserInteraction>();
        interaction.ShowMessage(Arg.Any<MessageBoxRequest>(), Arg.Any<CancellationToken>()).Throws(new OperationCanceledException());
        var session = Substitute.For<IRuntimeSession>();

        var result = new StdInteraction(session, With(interaction)).MsgBox(new VBVariantValue(new VBStringValue("x")), VBMsgBoxStyle.VBOkCancel);

        Assert.AreEqual((int)VBMsgBoxResult.VBCancel, result.Result!.Value);
    }

    // ---- InputBox ----

    [TestMethod]
    public void InputBox_AnswersWhatWasTyped()
    {
        var interaction = Substitute.For<IUserInteraction>();
        interaction.Prompt(Arg.Any<InputBoxRequest>(), Arg.Any<CancellationToken>()).Returns("Mathieu");

        CollectionAssert.AreEqual(new[] { "Hello, Mathieu" }, Run(With(interaction), "Debug.Print \"Hello, \" & InputBox(\"Name?\", \"Who\", \"you\", 100, 200)"));
        interaction.Received().Prompt(new InputBoxRequest("Name?", "Who", "you", 100, 200), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void InputBox_WithNoTitleAndNoDefault_IsTitledAsTheHostSays_AndStartsEmpty()
    {
        var interaction = Substitute.For<IUserInteraction>();
        interaction.Prompt(Arg.Any<InputBoxRequest>(), Arg.Any<CancellationToken>()).Returns(string.Empty);

        Run(With(interaction) with { DialogTitle = "Project1" }, "Debug.Print InputBox(\"Name?\")");
        interaction.Received().Prompt(new InputBoxRequest("Name?", "Project1", string.Empty), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void InputBox_ThatWasCancelled_IsVbNullString_AndOneConfirmedEmpty_IsAZeroLengthString()
    {
        var interaction = Substitute.For<IUserInteraction>();
        var module = new StdInteraction(Substitute.For<IRuntimeSession>(), With(interaction));
        var prompt = new VBVariantValue(new VBStringValue("Name?"));

        interaction.Prompt(Arg.Any<InputBoxRequest>(), Arg.Any<CancellationToken>()).Returns((string?)null);
        var cancelled = module.InputBox(prompt).Result!;

        interaction.Prompt(Arg.Any<InputBoxRequest>(), Arg.Any<CancellationToken>()).Returns(string.Empty);
        var empty = module.InputBox(prompt).Result!;

        Assert.IsTrue(cancelled.IsNullString, "a cancelled dialog answers a null string pointer");
        Assert.IsFalse(empty.IsNullString, "a dialog confirmed with nothing typed answers a zero-length string");
        Assert.AreEqual(empty.Value, cancelled.Value, "the two compare equal");
    }

    [TestMethod]
    public void InputBox_ThatWasCancelled_IsAZeroLengthString_ToTheProgram()
    {
        var interaction = Substitute.For<IUserInteraction>();
        interaction.Prompt(Arg.Any<InputBoxRequest>(), Arg.Any<CancellationToken>()).Returns((string?)null);
        CollectionAssert.AreEqual(new[] { "True", "0" }, Run(With(interaction),
            "Dim isEmptyString As Boolean",
            "isEmptyString = (InputBox(\"x\") = \"\")",
            "Debug.Print isEmptyString",
            "Debug.Print Len(InputBox(\"x\"))"));
    }

    [TestMethod]
    public void InputBox_WhoseRunIsStopped_AnswersVbNullString()
    {
        var interaction = Substitute.For<IUserInteraction>();
        interaction.Prompt(Arg.Any<InputBoxRequest>(), Arg.Any<CancellationToken>()).Throws(new OperationCanceledException());

        var result = new StdInteraction(Substitute.For<IRuntimeSession>(), With(interaction)).InputBox(new VBVariantValue(new VBStringValue("x")));

        Assert.IsTrue(result.Result!.IsNullString);
    }

    [TestMethod]
    public void InputBox_WithNobodyToAsk_AnswersItsDefault()
        => CollectionAssert.AreEqual(new[] { "42" }, Run("Debug.Print InputBox(\"How many?\", , \"42\")"));

    [TestMethod]
    public void TheDialogs_AreGivenTheRunsCancellation()
    {
        var interaction = Substitute.For<IUserInteraction>();
        using var stop = new CancellationTokenSource();
        var module = new StdInteraction(Substitute.For<IRuntimeSession>(), With(interaction), stop.Token);

        module.MsgBox(new VBVariantValue(new VBStringValue("x")));
        module.InputBox(new VBVariantValue(new VBStringValue("x")));

        interaction.Received().ShowMessage(Arg.Any<MessageBoxRequest>(), stop.Token);
        interaction.Received().Prompt(Arg.Any<InputBoxRequest>(), stop.Token);
    }

    // ---- AppActivate, SendKeys, Shell ----

    [TestMethod]
    public void AppActivate_ByTitle_OrByTaskId()
    {
        var desktop = Substitute.For<IDesktop>();
        desktop.AppActivate(Arg.Any<string>(), Arg.Any<bool>()).Returns(true);
        desktop.AppActivate(Arg.Any<int>(), Arg.Any<bool>()).Returns(true);

        Run(With(desktop), "AppActivate \"Untitled - Notepad\"", "AppActivate 1234, True");

        desktop.Received().AppActivate("Untitled - Notepad", false);
        desktop.Received().AppActivate(1234, true);
    }

    [TestMethod]
    public void AppActivate_AWindowThatIsNotThere_IsError5()
    {
        var desktop = Substitute.For<IDesktop>();
        Assert.AreEqual(5, Fail(With(desktop), "AppActivate \"nothing\"").Number);
    }

    [TestMethod]
    public void SendKeys_SendsTheKeys_AndWaitsWhenAskedTo()
    {
        var desktop = Substitute.For<IDesktop>();
        Run(With(desktop), "SendKeys \"%{F4}\"", "SendKeys \"abc~\", True");

        desktop.Received().SendKeys("%{F4}", false);
        desktop.Received().SendKeys("abc~", true);
    }

    [TestMethod]
    public void SendKeys_KeysThatAreNotInItsSyntax_AreError5()
    {
        var desktop = Substitute.For<IDesktop>();
        desktop.When(d => d.SendKeys(Arg.Any<string>(), Arg.Any<bool>())).Do(call => SendKeysParser.Parse(call.Arg<string>()));
        Assert.AreEqual(5, Fail(With(desktop), "SendKeys \"{NOTAKEY}\"").Number);
    }

    [TestMethod]
    public void Shell_AnswersTheTaskId_OfWhatItStarted()
    {
        var desktop = Substitute.For<IDesktop>();
        desktop.Shell(Arg.Any<string>(), Arg.Any<VBAppWinStyle>()).Returns(4321);

        CollectionAssert.AreEqual(new[] { "4321", "4321" }, Run(With(desktop), "Debug.Print Shell(\"notepad.exe\", vbNormalFocus)", "Debug.Print Shell(\"x\")"));
        desktop.Received().Shell("notepad.exe", VBAppWinStyle.VBNormalFocus);
        desktop.Received().Shell("x", VBAppWinStyle.VBMinimizedFocus);
    }

    [TestMethod]
    public void Shell_AProgramThatIsNotThere_IsFileNotFound()
    {
        var desktop = Substitute.For<IDesktop>();
        desktop.Shell(Arg.Any<string>(), Arg.Any<VBAppWinStyle>()).Throws(new FileNotFoundException("nope"));
        Assert.AreEqual(53, Fail(With(desktop), "Debug.Print Shell(\"nope.exe\")").Number);
    }

    [TestMethod]
    public void WithNoDesktop_AppActivateSendKeysAndShell_DoNothing_AndAreNoError()
        => CollectionAssert.AreEqual(new[] { "0", "done" }, Run(
            "Debug.Print Shell(\"notepad.exe\")",
            "AppActivate \"Notepad\"",
            "SendKeys \"{ENTER}\"",
            "SendKeys \"this is {not} valid\"",
            "Debug.Print \"done\""));

    // ---- Environ ----

    [TestMethod]
    public void Environ_ReadsAVariableOfTheProcess_AndEnvironStringToo()
    {
        Environment.SetEnvironmentVariable("RDCORE_INTERACTION_TEST", "duck");
        try
        {
            CollectionAssert.AreEqual(new[] { "duck", "duck", "", "duck" }, Run(
                "Debug.Print Environ(\"RDCORE_INTERACTION_TEST\")",
                "Debug.Print Environ$(\"%RDCORE_INTERACTION_TEST%\")",
                "Debug.Print Environ(\"RDCORE_NO_SUCH_VARIABLE\")",
                "Debug.Print Interaction.Environ(\"rdcore_interaction_test\")"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("RDCORE_INTERACTION_TEST", null);
        }
    }

    [TestMethod]
    public void Environ_ANumber_IsThePositionOfAVariableInTheTable_FromOne()
    {
        var output = Run("Debug.Print Environ(1)", "Debug.Print Environ(100000)");
        StringAssert.Contains(output[0], "=");
        Assert.AreEqual(string.Empty, output[1]);
        Assert.AreEqual(5, Fail("Debug.Print Environ(0)").Number);
    }

    [TestMethod]
    public void Environ_Null_IsInvalidUseOfNull()
        => Assert.AreEqual(94, Fail("Debug.Print Environ(Null)").Number);

    private static StdInteraction ModuleOver(Dictionary<string, string> variables, bool expandsMacros)
        => new(Substitute.For<IRuntimeSession>(), environment: new ProcessEnvironment(() => variables, expandsMacros));

    [TestMethod]
    public void Environ_OnWindows_ExpandsTheMacrosOfAValue()
    {
        var module = ModuleOver(new() { ["SystemRoot"] = @"C:\Windows", ["Tools"] = @"%SYSTEMROOT%\system32;%UNDEFINED%" }, expandsMacros: true);

        Assert.AreEqual(@"C:\Windows\system32;%UNDEFINED%", module.EnvironString(new VBVariantValue(new VBStringValue("tools"))).Result!.Value);
        Assert.AreEqual(@"C:\Windows", module.EnvironString(new VBVariantValue(new VBStringValue("%systemroot%"))).Result!.Value);
    }

    [TestMethod]
    public void Environ_Elsewhere_LeavesAPercentSignAlone()
    {
        var module = ModuleOver(new() { ["HOME"] = "/home/duck", ["Odd"] = "%HOME%" }, expandsMacros: false);
        Assert.AreEqual("%HOME%", module.EnvironString(new VBVariantValue(new VBStringValue("Odd"))).Result!.Value);
    }

    [TestMethod]
    public void Environ_TheTable_IsInTheOrderOfTheNames()
    {
        var module = ModuleOver(new() { ["b"] = "2", ["A"] = "1", ["c"] = "%A%" }, expandsMacros: true);
        string At(int position) => module.EnvironString(new VBVariantValue(new VBLongValue(position))).Result!.Value;

        CollectionAssert.AreEqual(new[] { "A=1", "b=2", "c=1", "" }, new[] { At(1), At(2), At(3), At(4) });
    }

    // ---- the legacy settings, in appsettings.json ----

    [TestMethod]
    public void SaveSetting_IsReadBack_ByGetSetting_AndKeptInTheWorkspacesAppSettings()
    {
        var (world, files) = WithSettings();

        CollectionAssert.AreEqual(new[] { "100", "100" }, Run(world,
            "SaveSetting \"MyApp\", \"Startup\", \"Top\", \"100\"",
            "Debug.Print GetSetting(\"MyApp\", \"Startup\", \"Top\")",
            "Debug.Print GetSetting(\"myapp\", \"STARTUP\", \"top\")"));

        StringAssert.Contains(files.File.ReadAllText(AppSettings), "\"ProgramSettings\"");
    }

    [TestMethod]
    public void GetSetting_ASettingThatIsNotThere_IsTheDefault_OrAZeroLengthString()
        => CollectionAssert.AreEqual(new[] { "fallback", "", "42" }, Run(WithSettings().World,
            "Debug.Print GetSetting(\"MyApp\", \"Startup\", \"Left\", \"fallback\")",
            "Debug.Print GetSetting(\"MyApp\", \"Startup\", \"Left\")",
            "Debug.Print GetSetting(\"MyApp\", \"Startup\", \"Left\", 42)"));

    [TestMethod]
    public void GetAllSettings_IsATwoDimensionalArray_OfKeysAndValues()
        => CollectionAssert.AreEqual(new[] { "1", "1", "Left=10", "Top=20" }, Run(WithSettings().World,
            "SaveSetting \"MyApp\", \"Startup\", \"Top\", \"20\"",
            "SaveSetting \"MyApp\", \"Startup\", \"Left\", \"10\"",
            "Dim settings As Variant",
            "settings = GetAllSettings(\"MyApp\", \"Startup\")",
            "Debug.Print UBound(settings, 1)",
            "Debug.Print UBound(settings, 2)",
            "Debug.Print settings(0, 0) & \"=\" & settings(0, 1)",
            "Debug.Print settings(1, 0) & \"=\" & settings(1, 1)"));

    [TestMethod]
    public void GetAllSettings_ASectionThatIsNotThere_IsEmpty()
    {
        var module = new StdInteraction(Substitute.For<IRuntimeSession>(), WithSettings().World);
        var settings = module.GetAllSettings(new VBStringValue("MyApp"), new VBStringValue("Nothing")).Result!;
        Assert.IsInstanceOfType<VBEmptyValue>(settings.TypedValue);
    }

    [TestMethod]
    public void DeleteSetting_DeletesAKey_OrASection_OrAnApplication()
        => CollectionAssert.AreEqual(new[] { "", "20", "", "" }, Run(WithSettings().World,
            "SaveSetting \"MyApp\", \"Startup\", \"Top\", \"20\"",
            "SaveSetting \"MyApp\", \"Startup\", \"Left\", \"10\"",
            "SaveSetting \"MyApp\", \"Other\", \"Key\", \"x\"",
            "DeleteSetting \"MyApp\", \"Startup\", \"Left\"",
            "Debug.Print GetSetting(\"MyApp\", \"Startup\", \"Left\")",
            "Debug.Print GetSetting(\"MyApp\", \"Startup\", \"Top\")",
            "DeleteSetting \"MyApp\", \"Startup\"",
            "Debug.Print GetSetting(\"MyApp\", \"Startup\", \"Top\")",
            "DeleteSetting \"MyApp\"",
            "Debug.Print GetSetting(\"MyApp\", \"Other\", \"Key\")"));

    [TestMethod]
    public void DeleteSetting_ASettingThatIsNotThere_IsError5()
        => Assert.AreEqual(5, Fail(WithSettings().World, "DeleteSetting \"MyApp\", \"Startup\", \"Top\"").Number);

    [TestMethod]
    public void SaveSetting_ANameWithNothingInIt_IsError5()
        => Assert.AreEqual(5, Fail(WithSettings().World, "SaveSetting \"\", \"Startup\", \"Top\", \"1\"").Number);

    [TestMethod]
    public void TheLegacySettings_AreTheProgramSettingsSection_OfTheJsonSettings()
        => CollectionAssert.AreEqual(new[] { "20" }, Run(WithSettings().World,
            "SaveSetting \"MyApp\", \"Startup\", \"Top\", \"20\"",
            "Debug.Print GetJsonSetting(\"ProgramSettings:MyApp:Startup:Top\")"));

    [TestMethod]
    public void WithNoSettings_ASettingIsSavedAndNotKept()
        => CollectionAssert.AreEqual(new[] { "none" }, Run(
            "SaveSetting \"MyApp\", \"Startup\", \"Top\", \"20\"",
            "Debug.Print GetSetting(\"MyApp\", \"Startup\", \"Top\", \"none\")"));

    // ---- the JSON settings ----

    private const string Configuration = """
        {
          // a comment, as appsettings.json has them
          "Configuration": {
            "ConnectionStrings": { "AppDb": "Server=.;Database=App", "Logs": "Server=.;Database=Logs" },
            "Retries": 3,
            "Verbose": true,
            "Nested": { "Deeper": { "Value": "x" } }
          }
        }
        """;

    [TestMethod]
    public void GetJsonSetting_ReadsAManagedConfigurationPath_WithoutRegardToCase()
        => CollectionAssert.AreEqual(new[] { "Server=.;Database=App", "3", "True", "x" }, Run(WithSettings(Configuration).World,
            "Debug.Print GetJsonSetting(\"Configuration:ConnectionStrings:AppDb\")",
            "Debug.Print GetJsonSetting(\"configuration:retries\")",
            "Debug.Print GetJsonSetting(\"Configuration:Verbose\")",
            "Debug.Print GetJsonSetting(\"Configuration:Nested:Deeper:Value\")"));

    [TestMethod]
    public void GetJsonSetting_ASettingThatIsNotThere_IsTheDefault()
        => CollectionAssert.AreEqual(new[] { "none", "" }, Run(WithSettings(Configuration).World,
            "Debug.Print GetJsonSetting(\"Configuration:Missing\", , \"none\")",
            "Debug.Print GetJsonSetting(\"Configuration:Nested\")"));

    [TestMethod]
    public void GetAllJsonSettings_IsTheSettingsOfASection_AndNotItsSections()
        => CollectionAssert.AreEqual(new[] { "1", "AppDb=Server=.;Database=App", "Logs=Server=.;Database=Logs", "Retries", "True" }, Run(WithSettings(Configuration).World,
            "Dim settings As Variant",
            "settings = GetAllJsonSettings(\"Configuration:ConnectionStrings\")",
            "Debug.Print UBound(settings, 1)",
            "Debug.Print settings(0, 0) & \"=\" & settings(0, 1)",
            "Debug.Print settings(1, 0) & \"=\" & settings(1, 1)",
            "settings = GetAllJsonSettings(\"Configuration\")",
            "Debug.Print settings(0, 0)",
            "Debug.Print settings(1, 1)"));

    [TestMethod]
    public void SaveJsonSetting_CreatesTheSectionsOfItsPath_AndKeepsWhatWasThere()
    {
        var (world, files) = WithSettings(Configuration);

        CollectionAssert.AreEqual(new[] { "on", "3" }, Run(world,
            "SaveJsonSetting \"Configuration:Features:Beta\", \"on\"",
            "Debug.Print GetJsonSetting(\"Configuration:Features:Beta\")",
            "Debug.Print GetJsonSetting(\"Configuration:Retries\")"));

        var text = files.File.ReadAllText(AppSettings);
        StringAssert.Contains(text, "\"Beta\": \"on\"");
        Assert.IsFalse(text.Contains("// a comment"), "JSON has no comments, and a file that is written to is written as JSON");
    }

    [TestMethod]
    public void SaveJsonSetting_ReplacesASetting_FoundWithoutRegardToCase()
    {
        var (world, files) = WithSettings(Configuration);
        Run(world, "SaveJsonSetting \"configuration:RETRIES\", \"5\"");
        var text = files.File.ReadAllText(AppSettings);

        StringAssert.Contains(text, "\"Retries\": \"5\"");
        Assert.IsFalse(text.Contains("RETRIES"));
    }

    [TestMethod]
    public void TheJsonSettings_OfAnotherFileOfTheWorkspace()
    {
        var (world, files) = WithSettings();
        Run(world, "SaveJsonSetting \"Section:Key\", \"value\", \"config/other.json\"");

        Assert.IsTrue(files.File.Exists(Path.Combine(WorkspaceRoot, "config", "other.json")));
        Assert.IsFalse(files.File.Exists(AppSettings));
        CollectionAssert.AreEqual(new[] { "value", "" }, Run(world,
            "Debug.Print GetJsonSetting(\"Section:Key\", \"config/other.json\")",
            "Debug.Print GetJsonSetting(\"Section:Key\")"));
    }

    [TestMethod]
    [DataRow("../outside.json")]
    [DataRow("settings.txt")]
    public void AConfigurationFile_ThatIsNotAJsonFileOfTheWorkspace_IsError5(string file)
        => Assert.AreEqual(5, Fail(WithSettings().World, $"Debug.Print GetJsonSetting(\"A:B\", \"{file}\")").Number);

    [TestMethod]
    public void AConfigurationFile_ThatIsNotJson_IsInvalidFileFormat()
        => Assert.AreEqual(321, Fail(WithSettings("{ this is not json").World, "Debug.Print GetJsonSetting(\"A:B\")").Number);

    [TestMethod]
    public void SaveJsonSetting_UnderAValue_IsError5()
        => Assert.AreEqual(5, Fail(WithSettings(Configuration).World, "SaveJsonSetting \"Configuration:Retries:Sub\", \"x\"").Number);

    // ---- MacScript ----

    [TestMethod]
    public void MacScript_IsNotImplemented_AndSaysSo()
        => StringAssert.Contains(Fail("Debug.Print MacScript(\"tell application \"\"Finder\"\" to activate\")").Verbose, "MacScript");

    // ---- what the declaration says ----

    private static readonly Uri Root = new("file://rdcore-test");

    private static IReadOnlyList<Symbol> Read() => new StdLibSymbolReader(Root).Read(typeof(IStdInteractionModule).Assembly);

    private static Symbol Member(string name)
    {
        var module = Read().OfType<VBStandardModuleSymbol>().Single(candidate => candidate.Name == "Interaction");
        return Read().Where(symbol => symbol.ParentUri.AbsoluteUri == module.Uri.AbsoluteUri).Single(member => member.Name == name);
    }

    private static bool IsHidden(Symbol member) => (member.GetProperty(SymbolProperties.MemberFlags) & SymbolProperties.HiddenMemberFlag) != 0;

    [TestMethod]
    [DataRow("CallByName")]
    [DataRow("Choose")]
    [DataRow("Command")]
    [DataRow("Command$")]
    [DataRow("CreateObject")]
    [DataRow("DoEvents")]
    [DataRow("Environ")]
    [DataRow("Environ$")]
    [DataRow("GetAllSettings")]
    [DataRow("GetAllJsonSettings")]
    [DataRow("GetAttr")]
    [DataRow("GetObject")]
    [DataRow("GetSetting")]
    [DataRow("GetJsonSetting")]
    [DataRow("IIf")]
    [DataRow("InputBox")]
    [DataRow("MsgBox")]
    [DataRow("Partition")]
    [DataRow("Shell")]
    [DataRow("Switch")]
    [DataRow("AppActivate")]
    [DataRow("Beep")]
    [DataRow("DeleteSetting")]
    [DataRow("SaveSetting")]
    [DataRow("SaveJsonSetting")]
    [DataRow("SendKeys")]
    [DataRow("MacScript")]
    public void EveryMemberTheSpecificationNames_IsNamedAsItIsInSource(string name) => Assert.IsNotNull(Member(name));

    [TestMethod]
    public void MacScript_IsHidden_AndTheRestAreNot()
    {
        Assert.IsTrue(IsHidden(Member("MacScript")));
        Assert.IsFalse(IsHidden(Member("MsgBox")));
    }

    [TestMethod]
    public void TheSubroutines_AreSubs_AndTheFunctionsAreNot()
    {
        Assert.AreEqual(VBVoidType.TypeInfo, ((VBProcedureMemberSymbol)Member("Beep")).ResolvedType);
        Assert.AreEqual(VBVoidType.TypeInfo, ((VBProcedureMemberSymbol)Member("SaveJsonSetting")).ResolvedType);
        Assert.AreEqual(VBIntegerType.TypeInfo, ((VBFunctionMemberSymbol)Member("DoEvents")).ResolvedType);
    }

    [TestMethod]
    public void MsgBox_AnswersAnEnum_AndGetAttrToo()
    {
        Assert.AreEqual("VbMsgBoxResult", ((VBFunctionMemberSymbol)Member("MsgBox")).ResolvedType.Name);
        Assert.AreEqual("VbFileAttribute", ((VBFunctionMemberSymbol)Member("GetAttr")).ResolvedType.Name);
    }

    [TestMethod]
    public void VbQuestion_IsAStyleOfMsgBox()
        => CollectionAssert.AreEqual(new[] { "32" }, Run("Debug.Print vbQuestion"));
}
