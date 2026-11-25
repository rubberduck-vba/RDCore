using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RDCore.CLI;
using RDCore.CLI.App.Bench;
using RDCore.CLI.App.Commands;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Support.Build;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Workspace;
using RDCore.Tests.Cli.Repl;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>rdc run script.vbs</c> runs a script as <c>cscript.exe</c> runs it: the script sees Windows Script Host's <c>WScript</c> object, whose <c>Echo</c>
/// writes to the program's output, whose <c>Arguments</c> are what follows the script on the command line, and whose <c>Quit</c> ends the process with the
/// code it is given.
/// </summary>
[TestClass]
public sealed class ScriptRunTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-script-ws");
    private static readonly string Script = Path.Combine(Root, "Backup.vbs");

    // the libraries that ship (Symbols/IHost.json and Symbols/IWshRuntimeLibrary.json), which a script references without saying so.
    private static string Symbols
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RDCore.slnx")))
            {
                directory = directory.Parent;
            }

            return Path.Combine(directory!.FullName, "Symbols");
        }
    }

    private static async Task<(int Exit, string Output, string Error)> RunAsync(string[] arguments, params string[] lines)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.Directory.CreateDirectory(Root);
        fileSystem.File.WriteAllText(Script, string.Join("\r\n", ["Sub Main()", .. lines, "End Sub"]) + "\r\n");
        var output = new StringWriter();
        var error = new StringWriter();
        var runner = new BenchRunner(RuntimeEnvironmentProfile.Default, fileSystem, Substitute.For<IErrorDetailsRecorder>(), NullLoggerFactory.Instance,
            new DirectoryLibrarySource(new FileSystem(), Symbols));
        var command = new RunCommand(fileSystem, new ProjectFileLoader(fileSystem), runner, Substitute.For<IErrorDetailsRecorder>(), output, error,
            new ScriptedKeys(interactive: false), Options.Create(new AppOptions()));
        var exit = await command.ExecuteAsync([Script, .. arguments], CancellationToken.None);
        return (exit, output.ToString(), error.ToString());
    }

    private static string Lines(params string[] lines) => string.Concat(lines.Select(line => line + Environment.NewLine));

    [TestMethod]
    public async Task Echo_WritesItsArgumentsSeparatedByASpace_ABooleanAsANumber_AndNullAsNull()
    {
        var (exit, output, error) = await RunAsync([], "WScript.Echo \"ahoy\", 1.5, True, False, Null", "WScript.Echo");

        Assert.AreEqual(ProgramRunner.Completed, exit, error);
        Assert.AreEqual(Lines("ahoy 1.5 -1 0 null", string.Empty), output);
    }

    [TestMethod]
    public async Task EchoOfAnArray_RaisesTypeMismatch()
    {
        var (exit, output, error) = await RunAsync([], "WScript.Echo \"before\"", "WScript.Echo Array(1)", "WScript.Echo \"after\"");

        Assert.AreEqual(ProgramRunner.Failed, exit, error);
        Assert.AreEqual(Lines("before"), output);
        StringAssert.Contains(error, "'13':");
    }

    [TestMethod]
    [DataRow("7", 7, DisplayName = "a code")]
    [DataRow("300", 300, DisplayName = "a code larger than a byte")]
    [DataRow("-2", -2, DisplayName = "a negative code")]
    [DataRow("2.6", 3, DisplayName = "a number that is made a Long")]
    [DataRow("", 0, DisplayName = "no code")]
    public async Task Quit_EndsTheScript_AndTheProcessWithTheCodeItIsGiven(string code, int expected)
    {
        var (exit, output, error) = await RunAsync([], "WScript.Echo \"before\"", $"WScript.Quit {code}", "WScript.Echo \"after\"");

        Assert.AreEqual(expected, exit, error);
        Assert.AreEqual(Lines("before"), output);
        Assert.AreEqual(string.Empty, error);
    }

    [TestMethod]
    public async Task QuitInAProcedureTheScriptCalls_EndsTheScript()
    {
        var (exit, output, error) = await RunAsync([], "Done", "WScript.Echo \"after\"", "End Sub", "Sub Done()", "WScript.Quit 4");

        Assert.AreEqual(4, exit, error);
        Assert.AreEqual(string.Empty, output);
    }

    [TestMethod]
    public async Task Arguments_AreWhatFollowsTheScript_NamedOnesToo()
    {
        var (exit, output, error) = await RunAsync(["one", "two words", "/flag:on", "/Quiet"],
            "Set args = WScript.Arguments",
            "WScript.Echo args.Count, args.Length, args(0), args.Item(1), args(2)",
            "WScript.Echo args.Named.Count, args.Named(\"FLAG\"), args.Named.Exists(\"quiet\"), IsEmpty(args.Named(\"quiet\")), args.Named.Exists(\"none\")",
            "WScript.Echo args.Unnamed.Count, args.Unnamed(1)");

        Assert.AreEqual(ProgramRunner.Completed, exit, error);
        Assert.AreEqual(Lines("4 4 one two words /flag:on", "2 on -1 -1 0", "2 two words"), output);
    }

    [TestMethod]
    public async Task AnArgumentPastTheLast_RaisesSubscriptOutOfRange()
    {
        var (exit, _, error) = await RunAsync(["one"], "WScript.Echo WScript.Arguments(1)");

        Assert.AreEqual(ProgramRunner.Failed, exit);
        StringAssert.Contains(error, "'9':");
    }

    [TestMethod]
    public async Task TheScript_KnowsItsFile_AndItsHost()
    {
        var (exit, output, error) = await RunAsync([], "WScript.Echo WScript.ScriptName", "WScript.Echo WScript.ScriptFullName",
            "WScript.Echo WScript.Name, WScript.Version");

        Assert.AreEqual(ProgramRunner.Completed, exit, error);
        Assert.AreEqual(Lines("Backup.vbs", Script, "Windows Script Host 10.0"), output);
    }

    [TestMethod]
    public async Task TheObjectsOfTheHost_AreTheSameEachTimeTheScriptAsks()
    {
        var (exit, output, error) = await RunAsync([],
            "WScript.Echo WScript Is WScript, WScript.Arguments Is WScript.Arguments, " +
            "WScript.StdOut Is WScript.StdOut");

        Assert.AreEqual(ProgramRunner.Completed, exit, error);
        Assert.AreEqual(Lines("-1 -1 -1"), output);
    }

    [TestMethod]
    public async Task StdOut_WritesToTheOutput_AndStdErrToTheErrorOutput()
    {
        var (exit, output, error) = await RunAsync([], "WScript.StdOut.Write \"a\"", "WScript.StdOut.Write \"b\"", "WScript.StdOut.WriteLine",
            "WScript.StdOut.WriteBlankLines 1", "WScript.StdErr.WriteLine \"oops\"", "WScript.StdOut.WriteLine \"c\"");

        Assert.AreEqual(ProgramRunner.Completed, exit, error);
        Assert.AreEqual(Lines("ab", string.Empty, "c"), output);
        Assert.AreEqual(Lines("oops"), error);
    }

    [TestMethod]
    public async Task ReadingStdOut_RaisesBadFileMode()
    {
        var (exit, _, error) = await RunAsync([], "x = WScript.StdOut.ReadLine");

        Assert.AreEqual(ProgramRunner.Failed, exit);
        StringAssert.Contains(error, "'54':");
    }
}
