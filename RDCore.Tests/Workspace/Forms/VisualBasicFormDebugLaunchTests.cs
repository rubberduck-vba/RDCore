using OmniSharp.Extensions.DebugAdapter.Protocol.Events;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;
using RDCore.CLI.Host.Handlers;
using RDCore.Runtime.Support.Host;
using RDCore.LanguageServer.Debugging;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;
using RDCore.Tests.LanguageServer;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// A Visual Basic 6.0 program launched by a debugger client, whose <c>Sub Main</c> returns with a form shown: the run lasts until the last form is unloaded,
/// as a run in VB6 does, and the debug session with it. The form is hosted by RDForms, headless.
/// </summary>
// the user's close is posted to the UI thread, which every RDForms window shares and every running program holds: a row that shows a form runs alone.
[TestClass]
[DoNotParallelize]
public sealed class VisualBasicFormDebugLaunchTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static readonly string[] Form1Lines =
    [
        "VERSION 5.00",
        "Begin VB.Form Form1 ",
        "   Caption         =   \"Form1\"",
        "   ClientHeight    =   3000",
        "   ClientWidth     =   5000",
        "End",
        "Attribute VB_Name = \"Form1\"",
        "Attribute VB_GlobalNameSpace = False",
        "Attribute VB_Creatable = False",
        "Attribute VB_PredeclaredId = True",
        "Attribute VB_Exposed = False",
        "Option Explicit",
        "Public Ends As Boolean",
        "Public Fails As Boolean",
        "Private Zero As Long",
        "Private Sub Form_QueryUnload(Cancel As Integer, UnloadMode As Integer)",
        "    Debug.Print \"QueryUnload \" & UnloadMode",
        "    If Ends Then End",
        "    If Fails Then Debug.Print 1 \\ Zero",
        "End Sub",
        "Private Sub Form_Unload(Cancel As Integer)",
        "    Debug.Print \"Unload\"",
        "End Sub",
    ];

    private static readonly string Form1 = string.Join("\r\n", Form1Lines) + "\r\n";

    // the line of Form1's QueryUnload handler a breakpoint is set on, counted from 1.
    private static readonly int InQueryUnload = Array.IndexOf(Form1Lines, "    Debug.Print \"QueryUnload \" & UnloadMode") + 1;

    // Main waits at its Stop, then shows Form1 and returns.
    private static string Program(params string[] before) => string.Join("\r\n", [
        "Attribute VB_Name = \"Program\"",
        "Public Sub Main()",
        "    Stop",
        .. before.Select(line => "    " + line),
        "    Form1.Show",
        "    Debug.Print \"main end\"",
        "End Sub",
        string.Empty]);

    // the program, started under a debugger and waiting at Main's Stop, debugged through the adapter: launched, its breakpoints set, continued; then what
    // the test does with the session.
    private static async Task DebugAsync(string program, bool closedByItsUser, Func<CompiledEditAndContinueTests.Session, EnvironmentSessionProvider, Task> debug)
    {
        CompiledExecutionTests.RequirePayloads();
        var libraries = new ModuleWorkspace.WorkspaceLibraries([], VisualBasicFormLifecycleTests.Shipped)
        {
            Outside = closedByItsUser
                ? VisualBasicFormLifecycleTests.RDFormsWorld(forms => new VisualBasicFormLifecycleTests.ClosedByItsUser(forms))
                : VisualBasicFormLifecycleTests.RDFormsWorld(),
        };

        await ModuleWorkspace.InspectAsync([], program, async (provider, first, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, first.ErrorMessage);
            await using var session = new CompiledEditAndContinueTests.Session(provider, first, new() { ["Program"] = program, ["Form1"] = Form1 });
            try
            {
                await session.StartAsync();
                _ = await session.SendAsync(new ProgramLaunchRequestArguments { Module = "Program", EntryPoint = "Main" });
                var set = await session.SetBreakpointsAsync("Form1", InQueryUnload);
                Assert.IsTrue(set.Breakpoints.Single().Verified, set.Breakpoints.Single().Message);
                _ = await session.SendAsync(new ConfigurationDoneArguments());
                Assert.AreEqual(StoppedEventReason.Breakpoint, (await session.NextAsync<StoppedEvent>()).Reason, "Main's Stop");
                await debug(session, provider);
            }
            finally
            {
                _ = await new HostDebugTerminateHandler(provider).Handle(new HostDebugTerminateParams(), CancellationToken.None).WaitAsync(Patience);
            }
        }, debug: true, libraries: libraries, language: SupportedLanguages.VB6, forms: [("Form1", Form1, FormDesigner.Read(Form1)!)]);
    }

    private static string[] Printed(CompiledEditAndContinueTests.Session session)
        => [.. session.Pending<OutputEvent>().Where(output => output.Category == OutputEventCategory.StandardOutput).Select(output => output.Output.Trim())];

    // the user closes the form once Main has returned: its QueryUnload stops at its breakpoint, and the session ends when the form is unloaded.
    [TestMethod]
    public async Task AHandlerOfAFormLeftShown_StopsAtItsBreakpoint_AndTheSessionEndsWhenTheFormIsUnloaded()
        => await DebugAsync(Program(), closedByItsUser: true, async (session, provider) =>
        {
            _ = await session.SendAsync(new ContinueArguments { ThreadId = ProgramDebugAdapter.ThreadId });
            var hit = await session.NextAsync<StoppedEvent>();
            Assert.AreEqual(StoppedEventReason.Breakpoint, hit.Reason, hit.Description);
            var top = (await session.SendAsync(new StackTraceArguments { ThreadId = ProgramDebugAdapter.ThreadId })).StackFrames!.First();
            Assert.AreEqual(("Form_QueryUnload", InQueryUnload), (top.Name, (int)top.Line));
            Assert.IsEmpty(session.Pending<TerminatedEvent>(), "the session is up while the form is loaded");

            _ = await session.SendAsync(new ContinueArguments { ThreadId = ProgramDebugAdapter.ThreadId });
            _ = await session.NextAsync<TerminatedEvent>();
            Assert.AreEqual(0, session.Pending<ExitedEvent>().Single().ExitCode);
            var printed = Printed(session);
            CollectionAssert.AreEqual(new[] { "main end", "QueryUnload 0", "Unload" }, printed, string.Join(" | ", printed));
        });

    // an End in the handler ends the run, as it ends a program: the form is not unloaded by its handlers, and the program's exit code is 0.
    [TestMethod]
    public async Task AnEnd_InAHandlerOfAFormLeftShown_EndsTheSession()
        => await DebugAsync(Program("Form1.Ends = True"), closedByItsUser: true, async (session, provider) =>
        {
            _ = await session.SetBreakpointsAsync("Form1");
            _ = await session.SendAsync(new ContinueArguments { ThreadId = ProgramDebugAdapter.ThreadId });
            _ = await session.NextAsync<TerminatedEvent>();
            Assert.AreEqual(0, session.Pending<ExitedEvent>().Single().ExitCode);
            var printed = Printed(session);
            CollectionAssert.AreEqual(new[] { "main end", "QueryUnload 0" }, printed, string.Join(" | ", printed));
        });

    // an error no handler catches, in a handler of the form: the run fails, and its exit code is 1.
    [TestMethod]
    public async Task AnErrorNothingCatches_InAHandlerOfAFormLeftShown_EndsTheSessionWithExitCode1()
        => await DebugAsync(Program("Form1.Fails = True"), closedByItsUser: true, async (session, provider) =>
        {
            _ = await session.SetBreakpointsAsync("Form1");
            _ = await session.SendAsync(new SetExceptionBreakpointsArguments { Filters = new Container<string>() });
            _ = await session.SendAsync(new ContinueArguments { ThreadId = ProgramDebugAdapter.ThreadId });
            _ = await session.NextAsync<TerminatedEvent>();
            Assert.AreEqual(1, session.Pending<ExitedEvent>().Single().ExitCode);
            CollectionAssert.Contains(Printed(session), "main end");
        });

    // nobody closes the form: the session is up once Main has returned, and the client's terminate ends the run, as the VBIDE's End does.
    [TestMethod]
    public async Task ARunThatWaitsForItsForm_IsEndedByTheClientsTerminate()
        => await DebugAsync(Program(), closedByItsUser: false, async (session, provider) =>
        {
            _ = await session.SendAsync(new ContinueArguments { ThreadId = ProgramDebugAdapter.ThreadId });

            // Main has returned once its frame is gone: the run goes on, waiting for the form.
            using (var patience = new CancellationTokenSource(Patience))
            {
                while (provider.Session.CallStack.Frames.Any())
                {
                    await Task.Delay(20, patience.Token);
                }
            }

            Assert.AreEqual(ProgramState.Running, provider.Execution.State, "the run waits for its form");
            Assert.IsEmpty(session.Pending<TerminatedEvent>(), "the session is up while the form is loaded");
            _ = await session.SendAsync(new TerminateArguments());
            _ = await session.NextAsync<TerminatedEvent>();
            Assert.AreEqual(0, session.Pending<ExitedEvent>().Single().ExitCode);
            CollectionAssert.AreEqual(new[] { "main end" }, Printed(session), string.Join(" | ", Printed(session)));
        });
}
