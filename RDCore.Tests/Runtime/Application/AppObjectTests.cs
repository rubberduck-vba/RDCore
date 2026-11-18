using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.External.Forms;
using RDCore.Runtime.Execution.External.Application;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Platform;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Workspace;
using RDCore.Tests.Cli;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;

namespace RDCore.Tests.Runtime.Application;

/// <summary>
/// Visual Basic 6.0's <c>App</c> under <c>rdc run</c>: the workspace project's folder and name, the values a new Visual Basic 6.0 project has, the
/// properties a program assigns, which keep their value for the session, and the errors of a member that cannot run where the program does.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 6.3 App Object")]
public sealed class AppObjectTests
{
    // the program's forms are a host of the test's own: what App tells the form host is never the machine's, which the tests that run alongside share.
    private static ModuleWorkspace.WorkspaceLibraries Shipped(IFormHost forms)
        => new([], RDCore.Tests.Runtime.Libraries.ShippedLibrariesTests.Platform, Outside: MachineExternal.World with { Forms = forms });

    // a Visual Basic 6.0 project, which references the Visual Basic objects and procedures (VB.Global's App) without listing them.
    private static async Task<string[]> RunAsync(bool unattended, IFormHost forms, params string[] lines)
    {
        ExecuteResult? ran = null;
        await ModuleWorkspace.InspectAsync([], Source(lines), (_, result, _) =>
        {
            ran = result;
            return Task.CompletedTask;
        }, libraries: Shipped(forms), language: SupportedLanguages.VB6, unattended: unattended);

        Assert.AreEqual(ExecutionOutcome.Completed, ran!.Outcome, $"{ran.ErrorMessage} {string.Join("; ", ran.Diagnostics)}");
        return [.. ran.Output.Select(line => line.Trim())];
    }

    private static Task<string[]> RunAsync(bool unattended, params string[] lines) => RunAsync(unattended, Substitute.For<IFormHost>(), lines);

    private static Task<string[]> RunAsync(params string[] lines) => RunAsync(unattended: false, lines);

    // each value the form host was told for App.KeepSystemTheme, in order.
    private static bool[] ToldTheme(IFormHost forms)
        => [.. forms.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == "set_" + nameof(IFormHost.KeepSystemTheme))
            .Select(call => (bool)call.GetArguments()[0]!)];

    private static string Source(string[] lines) => string.Join("\r\n",
    [
        "Attribute VB_Name = \"Program\"",
        "Option Explicit",
        "Public Sub Main()",
        "    On Error Resume Next",
        .. lines.Select(line => "    " + line),
        "End Sub",
        "",
    ]);

    // what the program printed, all of it in the message when it is not what was expected.
    private static void Printed(string[] expected, string[] printed)
        => Assert.IsTrue(expected.SequenceEqual(printed), $"printed [{string.Join(" | ", printed)}], expected [{string.Join(" | ", expected)}]");

    [TestMethod]
    public async Task App_UnderRun_IsTheWorkspaceProject()
        => Printed(
            [Path.TrimEndingDirectorySeparator(ModuleWorkspace.Root), "Project1", "Project1", "False"],
            await RunAsync("Debug.Print App.Path", "Debug.Print App.EXEName", "Debug.Print App.Title", "Debug.Print App.PrevInstance"));

    [TestMethod]
    public async Task App_HasTheVersionOfANewProject_AndNoVersionStrings()
        => Printed(
            ["1 0 0", "0"],
            await RunAsync(
                "Debug.Print App.Major & \" \" & App.Minor & \" \" & App.Revision",
                "Debug.Print Len(App.Comments & App.CompanyName & App.FileDescription & App.LegalCopyright & App.LegalTrademarks & App.ProductName)"));

    [TestMethod]
    public async Task App_IsAStandaloneProgram()
        => Printed(
            ["0 True False False"],
            await RunAsync("Debug.Print App.StartMode & \" \" & App.NonModalAllowed & \" \" & App.RetainedProject & \" \" & App.UnattendedApp"));

    [TestMethod]
    public async Task App_PlatformVersion_IsRDCoresVersion()
    {
        var version = PlatformComponents.VersionOf(PlatformComponents.Platform);
        Assert.IsFalse(string.IsNullOrEmpty(version), "the test's folder holds RDCore.Runtime.dll");
        Printed([version!, "0"], await RunAsync("Debug.Print App.PlatformVersion", "Debug.Print Err.Number"));
    }

    [TestMethod]
    [DataRow("RDCore", DisplayName = "RDCore")]
    [DataRow("RDCore.SDK", DisplayName = "RDCore.SDK")]
    [DataRow("RDCore.Forms", DisplayName = "RDCore.Forms")]
    [DataRow("rdcore.forms", DisplayName = "a name in another case")]
    public async Task App_ComponentVersion_IsTheVersionOfTheComponentItNames(string component)
    {
        var version = PlatformComponents.VersionOf(component);
        Assert.IsFalse(string.IsNullOrEmpty(version), $"the test's folder holds {component}");
        Printed([version!, "0"], await RunAsync($"Debug.Print App.ComponentVersion(\"{component}\")", "Debug.Print Err.Number"));
    }

    [TestMethod]
    [DataRow("Excel", DisplayName = "the name of another component")]
    [DataRow("", DisplayName = "an empty name")]
    public async Task App_ComponentVersion_OfAnyOtherName_IsEmpty_WithNoError(string component)
        => Printed(["[]", "0"], await RunAsync($"Debug.Print \"[\" & App.ComponentVersion(\"{component}\") & \"]\"", "Debug.Print Err.Number"));

    [TestMethod]
    public void App_ComponentVersion_OfAComponentThatIsNotInstalled_IsEmpty()
    {
        // the platform's reader, in an installation whose folder holds no RDCore.Forms.dll.
        var app = new AppCallProvider(Substitute.For<IRuntimeSession>())
        {
            VersionOf = name => string.Equals(name, PlatformComponents.Forms, StringComparison.OrdinalIgnoreCase) ? null : PlatformComponents.VersionOf(name),
        };
        Assert.IsTrue(RDCore.Tests.Runtime.Libraries.ShippedLibrariesTests.Platform.TryGet("VB", out var vb));
        var member = new LibrarySymbolReader(new Uri(ModuleWorkspace.Root)).Read([vb])
            .OfType<VBClassModuleSymbol>().First(declared => declared.Name == "App")
            .Members.OfType<VBPropertyGetMemberSymbol>().Single(declared => declared.Name == "ComponentVersion");

        var result = app.Dispatch(
            new ExternalCallRequest(member, [new VBRuntimeValue<string>("App"), new VBRuntimeValue<string>(PlatformComponents.Forms)]),
            Substitute.For<IRuntimeSymbolResolver>());

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(string.Empty, ((VBStringValue)result.Result!).Value);
    }

    [TestMethod]
    public async Task App_OfAProgramRunUnattended_IsUnattended()
        => Printed(["True"], await RunAsync(unattended: true, "Debug.Print App.UnattendedApp"));

    [TestMethod]
    public async Task App_Title_KeepsWhatTheProgramAssigns()
        => Printed(["Renamed", "Renamed"], await RunAsync("App.Title = \"Renamed\"", "Debug.Print App.Title", "Debug.Print App.OleServerBusyMsgTitle"));

    [TestMethod]
    [DataRow("TaskVisible", "True", "False", DisplayName = "TaskVisible")]
    [DataRow("KeepSystemTheme", "False", "True", DisplayName = "KeepSystemTheme, with no form")]
    [DataRow("OleServerBusyTimeout", "10000", "1", DisplayName = "OleServerBusyTimeout")]
    [DataRow("OleRequestPendingTimeout", "5000", "2", DisplayName = "OleRequestPendingTimeout")]
    [DataRow("OleServerBusyRaiseError", "False", "True", DisplayName = "OleServerBusyRaiseError")]
    [DataRow("OleServerBusyMsgTitle", "Project1", "\"Busy\"", DisplayName = "OleServerBusyMsgTitle, the title until one is assigned")]
    [DataRow("OleServerBusyMsgText", "", "\"Wait\"", DisplayName = "OleServerBusyMsgText")]
    [DataRow("OleRequestPendingMsgTitle", "Project1", "\"Pending\"", DisplayName = "OleRequestPendingMsgTitle, the title until one is assigned")]
    [DataRow("OleRequestPendingMsgText", "", "\"Later\"", DisplayName = "OleRequestPendingMsgText")]
    public async Task App_StoredProperty_KeepsWhatTheProgramAssigns(string property, string initially, string assigned)
        => Printed(
            [initially, assigned.Trim('"'), "0"],
            await RunAsync($"Debug.Print App.{property}", $"App.{property} = {assigned}", $"Debug.Print App.{property}", "Debug.Print Err.Number"));

    // the session's value is told as it is composed, and each the program assigns as it assigns it.
    [TestMethod]
    public async Task App_KeepSystemTheme_TellsTheFormHostEachValueTheProgramAssigns()
    {
        var forms = Substitute.For<IFormHost>();

        Printed(["True", "False"], await RunAsync(unattended: false, forms,
            "App.KeepSystemTheme = True", "Debug.Print App.KeepSystemTheme", "App.KeepSystemTheme = False", "Debug.Print App.KeepSystemTheme"));

        CollectionAssert.AreEqual(new[] { false, true, false }, ToldTheme(forms));
    }

    // the project's value is App's until the program assigns another, and the form host is told it as the session is composed: before any form is made.
    [TestMethod]
    [DataRow(true, DisplayName = "the project keeps the system's theme")]
    [DataRow(false, DisplayName = "the project stays light")]
    public void App_KeepSystemTheme_IsTheProjects_AndTheFormHostIsToldIt_AsTheSessionIsComposed(bool keep)
    {
        var forms = Substitute.For<IFormHost>();
        var project = new RDCoreProject { Name = "Project1", KeepSystemTheme = keep };
        var files = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(ModuleWorkspace.Root, ProjectFile.FileName)] = new(JsonSerializer.Serialize(new ProjectFile(ModuleWorkspace.Root, project))),
        });
        using var provider = new EnvironmentSessionProvider(new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), files,
            NullLogger<EnvironmentSessionProvider>.Instance, outside: MachineExternal.World with { Forms = forms });

        var session = provider.Compose(project, new Uri(ModuleWorkspace.Root));

        Assert.AreEqual(keep, SessionApplication.Of(session).KeepSystemTheme);
        CollectionAssert.AreEqual(new[] { keep }, ToldTheme(forms));
        Assert.IsFalse(forms.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IFormHost.Create)), "no form is made as a session is composed");
    }

    [TestMethod]
    public async Task App_IsOneObject()
        => Printed(["True"], await RunAsync("Dim a As Object", "Set a = App", "Debug.Print a Is App"));

    [TestMethod]
    public async Task App_ThreadID_IsTheThreadOfTheOperatingSystem()
        => Printed(["True 0"], await RunAsync("Debug.Print (App.ThreadID <> 0) & \" \" & Err.Number"));

    [TestMethod]
    public async Task App_hInstance_RaisesAnErrorWhereTheHandleIsNoLong()
    {
        // a module handle is Windows' (5 elsewhere), and the handle of a 64-bit process does not fit a Long (6).
        var expected = !OperatingSystem.IsWindows() ? "5" : Environment.Is64BitProcess ? "6" : "0";
        Printed([expected], await RunAsync("Dim h As Long", "h = App.hInstance", "Debug.Print Err.Number"));
    }

    [TestMethod]
    [DataRow("Debug.Print App.HelpFile", DisplayName = "HelpFile")]
    [DataRow("App.HelpFile = \"x.hlp\"", DisplayName = "HelpFile =")]
    [DataRow("Debug.Print App.LogMode", DisplayName = "LogMode")]
    [DataRow("Debug.Print App.LogPath", DisplayName = "LogPath")]
    [DataRow("App.StartLogging \"log.txt\", 2", DisplayName = "StartLogging")]
    [DataRow("App.LogEvent \"x\"", DisplayName = "LogEvent")]
    public async Task App_MemberNotImplemented_RaisesObjectDoesntSupportThisPropertyOrMethod(string statement)
        => Printed(["438"], await RunAsync(statement, "Debug.Print Err.Number"));

    [TestMethod]
    [Ignore("open question: whether assigning App.HelpFile changes Err.HelpFile, before and after an error (a Visual Basic 6.0 row)")]
    public async Task App_HelpFile_KeepsWhatTheProgramAssigns()
        => Printed(["", "x.hlp", "0"], await RunAsync("Debug.Print App.HelpFile", "App.HelpFile = \"x.hlp\"", "Debug.Print App.HelpFile", "Debug.Print Err.Number"));

    [TestMethod]
    [Ignore("open question: what App.LogMode and App.LogPath are before StartLogging, and what a logged event's line holds (Visual Basic 6.0 rows)")]
    public async Task App_StartLogging_WritesTheFileItNames()
        => Printed(["0"], await RunAsync("App.StartLogging \"app.log\", 2", "App.LogEvent \"x\"", "Debug.Print Err.Number"));
}
