using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RDCore.Tests.Cli;

/// <summary>
/// A command that runs a program - <c>rdc run</c> - starts an external host of <c>rdc</c> when the program first reaches the outside world. The command stops it
/// as it ends: the host does not outlive the <c>rdc</c> that started it until it next looks for it, holding the console that <c>rdc</c> gave it all the while.
/// </summary>
[TestClass]
[TestCategory("External host")]
public sealed class CommandModeExternalHostTests
{
    // a cap on a hang, not a budget: each wait is on the process itself, which a loaded machine starts and stops as late as it does.
    private static readonly TimeSpan Bound = TimeSpan.FromMinutes(5);

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    [SupportedOSPlatform("windows")]
    public async Task RdcRun_StopsTheExternalHostItStarted_BeforeItExits()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "RDCore", "run-external-host", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "Program.bas"), string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            "Option Explicit",
            "Public Sub Main()",
            "    Dim items As Object",
            "    Set items = CreateObject(\"Scripting.Dictionary\")",
            "    items.Add \"key\", 1",
            "    Debug.Print \"items: \" & items.Count",
            "End Sub",
            ""));

        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "rdc.exe"))
        {
            ArgumentList = { "run", workspace },
            WorkingDirectory = workspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var rdc = Process.Start(start)!;
        try
        {
            var output = rdc.StandardOutput.ReadToEndAsync();
            var errors = rdc.StandardError.ReadToEndAsync();
            await rdc.WaitForExitAsync().WaitAsync(Bound);

            // looked for as soon as rdc has exited: a child that is still running is one rdc left behind.
            var left = Children.RunningOf(rdc.Id, rdc.StartTime);

            var printed = await output.WaitAsync(Bound);
            Assert.AreEqual(0, rdc.ExitCode, $"{printed}\n{await errors.WaitAsync(Bound)}");
            StringAssert.Contains(printed, "items: 1", "the program reached its automation server, through the external host");
            Assert.IsEmpty(left, $"still running once rdc exited: {string.Join(", ", left)}");
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

    // the processes of this machine that a process started, as the system's snapshot of them says: each still running is named, by id and name.
    [SupportedOSPlatform("windows")]
    private static class Children
    {
        private const uint SnapProcess = 0x2;

        public static List<string> RunningOf(int parent, DateTime parentStarted)
        {
            var running = new List<string>();
            using var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            for (var more = Process32FirstW(snapshot, ref entry); more; more = Process32NextW(snapshot, ref entry))
            {
                if (entry.ParentProcessId != parent)
                {
                    continue;
                }

                try
                {
                    using var child = Process.GetProcessById((int)entry.ProcessId);
                    // an id the system gave again, to a process older than rdc, is not one of rdc's.
                    if (!child.HasExited && child.StartTime >= parentStarted)
                    {
                        running.Add($"{entry.ProcessId} {entry.ExeFile}");
                    }
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // a process that ended since the snapshot is not running.
                }
            }

            return running;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProcessEntry
        {
            public uint Size;
            public uint Usage;
            public uint ProcessId;
            public IntPtr DefaultHeapId;
            public uint ModuleId;
            public uint Threads;
            public uint ParentProcessId;
            public int PriorityClassBase;
            public uint Flags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string ExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool Process32FirstW(Microsoft.Win32.SafeHandles.SafeFileHandle snapshot, ref ProcessEntry entry);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool Process32NextW(Microsoft.Win32.SafeHandles.SafeFileHandle snapshot, ref ProcessEntry entry);
    }
}
