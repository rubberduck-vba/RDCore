using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace RDCore.CLI.App.Bench;

/// <summary>
/// What a round of MS-VBA returned: the Excel that ran it, and what the harness returned (<see cref="MsVbaHarness.ModuleText"/>).
/// </summary>
/// <param name="Excel">The Excel that ran the round: its version, build and bitness, as the reference stamp says them.</param>
/// <param name="Corpus">What <c>TimeCorpus</c> returned: a line a case.</param>
/// <param name="ExcelCases">What <c>TimeExcel</c> returned, a line a phase; null when the round did not run the Excel automation cases.</param>
internal sealed record class MsVbaRound(string Excel, string Corpus, string? ExcelCases);

/// <summary>
/// Why a round of MS-VBA did not run, or was stopped: the message says it to the user.
/// </summary>
/// <param name="message">What went wrong.</param>
internal sealed class MsVbaHostException(string message) : Exception(message);

/// <summary>
/// Runs MS-VBA's side of a round of <c>rdc bench-msvba</c>.
/// </summary>
internal interface IMsVbaHost
{
    /// <summary>
    /// Starts a new hidden Excel, imports the modules into a new workbook, runs the harness, and ends that Excel.
    /// </summary>
    /// <param name="folder">The folder the modules are in.</param>
    /// <param name="modules">The file names of the modules, the harness among them, in the order they are imported.</param>
    /// <param name="runs">The runs of each case of the corpus.</param>
    /// <param name="excelRuns">The runs of each Excel automation case; none are run when 0.</param>
    /// <param name="token">Stops the round: the Excel it started is ended.</param>
    /// <exception cref="MsVbaHostException">Excel could not run the round, or did not answer.</exception>
    MsVbaRound Run(string folder, IReadOnlyList<string> modules, int runs, int excelRuns, CancellationToken token);

    /// <summary>
    /// The ids of the Excel processes running now.
    /// </summary>
    IReadOnlySet<int> ExcelProcesses();
}

/// <summary>
/// MS-VBA's side of a round, in Excel driven through its object model from a thread of its own.
/// </summary>
/// <remarks>
/// <para>
/// The Excel the round starts (the harness's) and the second instance the harness starts for the Excel automation cases are the only processes it ends, by
/// their ids: an Excel the user has open is never touched.
/// </para>
/// <para>
/// A dialog of either instance (a compile error, a run-time error the harness does not handle) stops the round with what the dialog says, and so does a call
/// that has not returned after <see cref="CallTimeout"/>.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class ExcelMsVbaHost : IMsVbaHost
{
    /// <summary>
    /// How long the round waits for Excel to answer a call: the Excel automation cases take minutes.
    /// </summary>
    public static readonly TimeSpan CallTimeout = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan EndTimeout = TimeSpan.FromSeconds(10);

    public IReadOnlySet<int> ExcelProcesses() => Process.GetProcessesByName("EXCEL").Select(process =>
    {
        using (process)
        {
            return process.Id;
        }
    }).ToHashSet();

    public MsVbaRound Run(string folder, IReadOnlyList<string> modules, int runs, int excelRuns, CancellationToken token)
    {
        var round = new Round(folder, modules, runs, excelRuns);

        // a COM object is called from the single-threaded apartment that created it.
        var thread = new Thread(round.Run) { IsBackground = true, Name = "rdc bench-msvba Excel" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var deadline = DateTime.UtcNow + CallTimeout;
        while (!thread.Join(Poll))
        {
            string? stopped = null;
            if ((Dialog.Find(round.Host) ?? Dialog.Find(round.Target)) is { } dialog)
            {
                stopped = string.Format(CultureInfo.CurrentCulture, Resources.BenchMsVba_Dialog, dialog);
            }
            else if (DateTime.UtcNow > deadline)
            {
                stopped = string.Format(CultureInfo.CurrentCulture, Resources.BenchMsVba_NoAnswer, CallTimeout.TotalMinutes);
            }

            if (stopped is not null || token.IsCancellationRequested)
            {
                round.End(TimeSpan.Zero);
                thread.Join(EndTimeout);
                token.ThrowIfCancellationRequested();
                throw new MsVbaHostException(stopped!);
            }
        }

        return round.Result ?? throw round.Failure switch
        {
            MsVbaHostException failure => failure,
            { } failure => new MsVbaHostException(failure.Message),
            null => new MsVbaHostException(string.Format(CultureInfo.CurrentCulture, Resources.BenchMsVba_NoAnswer, CallTimeout.TotalMinutes)),
        };
    }

    // a round, run on its own thread; Host and Target are the processes it started, which End ends from any thread.
    private sealed class Round(string folder, IReadOnlyList<string> modules, int runs, int excelRuns)
    {
        private volatile int _host;
        private volatile int _target;

        public int Host => _host;
        public int Target => _target;
        public MsVbaRound? Result { get; private set; }
        public Exception? Failure { get; private set; }

        public void Run()
        {
            dynamic? excel = null;
            dynamic? book = null;
            try
            {
                var type = Type.GetTypeFromProgID("Excel.Application", throwOnError: false) ?? throw new MsVbaHostException(Resources.BenchMsVba_NoExcel);
                excel = Activator.CreateInstance(type) ?? throw new MsVbaHostException(Resources.BenchMsVba_NoExcel);
                _host = ProcessOf((int)excel.Hwnd);
                excel.Visible = false;
                excel.DisplayAlerts = false;
                book = excel.Workbooks.Add();

                dynamic components;
                try
                {
                    components = book.VBProject.VBComponents;
                }
                catch (COMException)
                {
                    throw new MsVbaHostException(Resources.BenchMsVba_ProjectNotTrusted);
                }

                foreach (var module in modules)
                {
                    components.Import(Path.Combine(folder, module));
                }

                string version = $"{excel.Version} build {excel.Build} ({excel.Run($"{MsVbaHarness.ModuleName}.Bitness")}-bit)";
                string corpus = excel.Run($"{MsVbaHarness.ModuleName}.TimeCorpus", runs);
                string? cases = null;
                if (excelRuns > 0)
                {
                    int host = excel.Run($"{MsVbaHarness.ModuleName}.HostProcess");
                    _target = excel.Run($"{MsVbaHarness.ModuleName}.StartTarget");
                    try
                    {
                        if (host != _host || _target == 0 || _target == _host)
                        {
                            throw new MsVbaHostException(string.Format(CultureInfo.CurrentCulture, Resources.BenchMsVba_OneProcess, host, _target));
                        }

                        cases = excel.Run($"{MsVbaHarness.ModuleName}.TimeExcel", excelRuns);
                    }
                    finally
                    {
                        excel.Run($"{MsVbaHarness.ModuleName}.StopTarget");
                    }
                }

                Result = new MsVbaRound(version, corpus, cases);
            }
            catch (Exception exception)
            {
                Failure = exception;
            }
            finally
            {
                Quietly(() => book?.Close(false));
                Quietly(() => excel?.Quit());
                if (book is not null)
                {
                    Quietly(() => Marshal.FinalReleaseComObject(book));
                }

                if (excel is not null)
                {
                    Quietly(() => Marshal.FinalReleaseComObject(excel));
                }

                End(EndTimeout);
            }
        }

        // the processes the round started: at once, or after they had a moment to end after Quit.
        public void End(TimeSpan grace)
        {
            Stop(_target, grace);
            Stop(_host, grace);
        }

        private static void Stop(int id, TimeSpan grace)
        {
            if (id == 0)
            {
                return;
            }

            try
            {
                using var process = Process.GetProcessById(id);
                if (!process.WaitForExit(grace))
                {
                    process.Kill();
                }
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // it has ended already.
            }
        }

        private static void Quietly(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception) when (exception is COMException or InvalidComObjectException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                // Excel has ended, or was ended.
            }
        }
    }

    private static int ProcessOf(int window)
    {
        _ = GetWindowThreadProcessId(window, out var id);
        return id;
    }

    // the visible dialogs of a process (the dialog window class), and what they say: their caption and their static text.
    private static class Dialog
    {
        private const string DialogClass = "#32770";

        public static string? Find(int process)
        {
            if (process == 0)
            {
                return null;
            }

            string? found = null;
            _ = EnumWindows((window, parameter) =>
            {
                _ = GetWindowThreadProcessId(window, out var owner);
                if (owner != process || !IsWindowVisible(window) || ClassOf(window) != DialogClass)
                {
                    return true;
                }

                var text = new StringBuilder(TextOf(window));
                _ = EnumChildWindows(window, (child, childParameter) =>
                {
                    if (ClassOf(child) == "Static" && TextOf(child) is { Length: > 0 } line)
                    {
                        text.Append(" | ").Append(line);
                    }

                    return true;
                }, 0);
                found = text.ToString();
                return false;
            }, 0);
            return found;
        }

        private static string ClassOf(nint window)
        {
            var name = new StringBuilder(256);
            _ = GetClassName(window, name, name.Capacity);
            return name.ToString();
        }

        private static string TextOf(nint window)
        {
            var text = new StringBuilder(2048);
            _ = GetWindowText(window, text, text.Capacity);
            return text.ToString();
        }
    }

    private delegate bool EnumWindowsProc(nint window, nint parameter);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetWindowThreadProcessId(nint window, out int processId);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(nint parent, EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetClassName(nint window, StringBuilder name, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetWindowText(nint window, StringBuilder text, int capacity);
}

/// <summary>
/// Runs this <c>rdc</c> again, as a process of its own, and returns what it wrote.
/// </summary>
internal interface IRdcProcess
{
    /// <summary>
    /// Runs <c>rdc</c> with <paramref name="arguments"/>, and waits for it to end.
    /// </summary>
    /// <param name="arguments">Its command line.</param>
    /// <param name="timeout">How long it may run; it is stopped after that, with its children.</param>
    /// <param name="token">Stops it.</param>
    /// <returns>Its exit code (null when it was stopped for running too long), and what it wrote to its standard output and error.</returns>
    Task<(int? ExitCode, string Output)> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken token);
}

/// <summary>
/// Runs this <c>rdc</c>: its own executable, or <c>dotnet</c> with its assembly when that is how it runs.
/// </summary>
internal sealed class RdcProcess : IRdcProcess
{
    public async Task<(int? ExitCode, string Output)> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken token)
    {
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("The path of this process is unknown.");
        var start = new ProcessStartInfo(path)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (string.Equals(Path.GetFileNameWithoutExtension(path), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            start.ArgumentList.Add(typeof(RdcProcess).Assembly.Location);
        }

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"'{path}' did not start.");
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            token.ThrowIfCancellationRequested();
            return (null, await output + await error);
        }

        return (process.ExitCode, await output + await error);
    }
}
