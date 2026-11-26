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
/// code it is given. Its code is what it says outside its procedures, run from the top, as <c>cscript.exe</c> runs it.
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
        fileSystem.File.WriteAllText(Script, string.Join("\r\n", lines) + "\r\n");
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

    // what the error says beyond its description is said on the line under it: a For Each over an array that was never given bounds says why there is
    // nothing to enumerate.
    [TestMethod]
    public async Task AnErrorThatSaysMoreThanItsDescription_IsSaidWithItUnderTheDescription()
    {
        var (exit, _, error) = await RunAsync([], "Dim items()", "Dim item", "For Each item In items", "Next");

        Assert.AreEqual(ProgramRunner.Failed, exit, error);
        var lines = error.Split(Environment.NewLine);
        Assert.Contains("'92':", lines[0], error);
        Assert.AreEqual($"  {RDCore.Runtime.Exceptions.VBForEach_ArrayNotInitialized_Verbose}", lines[1], error);
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
        var (exit, output, error) = await RunAsync([], "Done", "WScript.Echo \"after\"", "Sub Done()", "WScript.Quit 4", "End Sub");

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

    [TestMethod]
    public async Task TheScriptsCode_RunsInTheOrderItIsWritten_AroundItsProcedures()
    {
        var (exit, output, error) = await RunAsync([], "WScript.Echo 1", "Sub Two()", "WScript.Echo 2", "End Sub", "Two", "WScript.Echo 3");

        Assert.AreEqual(ProgramRunner.Completed, exit, error);
        Assert.AreEqual(Lines("1", "2", "3"), output);
    }

    [TestMethod]
    public async Task ASubMain_IsAProcedureLikeAnother_ThatTheScriptCallsOrNot()
    {
        // cscript.exe runs the script's code, and calls no procedure of its own accord.
        var (exit, output, error) = await RunAsync([], "WScript.Echo \"script\"", "Sub Main()", "WScript.Echo \"main\"", "End Sub");

        Assert.AreEqual(ProgramRunner.Completed, exit, error);
        Assert.AreEqual(Lines("script"), output);
    }

    [TestMethod]
    public async Task WhatTheScriptDeclaresOutsideItsProcedures_IsSeenByThem_TypedOrNot()
    {
        var (exit, output, error) = await RunAsync([],
            "Dim count", "Dim total As Long", "Const Greeting As String = \"hi\"", "count = 2", "total = 40",
            "Sub Show()", "WScript.Echo Greeting, count + total", "End Sub", "Show");

        Assert.AreEqual(ProgramRunner.Completed, exit, error);
        Assert.AreEqual(Lines("hi 42"), output);
    }

    // what the script's code declares anywhere in it is the file's, as a VBScript global is: a name it assigns without declaring it, a Dim in one of its
    // statements (a Dim on a line of its own is a declaration of the file already), and a ReDim of a name nothing declares. Each is read back by a procedure
    // the script calls after setting it.
    [TestMethod]
    [DataRow("count = 5", "WScript.Echo count", "5", DisplayName = "a name it assigns")]
    [DataRow("If True Then Dim label : label = \"set\"", "WScript.Echo label", "set", DisplayName = "a Dim in a single-line If")]
    [DataRow("ReDim items(2) : items(1) = \"b\"", "WScript.Echo items(1)", "b", DisplayName = "a ReDim")]
    public async Task WhatTheScriptsCodeDeclares_IsTheFilesOwn_ThatItsProceduresSee(string code, string echo, string expected)
    {
        var (exit, output, error) = await RunAsync([], code, "Show", "Sub Show()", echo, "End Sub");

        Assert.AreEqual(ProgramRunner.Completed, exit, error);
        Assert.AreEqual(Lines(expected), output);
    }

    // a script's Class and Module blocks are modules of its own: its code makes an object of the class, each with its own fields, and calls the module's
    // procedures by their name alone or by the module's; what it writes after the blocks still runs, in the order it is written.
    [TestMethod]
    public async Task TheBlocksOfAScript_AreItsClassesAndModules_ThatItsCodeUses()
    {
        var (exit, output, error) = await RunAsync([],
            "Set first = New Account", "Set other = New Account", "first.Deposit Twice(20)", "other.Deposit 1",
            "WScript.Echo first.Balance, other.Balance, Helpers.Twice(1)",
            "Class Account", "Private total", "Public Sub Deposit(amount)", "total = total + amount", "End Sub",
            "Public Property Get Balance()", "Balance = total", "End Property", "End Class",
            "Module Helpers", "Public Function Twice(value)", "Twice = value * 2", "End Function", "End Module",
            "WScript.Echo \"after\"");

        Assert.AreEqual(ProgramRunner.Completed, exit, error);
        Assert.AreEqual(Lines("40 1 2", "after"), output);
    }

    // each module of the file is defined with its own part of the file's parse: a syntax error in a block is that block's, whose load refuses the run, and
    // not the script's own module's.
    [TestMethod]
    public async Task ASyntaxErrorInABlock_IsTheBlocks_AndRefusesTheRun()
    {
        var (exit, output, error) = await RunAsync([],
            "WScript.Echo \"script\"", "Class Account", "Public Sub Deposit(amount", "End Sub", "End Class");

        Assert.AreEqual(ProgramRunner.Refused, exit, error);
        Assert.AreEqual(string.Empty, output);
        StringAssert.StartsWith(error, "Account: ");
        Assert.IsFalse(error.Contains("Backup: ", StringComparison.Ordinal), error);
    }

    [TestMethod]
    public async Task AScriptOfProceduresOnly_RunsNothing()
    {
        var (exit, output, error) = await RunAsync([], "Sub Main()", "WScript.Echo \"main\"", "End Sub");

        Assert.AreEqual(ProgramRunner.Completed, exit, error);
        Assert.AreEqual(string.Empty, output);
    }

    // a script of a project runs as a module of it: rdc run of its file starts at its own code, the other script's code does not run, and the procedures of
    // the other script are there for it to call.
    [TestMethod]
    [DataRow("Backup.vbs", "backup", "undone", DisplayName = "the first script of the project")]
    [DataRow("Restore.vbs", "restore", "saved", DisplayName = "the second script of the project")]
    public async Task EachScriptOfAProject_RunsByItsFile_AndCallsTheOtherScriptsProcedures(string file, string first, string second)
    {
        var (exit, output, error) = await RunInProjectAsync(file, ScriptsOfTools);

        Assert.AreEqual(ProgramRunner.Completed, exit, error);
        Assert.AreEqual(Lines(first, second), output);
    }

    // a module of the project that is no script runs from its file as it ran as a project of its own: from its Sub Main, which can call a script's
    // procedures, and the script's code does not run.
    [TestMethod]
    public async Task AModuleOfAProjectThatIsNoScript_RunsByItsFile_FromItsSubMain()
    {
        const string program = "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n    Debug.Print \"main\", Twice(21)\r\nEnd Sub\r\n";
        const string script = "WScript.Echo \"script\"\r\nFunction Twice(value)\r\nTwice = value * 2\r\nEnd Function\r\n";
        var (exit, output, error) = await RunInProjectAsync("Program.bas", [("Maths.vbs", script), ("Program.bas", program)]);

        Assert.AreEqual(ProgramRunner.Completed, exit, error);
        StringAssert.StartsWith(output, "main", error);
        StringAssert.Contains(output, "42", error);
        Assert.IsFalse(output.Contains("script", StringComparison.Ordinal), output);
    }

    // a module of the project with neither a script nor a Sub Main is refused as a project of its own is: it has no entry point, whatever the other modules
    // declare.
    [TestMethod]
    public async Task AModuleOfAProjectWithNeitherAScriptNorASubMain_IsRefused_AsItIsOnItsOwn()
    {
        const string helpers = "Attribute VB_Name = \"Helpers\"\r\nPublic Sub Helper()\r\nEnd Sub\r\n";
        var inProject = await RunInProjectAsync("Helpers.bas",
            [.. ScriptsOfTools, ("Program.bas", "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nEnd Sub\r\n"), ("Helpers.bas", helpers)]);
        var onItsOwn = await RunInProjectAsync("Helpers.bas", [("Helpers.bas", helpers)], listed: false);

        Assert.AreEqual(ProgramRunner.Refused, inProject.Exit, inProject.Error);
        StringAssert.Contains(inProject.Error, ProgramRunner.NoEntryPoint.ToString());
        Assert.AreEqual(onItsOwn.Error, inProject.Error, "the refusal is the one the module's file gets as a project of its own");
        Assert.AreEqual(string.Empty, inProject.Output);
    }

    // two scripts of a project: each calls a procedure of the other's.
    private static readonly (string File, string Text)[] ScriptsOfTools =
    [
        ("Backup.vbs", "WScript.Echo \"backup\"\r\nUndo\r\nSub Save()\r\nWScript.Echo \"saved\"\r\nEnd Sub\r\n"),
        ("Restore.vbs", "WScript.Echo \"restore\"\r\nSave\r\nSub Undo()\r\nWScript.Echo \"undone\"\r\nEnd Sub\r\n"),
    ];

    // rdc run of one file of a folder: the folder's .rdproj lists every file (listed), or there is none and the file is a project of its own.
    private static async Task<(int Exit, string Output, string Error)> RunInProjectAsync(string file, (string File, string Text)[] files, bool listed = true)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.Directory.CreateDirectory(Root);
        foreach (var (name, text) in files)
        {
            fileSystem.File.WriteAllText(Path.Combine(Root, name), text);
        }

        if (listed)
        {
            var modules = string.Join(", ", files.Select(each => $$"""{ "RelativeUri": "{{each.File}}" }"""));
            fileSystem.File.WriteAllText(Path.Combine(Root, ".rdproj"),
                $$"""{ "Version": "0.1.0", "ProjectInfo": { "Name": "Tools", "Modules": [ {{modules}} ] } }""");
        }

        var output = new StringWriter();
        var error = new StringWriter();
        var runner = new BenchRunner(RuntimeEnvironmentProfile.Default, fileSystem, Substitute.For<IErrorDetailsRecorder>(), NullLoggerFactory.Instance,
            new DirectoryLibrarySource(new FileSystem(), Symbols));
        var command = new RunCommand(fileSystem, new ProjectFileLoader(fileSystem), runner, Substitute.For<IErrorDetailsRecorder>(), output, error,
            new ScriptedKeys(interactive: false), Options.Create(new AppOptions()));
        var exit = await command.ExecuteAsync([Path.Combine(Root, file)], CancellationToken.None);
        return (exit, output.ToString(), error.ToString());
    }
}
