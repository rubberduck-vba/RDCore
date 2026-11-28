using RDCore.Runtime.Hosting;
using RDCore.Tests.Runtime.Libraries;
using System.Diagnostics;
using static RDCore.Tests.Composer.ComposedProject;

namespace RDCore.Tests.Hosting;

/// <summary>
/// A host that is the external host of its own machines (<see cref="ProgramExternalHost"/>), as a host that deploys no <c>rdc</c> is: RDCore.Tests.Host,
/// started from the tests' folder, loads a composition and makes a form in a machine, in a process of its own executable. An RDForms form is made with its
/// window, which a desktop with no display cannot open: the rows run on Windows and macOS.
/// </summary>
[TestClass]
[TestCategory("External host")]
[OSCondition(OperatingSystems.Windows | OperatingSystems.OSX)]
public sealed class OwnExternalHostTests
{
    // a cap on a hang, not a budget: each wait is on the process itself.
    private static readonly TimeSpan Bound = TimeSpan.FromMinutes(5);

    private static readonly string Host = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "RDCore.Tests.Host.exe" : "RDCore.Tests.Host");

    private string _root = string.Empty;
    private string _host = string.Empty;

    [TestInitialize]
    public void CreateFolders() => (_root, _host) = (NewFolder(), NewFolder());

    [TestCleanup]
    public void DeleteFolders()
    {
        Directory.Delete(_root, recursive: true);
        Directory.Delete(_host, recursive: true);
    }

    // the host names its own executable as its machines' external host, and the form its machine makes is made there, in a process the host started of
    // itself.
    [TestMethod]
    public async Task AHostThatIsItsOwnExternalHost_MakesItsMachinesForm()
    {
        Assert.IsTrue(File.Exists(Host), $"{Host} is beside the tests");
        var composition = await ExternalHostOptionTests.WriteFormCompositionAsync(_root, _host);

        var start = new ProcessStartInfo(Host)
        {
            ArgumentList = { composition, ShippedLibrariesTests.Symbols, "Program", "Made" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var host = Process.Start(start)!;
        try
        {
            var output = host.StandardOutput.ReadToEndAsync();
            var errors = host.StandardError.ReadToEndAsync();
            await host.WaitForExitAsync().WaitAsync(Bound);
            var said = (await output.WaitAsync(Bound)).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var context = $"{string.Join("\n", said)}\n{await errors.WaitAsync(Bound)}";

            Assert.AreEqual(0, host.ExitCode, context);
            Assert.HasCount(2, said, context);
            Assert.AreEqual(Path.GetFullPath(Host), Path.GetFullPath(said[0]), context);
            Assert.AreEqual("\"Form1\"", said[1], context);
        }
        finally
        {
            if (!host.HasExited)
            {
                host.Kill(entireProcessTree: true);
            }
        }
    }
}
