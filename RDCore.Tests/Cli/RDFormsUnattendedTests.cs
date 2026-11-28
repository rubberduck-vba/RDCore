using RDCore.SDK.Platform;
using RDCore.Tests.Runtime.Libraries;
using System.Diagnostics;

namespace RDCore.Tests.Cli;

/// <summary>
/// The RDForms forms of a program run unattended (<c>rdc run --unattended</c>): nobody sees them, so they are loaded where no screen is needed, and a
/// program's tests that load and unload forms run on a machine with no display. Showing one is a dialog nobody answers. The windows are the process's
/// once, so each program runs in an <c>rdc</c> of its own, with no display to open.
/// </summary>
[TestClass]
[TestCategory("External host")]
public sealed class RDFormsUnattendedTests
{
    // a cap on a hang, not a budget: each wait is on the process itself, which a loaded machine starts and stops as late as it does.
    private static readonly TimeSpan Bound = TimeSpan.FromMinutes(5);

    private static readonly string VisualBasicForm = string.Join("\r\n",
        "VERSION 5.00",
        "Begin VB.Form Form1 ",
        "   Caption         =   \"Form1\"",
        "   ClientHeight    =   3195",
        "   Begin VB.TextBox Text1 ",
        "      TabIndex        =   0",
        "      Text            =   \"Text1\"",
        "   End",
        "End",
        "Attribute VB_Name = \"Form1\"",
        "Attribute VB_GlobalNameSpace = False",
        "Attribute VB_Creatable = False",
        "Attribute VB_PredeclaredId = True",
        "Attribute VB_Exposed = False",
        "Option Explicit",
        "Public Answer As String",
        "Private Sub Form_Initialize()",
        "    Debug.Print \"Initialize\"",
        "End Sub",
        "Private Sub Form_Load()",
        "    Debug.Print \"Load\"",
        "End Sub",
        "Private Sub Form_Unload(Cancel As Integer)",
        "    Debug.Print \"Unload\"",
        "End Sub",
        "Private Sub Form_Terminate()",
        "    Debug.Print \"Terminate\"",
        "End Sub",
        string.Empty);

    // runs Main, of the given lines, unattended, with a form, and with no display: what rdc exits with, and what it printed, a line each.
    private static async Task<(int Exit, string[] Printed, string Errors)> RunUnattendedAsync(params string[] main)
    {
        var workspace = Path.Combine(Path.GetTempPath(), "RDCore", "rdforms-unattended", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "Form1.frm"), VisualBasicForm);
        File.WriteAllText(Path.Combine(workspace, "Program.bas"), string.Join("\r\n", [
            "Attribute VB_Name = \"Program\"",
            "Option Explicit",
            "Public Sub Main()",
            .. main.Select(line => $"    {line}"),
            "End Sub",
            string.Empty]));
        File.WriteAllText(Path.Combine(workspace, ".rdproj"), """
            { "Version": "0.1.0", "ProjectInfo": { "Name": "Probe", "Modules": [ { "RelativeUri": "Program.bas" }, { "RelativeUri": "Form1.frm" } ],
              "References": [ { "Name": "VB" }, { "Name": "VBRUN" } ] } }
            """);

        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "rdc.exe" : "rdc"))
        {
            ArgumentList = { "run", "--unattended", workspace },
            WorkingDirectory = workspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // the libraries the project references (VB, VBRUN, and DAO, which VB needs) are in the Symbols folder of the platform's root, the repository's here.
        start.Environment[PlatformEnvironment.RootEnvironmentVariable] = Path.GetDirectoryName(ShippedLibrariesTests.Symbols);

        // no display to open: a desktop's windows could not be started.
        start.Environment.Remove("DISPLAY");
        start.Environment.Remove("WAYLAND_DISPLAY");

        using var rdc = Process.Start(start)!;
        try
        {
            var output = rdc.StandardOutput.ReadToEndAsync();
            var errors = rdc.StandardError.ReadToEndAsync();
            await rdc.WaitForExitAsync().WaitAsync(Bound);
            var printed = (await output.WaitAsync(Bound)).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return (rdc.ExitCode, printed, await errors.WaitAsync(Bound));
        }
        finally
        {
            if (!rdc.HasExited)
            {
                rdc.Kill(entireProcessTree: true);
            }

            try
            {
                Directory.Delete(workspace, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // the folder is a temporary one.
            }
        }
    }

    // a form loaded and unloaded, and a control of it read and written between, as a program's tests do with the form they test.
    [TestMethod]
    public async Task AFormLoadedAndUnloaded_Unattended_WithNoDisplay_RunsToItsEnd()
    {
        var (exit, printed, errors) = await RunUnattendedAsync(
            "Dim f As Form1",
            "Set f = New Form1",
            "Load f",
            "f.Text1.Text = \"probe\"",
            "Debug.Print f.Text1.Text",
            "Unload f",
            "Debug.Print \"end\"");

        var context = $"{string.Join("\n", printed)}\n{errors}";
        Assert.AreEqual(0, exit, context);
        CollectionAssert.IsSubsetOf(new[] { "Load", "probe", "Unload", "end" }, printed, context);
        Assert.IsLessThan(Array.IndexOf(printed, "end"), Array.IndexOf(printed, "Unload"), context);
    }

    // a program that shows a form, lets its state change, closes it, then asks its default instance for that state, as a smart-UI program does (VB6): Hide
    // keeps the form loaded, with its controls' state and its variables; Unload drops its controls, which the next member of its form loads again as designed,
    // and keeps the instance and its variables, with no second Initialize; only Set Form1 = Nothing lets the instance go, and its name then makes another.
    // What the run prints after "end" is the run's end, which is not this test's.
    [TestMethod]
    [DataRow(new[] { "Load Form1", "Form1.Text1.Text = \"typed\"", "Form1.Answer = \"ok\"", "Form1.Hide",
            "Debug.Print Form1.Text1.Text & \"/\" & Form1.Answer", "Dim f As Form1", "Set f = Form1", "Debug.Print f Is Form1", "Unload Form1" },
        new[] { "Initialize", "Load", "typed/ok", "True", "Unload" },
        DisplayName = "Hide keeps its controls' state and its variables")]
    [DataRow(new[] { "Dim f As Form1", "Set f = Form1", "Load Form1", "Form1.Text1.Text = \"typed\"", "Form1.Answer = \"ok\"", "Unload Form1",
            "Debug.Print Form1.Answer", "Debug.Print f Is Form1", "Debug.Print Form1.Text1.Text", "Unload Form1" },
        new[] { "Initialize", "Load", "Unload", "ok", "True", "Load", "Text1", "Unload" },
        DisplayName = "Unload keeps the instance and its variables, and its next control is loaded again as designed")]
    [DataRow(new[] { "Load Form1", "Form1.Answer = \"ok\"", "Unload Form1", "Set Form1 = Nothing", "Debug.Print \"[\" & Form1.Answer & \"]\"" },
        new[] { "Initialize", "Load", "Unload", "Terminate", "Initialize", "[]" },
        DisplayName = "Set Form1 = Nothing lets the instance go, and its name makes another")]
    public async Task AFormsDefaultInstance_KeepsTheStateTheProgramAsksForOnceTheFormIsClosed(string[] main, string[] printed)
    {
        var (exit, run, errors) = await RunUnattendedAsync([.. main, "Debug.Print \"end\""]);

        var context = $"{string.Join("\n", run)}\n{errors}";
        Assert.AreEqual(0, exit, context);
        CollectionAssert.Contains(run, "end", context);
        CollectionAssert.AreEqual(printed, run.TakeWhile(line => line != "end").ToArray(), context);
    }

    // a form shown, after it is loaded, and the dialogs of the standard library, are what nobody answers: the run fails with VBR50001, whatever handler the
    // program has, and not for want of a display.
    [TestMethod]
    [DataRow("f.Show", DisplayName = "Show, of a form loaded")]
    [DataRow("MsgBox \"Done\"", DisplayName = "MsgBox")]
    [DataRow("Dim name As String: name = InputBox(\"Name?\")", DisplayName = "InputBox")]
    public async Task ADialog_Unattended_WithNoDisplay_FailsTheRunWithNobodyToAnswer(string dialog)
    {
        var (exit, printed, errors) = await RunUnattendedAsync(
            "Dim f As Form1",
            "On Error Resume Next",
            "Set f = New Form1",
            "Load f",
            dialog,
            "Debug.Print \"after\"");

        var context = $"{string.Join("\n", printed)}\n{errors}";
        Assert.AreNotEqual(0, exit, context);
        StringAssert.Contains(context, "VBR50001", context);
        Assert.IsFalse(context.Contains("XOpenDisplay", StringComparison.Ordinal), context);
        CollectionAssert.Contains(printed, "Load", context);
        CollectionAssert.DoesNotContain(printed, "after", context);
    }
}
