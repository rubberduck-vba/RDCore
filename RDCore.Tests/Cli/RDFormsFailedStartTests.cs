using RDCore.SDK.Platform;
using RDCore.Tests.Runtime.Libraries;
using System.Diagnostics;

namespace RDCore.Tests.Cli;

/// <summary>
/// The RDForms windows of a process that cannot be started, on a Linux desktop with no display: every form the program loads after the first fails as the
/// first did, for the reason the first did. The windows are the process's once, so the program runs in an <c>rdc</c> of its own.
/// </summary>
[TestClass]
[TestCategory("External host")]
public sealed class RDFormsFailedStartTests
{
    // a cap on a hang, not a budget: each wait is on the process itself, which a loaded machine starts and stops as late as it does.
    private static readonly TimeSpan Bound = TimeSpan.FromMinutes(5);

    private static readonly string VisualBasicForm = string.Join("\r\n",
        "VERSION 5.00",
        "Begin VB.Form Form1 ",
        "   Caption         =   \"Form1\"",
        "   ClientHeight    =   3195",
        "End",
        "Attribute VB_Name = \"Form1\"",
        "Attribute VB_GlobalNameSpace = False",
        "Attribute VB_Creatable = False",
        "Attribute VB_PredeclaredId = True",
        "Attribute VB_Exposed = False",
        "Option Explicit",
        string.Empty);

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public async Task AFormLoadedAfterTheWindowsFailedToStart_FailsForTheReasonTheFirstDid()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "RDCore", "rdforms-failed-start", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "Form1.frm"), VisualBasicForm);
        File.WriteAllText(Path.Combine(workspace, "Program.bas"), string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            "Option Explicit",
            "Public Sub Main()",
            "    Dim f As Form1",
            "    On Error Resume Next",
            "    Set f = New Form1",
            "    Load f",
            "    Debug.Print \"first: \" & Err.Number & \" \" & Err.Description",
            "    Err.Clear",
            "    Set f = New Form1",
            "    Load f",
            "    Debug.Print \"again: \" & Err.Number & \" \" & Err.Description",
            "End Sub",
            string.Empty));
        File.WriteAllText(Path.Combine(workspace, ".rdproj"), """
            { "Version": "0.1.0", "ProjectInfo": { "Name": "Probe", "Modules": [ { "RelativeUri": "Program.bas" }, { "RelativeUri": "Form1.frm" } ],
              "References": [ { "Name": "VB" }, { "Name": "VBRUN" } ] } }
            """);

        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "rdc"))
        {
            ArgumentList = { "run", workspace },
            WorkingDirectory = workspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // the libraries the project references (VB, VBRUN, and DAO, which VB needs) are in the Symbols folder of the platform's root, the repository's here.
        start.Environment[PlatformEnvironment.RootEnvironmentVariable] = Path.GetDirectoryName(ShippedLibrariesTests.Symbols);

        // a desktop with no display: the windows are the desktop's, which X11 cannot open.
        start.Environment.Remove("DISPLAY");
        start.Environment.Remove("WAYLAND_DISPLAY");

        using var rdc = Process.Start(start)!;
        try
        {
            var output = rdc.StandardOutput.ReadToEndAsync();
            var errors = rdc.StandardError.ReadToEndAsync();
            await rdc.WaitForExitAsync().WaitAsync(Bound);
            var printed = (await output.WaitAsync(Bound)).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var context = $"{string.Join("\n", printed)}\n{await errors.WaitAsync(Bound)}";

            var first = printed.Single(line => line.StartsWith("first: ", StringComparison.Ordinal))["first: ".Length..];
            var again = printed.Single(line => line.StartsWith("again: ", StringComparison.Ordinal))["again: ".Length..];
            StringAssert.Contains(first, "The RDForms windows cannot be started: XOpenDisplay failed", context);
            Assert.AreEqual(first, again, context);
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
}
