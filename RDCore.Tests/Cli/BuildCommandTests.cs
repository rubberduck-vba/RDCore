using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.App.Bench;
using RDCore.CLI.App.Build;
using RDCore.CLI.App.Commands;
using RDCore.Runtime.Compiled;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Runtime;
using RDCore.SDK.Services.VerboseMessages;
using RDCore.SDK.Workspace;
using System.IO.Abstractions.TestingHelpers;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Text;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>rdc build</c>: a workspace built into a program (its assembly, its debugging information, its runtime configuration, its executable and the runtime it
/// runs on), and the program run from its assembly as the interpreter runs the workspace.
/// </summary>
[TestClass]
public sealed class BuildCommandTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-build-ws");
    private static readonly string Runtime = Path.Combine(Path.GetTempPath(), "rdcore-build-runtime");
    private static readonly string Host = Path.Combine(Path.GetTempPath(), "rdcore-build-packs", "apphost.exe");
    private static readonly string Output = Path.Combine(Path.GetTempPath(), "rdcore-build-out");

    private static string Module(string name, params string[] body) => $"Attribute VB_Name = \"{name}\"\r\nOption Explicit\r\n{string.Join("\r\n", body)}\r\n";

    private static readonly string Program = Module("Program",
        "Public Sub Main()",
        "    Debug.Print Twice(21)",
        "    Debug.Print Total(100)",
        "End Sub",
        "Private Function Twice(ByVal N As Long) As Long",
        "    Twice = N * 2",
        "End Function",
        "Private Function Total(ByVal Count As Long) As Double",
        "    Dim I As Long",
        "    For I = 1 To Count",
        "        Total = Total + I",
        "    Next",
        "End Function");

    // an application host as small as one can be: the placeholder, between what comes before it and after it.
    private static readonly byte[] FakeHost = [.. "MZ-before-"u8, .. AppHost.Placeholder, .. new byte[AppHost.SlotLength - AppHost.Placeholder.Length], .. "-after"u8];

    private static MockFileSystem Files(params (string Path, string Source)[] modules)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.Directory.CreateDirectory(Root);
        foreach (var (path, source) in modules)
        {
            fileSystem.File.WriteAllText(Path.Combine(Root, path), source);
        }

        // the runtime a program runs on: an assembly, its resources in French, and rdc's runtime configuration.
        fileSystem.Directory.CreateDirectory(Path.Combine(Runtime, "fr"));
        fileSystem.File.WriteAllText(Path.Combine(Runtime, "RDCore.Runtime.dll"), "the runtime");
        fileSystem.File.WriteAllText(Path.Combine(Runtime, "fr", "rdc.resources.dll"), "les ressources");
        fileSystem.File.WriteAllText(Path.Combine(Runtime, "rdc.runtimeconfig.json"), "{ \"runtimeOptions\": { \"tfm\": \"net10.0\" } }");
        fileSystem.Directory.CreateDirectory(Path.GetDirectoryName(Host)!);
        fileSystem.File.WriteAllBytes(Host, FakeHost);
        return fileSystem;
    }

    private static (BuildCommand Command, List<(MessageKind Kind, string Text)> Messages) Command(MockFileSystem fileSystem, bool hasHost = true)
    {
        var messages = new List<(MessageKind, string)>();
        var writer = Substitute.For<IConsoleMessageWriter>();
        writer.When(w => w.WriteMessage(Arg.Any<ConsoleMessageBuilder>())).Do(call =>
        {
            var message = call.Arg<ConsoleMessageBuilder>();
            messages.Add((message.Kind, string.Concat(message.Parts.OfType<ConsoleMessageBodyPart>().Select(part => part.Body))));
        });
        var runner = new BenchRunner(RuntimeEnvironmentProfile.Default, fileSystem, Substitute.For<IVerboseMessageBuilder>(), NullLoggerFactory.Instance,
            new RDCore.SDK.Runtime.Libraries.DirectoryLibrarySource(fileSystem, string.Empty));
        return (new BuildCommand(writer, fileSystem, new ProjectFileLoader(fileSystem), runner, new ProgramOutput(fileSystem, Runtime),
            _ => hasHost ? Host : null), messages);
    }

    [TestMethod]
    public void TheApplicationHost_IsPatchedWithThePathOfTheAssembly_TheRestOfThePlaceholderZeros()
    {
        var patched = AppHost.Patch(FakeHost, "Hello.dll");

        Assert.HasCount(FakeHost.Length, patched);
        var at = "MZ-before-"u8.Length;
        Assert.AreEqual("Hello.dll", Encoding.UTF8.GetString(patched, at, "Hello.dll".Length));
        Assert.IsTrue(patched.AsSpan(at + "Hello.dll".Length, AppHost.Placeholder.Length - "Hello.dll".Length).IndexOfAnyExcept((byte)0) < 0, "the rest of the placeholder is zeros");
        Assert.IsTrue(patched.AsSpan(patched.Length - "-after"u8.Length).SequenceEqual("-after"u8), "what follows the slot is the host's");
    }

    [TestMethod]
    public void AHostWithoutAPlaceholder_IsRefused()
        => Assert.ThrowsExactly<InvalidDataException>(() => AppHost.Patch("MZ no placeholder"u8, "Hello.dll"));

    [TestMethod]
    public void APathLongerThanTheSlot_IsRefused()
        => Assert.ThrowsExactly<ArgumentException>(() => AppHost.Patch(FakeHost, new string('x', AppHost.SlotLength + 1)));

    [TestMethod]
    public async Task AWorkspace_IsBuiltIntoItsAssembly_ItsDebuggingInformation_ItsExecutable_AndTheRuntime()
    {
        CompiledExecutionTests.RequirePayloads();
        var fileSystem = Files(("Program.bas", Program));
        var (command, messages) = Command(fileSystem);

        var exit = await command.ExecuteAsync([Root, "--name", "Hello", "--output", Output, "--runtime", "win-x64"], CancellationToken.None);

        Assert.AreEqual(0, exit, string.Join("; ", messages.Select(message => message.Text)));
        foreach (var file in new[] { "Hello.dll", "Hello.pdb", "Hello.exe", "Hello.runtimeconfig.json", "RDCore.Runtime.dll", Path.Combine("fr", "rdc.resources.dll") })
        {
            Assert.IsTrue(fileSystem.File.Exists(Path.Combine(Output, file)), file);
        }

        Assert.AreEqual(fileSystem.File.ReadAllText(Path.Combine(Runtime, "rdc.runtimeconfig.json")), fileSystem.File.ReadAllText(Path.Combine(Output, "Hello.runtimeconfig.json")));
        CollectionAssert.AreEqual(AppHost.Patch(FakeHost, "Hello.dll"), fileSystem.File.ReadAllBytes(Path.Combine(Output, "Hello.exe")));
        Assert.Contains(message => message.Kind == MessageKind.Information && message.Text.Contains("3 ", StringComparison.Ordinal), messages);
    }

    [TestMethod]
    public async Task TheBuiltAssembly_RunsTheProgram_ItsProceduresTheCodeTheyWereBuiltTo()
    {
        CompiledExecutionTests.RequirePayloads();
        var fileSystem = Files(("Program.bas", Program));
        var (command, messages) = Command(fileSystem);
        Assert.AreEqual(0, await command.ExecuteAsync([Root, "--name", "Hello", "--output", Output], CancellationToken.None),
            string.Join("; ", messages.Select(message => message.Text)));

        var context = new AssemblyLoadContext("built program", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(new MemoryStream(fileSystem.File.ReadAllBytes(Path.Combine(Output, "Hello.dll"))));
            Assert.IsNotNull(assembly.EntryPoint, "the assembly is an executable one");
            Assert.AreEqual("Main", assembly.EntryPoint.Name);

            var output = new StringWriter();
            var error = new StringWriter();
            var exit = await BuiltProgram.RunAsync(assembly, [], output, error, CancellationToken.None);

            Assert.AreEqual(0, exit, error.ToString());
            CollectionAssert.AreEqual(new[] { "42", "5050" }, output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()).ToArray());
        }
        finally
        {
            context.Unload();
        }
    }

    [TestMethod]
    public async Task TheDebuggingInformation_MapsTheCodeOfAProcedure_ToTheLinesOfItsModule()
    {
        CompiledExecutionTests.RequirePayloads();
        var fileSystem = Files(("Program.bas", Program));
        var (command, _) = Command(fileSystem);
        Assert.AreEqual(0, await command.ExecuteAsync([Root, "--name", "Hello", "--output", Output], CancellationToken.None));

        using var provider = MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(fileSystem.File.ReadAllBytes(Path.Combine(Output, "Hello.pdb"))));
        var pdb = provider.GetMetadataReader();
        var document = pdb.GetDocument(Assert.ContainsSingle(pdb.Documents));
        Assert.AreEqual(Path.Combine(Root, "Program.bas"), pdb.GetString(document.Name));
        Assert.AreEqual(ProgramAssemblyBuilder.Language, pdb.GetGuid(document.Language));
        CollectionAssert.AreEqual(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Program)), pdb.GetBlobBytes(document.Hash));

        // the line of "Twice = N * 2", one-based, is among the lines the code of the procedures is mapped to.
        var lines = pdb.MethodDebugInformation.SelectMany(handle => pdb.GetMethodDebugInformation(handle).GetSequencePoints())
            .Where(point => !point.IsHidden).Select(point => point.StartLine).ToHashSet();
        var twice = Program.Split("\r\n").ToList().IndexOf("    Twice = N * 2") + 1;
        Assert.Contains(twice, lines);

        using var pe = new PEReader(new MemoryStream(fileSystem.File.ReadAllBytes(Path.Combine(Output, "Hello.dll"))));
        var codeView = pe.ReadDebugDirectory().Single(entry => entry.Type == DebugDirectoryEntryType.CodeView);
        Assert.AreEqual("Hello.pdb", pe.ReadCodeViewDebugDirectoryData(codeView).Path);
    }

    [TestMethod]
    public async Task WithNoApplicationHostForThePlatform_TheProgramIsWrittenWithoutAnExecutable_AndSaysHowToRunIt()
    {
        CompiledExecutionTests.RequirePayloads();
        var fileSystem = Files(("Program.bas", Program));
        var (command, messages) = Command(fileSystem, hasHost: false);

        Assert.AreEqual(0, await command.ExecuteAsync([Root, "--name", "Hello", "--output", Output, "--runtime", "linux-x64"], CancellationToken.None));

        Assert.IsTrue(fileSystem.File.Exists(Path.Combine(Output, "Hello.dll")));
        Assert.IsFalse(fileSystem.File.Exists(Path.Combine(Output, "Hello")));
        Assert.Contains(message => message.Kind == MessageKind.Warning && message.Text.Contains("dotnet Hello.dll", StringComparison.Ordinal), messages);
    }

    [TestMethod]
    public async Task AWorkspaceWithoutSubMain_IsNotBuilt()
    {
        var fileSystem = Files(("Program.bas", Module("Program", "Public Sub NotMain()", "End Sub")));
        var (command, messages) = Command(fileSystem);

        Assert.AreEqual(1, await command.ExecuteAsync([Root, "--name", "Hello", "--output", Output], CancellationToken.None));

        Assert.Contains(message => message.Kind == MessageKind.Error, messages);
        Assert.IsFalse(fileSystem.File.Exists(Path.Combine(Output, "Hello.dll")));
    }

    // ---- rdc run ----

    private static async Task<(int Exit, string[] Output, string Error)> RunAsync(MockFileSystem fileSystem, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var runner = new BenchRunner(RuntimeEnvironmentProfile.Default, fileSystem, Substitute.For<IVerboseMessageBuilder>(), NullLoggerFactory.Instance,
            new RDCore.SDK.Runtime.Libraries.DirectoryLibrarySource(fileSystem, string.Empty));
        var command = new RunCommand(fileSystem, new ProjectFileLoader(fileSystem), runner, Substitute.For<IVerboseMessageBuilder>(), output, error);
        var exit = await command.ExecuteAsync(args, CancellationToken.None);
        return (exit, output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()).ToArray(), error.ToString());
    }

    [TestMethod]
    public async Task Run_RunsTheEntryPoint_AsTheBuiltProgramDoes()
    {
        CompiledExecutionTests.RequirePayloads();
        var fileSystem = Files(("Program.bas", Program));

        var run = await RunAsync(fileSystem, Root);

        Assert.AreEqual(0, run.Exit, run.Error);
        CollectionAssert.AreEqual(new[] { "42", "5050" }, run.Output);
        Assert.AreEqual(string.Empty, run.Error);
    }

    [TestMethod]
    public async Task Run_OfAProgramARunTimeErrorStops_ExitsWith1_TheErrorOnStandardError()
    {
        var fileSystem = Files(("Program.bas", Module("Program",
            "Public Sub Main()",
            "    Debug.Print \"before\"",
            "    Err.Raise 5",
            "End Sub")));

        var run = await RunAsync(fileSystem, Root);

        Assert.AreEqual(ProgramRunner.Failed, run.Exit);
        CollectionAssert.AreEqual(new[] { "before" }, run.Output, "what it printed before the error is written");
        Assert.Contains("'5'", run.Error, "the VBA error number is in the message");
        Assert.Contains("Main:5", run.Error, "where it stopped: the line of the Err.Raise");
    }

    // an exit code is a byte on Linux: an error number is said on standard error, and the exit code is 1 whatever it is.
    private static readonly string RaisesAnObjectError = Module("Program",
        "Public Sub Main()",
        "    Err.Raise vbObjectError + 513, , \"Out of widgets\"",
        "End Sub");

    [TestMethod]
    public async Task Run_OfAnErrorWhoseNumberIsNoByte_ExitsWith1_ItsNumberAndDescriptionOnStandardError()
    {
        var fileSystem = Files(("Program.bas", RaisesAnObjectError));

        var run = await RunAsync(fileSystem, Root);

        Assert.AreEqual(ProgramRunner.Failed, run.Exit);
        Assert.Contains("'-2147220991'", run.Error);
        Assert.Contains("Out of widgets", run.Error);
    }

    [TestMethod]
    public async Task TheBuiltProgram_OfAnErrorWhoseNumberIsNoByte_ExitsWith1_AsRunDoes()
    {
        CompiledExecutionTests.RequirePayloads();
        var fileSystem = Files(("Program.bas", RaisesAnObjectError));
        var (command, messages) = Command(fileSystem);
        Assert.AreEqual(0, await command.ExecuteAsync([Root, "--name", "Hello", "--output", Output], CancellationToken.None),
            string.Join("; ", messages.Select(message => message.Text)));

        var context = new AssemblyLoadContext("built program", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(new MemoryStream(fileSystem.File.ReadAllBytes(Path.Combine(Output, "Hello.dll"))));
            var error = new StringWriter();

            var exit = await BuiltProgram.RunAsync(assembly, [], new StringWriter(), error, CancellationToken.None);

            Assert.AreEqual(ProgramRunner.Failed, exit);
            Assert.Contains("'-2147220991'", error.ToString());
            Assert.Contains("Out of widgets", error.ToString());
        }
        finally
        {
            context.Unload();
        }
    }

    private static readonly string Echo = Module("Program",
        "Public Sub Main(ParamArray Args())",
        "    Dim I As Long",
        "    Debug.Print UBound(Args)",
        "    For I = 0 To UBound(Args)",
        "        Debug.Print TypeName(Args(I)) & \":\" & Args(I)",
        "    Next",
        "    Debug.Print \"[\" & Command$ & \"]\"",
        "End Sub");

    [TestMethod]
    public async Task Run_PassesWhatFollowsTheSeparator_ToTheParamArrayOfMain_AndToCommand()
    {
        var fileSystem = Files(("Program.bas", Echo));

        var run = await RunAsync(fileSystem, Root, "--", "one", "two words", "--unattended");

        Assert.AreEqual(0, run.Exit, run.Error);
        CollectionAssert.AreEqual(new[] { "2", "String:one", "String:two words", "String:--unattended", "[one \"two words\" --unattended]" }, run.Output);
    }

    [TestMethod]
    public async Task Run_WithNoArguments_GivesMainAParamArrayWithNoElements_AndAnEmptyCommand()
    {
        var fileSystem = Files(("Program.bas", Echo));

        var run = await RunAsync(fileSystem, Root);

        Assert.AreEqual(0, run.Exit, run.Error);
        CollectionAssert.AreEqual(new[] { "-1", "[]" }, run.Output);
    }

    [TestMethod]
    public void TheCommandLine_QuotesAnArgumentThatIsEmptyOrHoldsASpace()
        => Assert.AreEqual("a \"b c\" \"\" \"d\te\"", ProgramRunner.CommandLineOf(["a", "b c", "", "d\te"]));

    // what is written, and whether it was written while the program ran: the program runs on a thread of its own, and its result is written once it is over.
    private sealed class ThreadRecordingWriter : StringWriter
    {
        public List<(string Line, bool WhileRunning)> Lines { get; } = [];

        public override void WriteLine(string? value) => Lines.Add((value ?? string.Empty, Thread.CurrentThread.Name == "RDCore program"));

        public override Task WriteLineAsync(string? value)
        {
            WriteLine(value);
            return Task.CompletedTask;
        }
    }

    [TestMethod]
    public async Task Run_WritesEachLine_AsTheProgramPrintsIt_AndTheLineItLeavesOpen_WhenItEnds()
    {
        var fileSystem = Files(("Program.bas", Module("Program",
            "Public Sub Main()",
            "    Debug.Print \"first\"",
            "    Debug.Print \"second\"",
            "    Debug.Print \"open\";",
            "End Sub")));
        var output = new ThreadRecordingWriter();
        var runner = new BenchRunner(RuntimeEnvironmentProfile.Default, fileSystem, Substitute.For<IVerboseMessageBuilder>(), NullLoggerFactory.Instance,
            new RDCore.SDK.Runtime.Libraries.DirectoryLibrarySource(fileSystem, string.Empty));
        var command = new RunCommand(fileSystem, new ProjectFileLoader(fileSystem), runner, Substitute.For<IVerboseMessageBuilder>(), output, new StringWriter());

        Assert.AreEqual(0, await command.ExecuteAsync([Root], CancellationToken.None));

        CollectionAssert.AreEqual(new[] { ("first", true), ("second", true), ("open", false) }, output.Lines.Select(line => (line.Line.Trim(), line.WhileRunning)).ToArray());
    }

    [TestMethod]
    public async Task TheBuiltProgram_PassesTheArgumentsItIsStartedWith_AsRunDoes()
    {
        CompiledExecutionTests.RequirePayloads();
        var fileSystem = Files(("Program.bas", Echo));
        var (command, messages) = Command(fileSystem);
        Assert.AreEqual(0, await command.ExecuteAsync([Root, "--name", "Hello", "--output", Output], CancellationToken.None),
            string.Join("; ", messages.Select(message => message.Text)));

        var context = new AssemblyLoadContext("built program", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(new MemoryStream(fileSystem.File.ReadAllBytes(Path.Combine(Output, "Hello.dll"))));
            var output = new StringWriter();
            var error = new StringWriter();

            var exit = await BuiltProgram.RunAsync(assembly, ["one", "two words"], output, error, CancellationToken.None);

            Assert.AreEqual(0, exit, error.ToString());
            CollectionAssert.AreEqual(new[] { "1", "String:one", "String:two words", "[one \"two words\"]" },
                output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()).ToArray());
        }
        finally
        {
            context.Unload();
        }
    }

    [TestMethod]
    public async Task Run_OfAWorkspaceWithoutSubMain_ExitsWith2()
    {
        var fileSystem = Files(("Program.bas", Module("Program", "Public Sub NotMain()", "End Sub")));

        var run = await RunAsync(fileSystem, Root);

        Assert.AreEqual(ProgramRunner.Refused, run.Exit);
        Assert.AreNotEqual(string.Empty, run.Error);
    }

    [TestMethod]
    public async Task Run_OfAWorkspaceWithTwoSubMain_ExitsWith2_NamingTheModules()
    {
        var fileSystem = Files(("One.bas", Module("One", "Public Sub Main()", "End Sub")), ("Two.bas", Module("Two", "Public Sub Main()", "End Sub")));

        var run = await RunAsync(fileSystem, Root);

        Assert.AreEqual(ProgramRunner.Refused, run.Exit);
        Assert.Contains("One", run.Error);
        Assert.Contains("Two", run.Error);
    }

    [TestMethod]
    public async Task Run_OfAModuleThatDoesNotParse_ExitsWith2()
    {
        var fileSystem = Files(("Program.bas", Module("Program", "Public Sub Main()", "    If Then", "End Sub")));

        var run = await RunAsync(fileSystem, Root);

        Assert.AreEqual(ProgramRunner.Refused, run.Exit);
        Assert.IsEmpty(run.Output);
    }

    [TestMethod]
    public void TheEntryPointTheProjectNames_AndALibrary_AreReadFromItsBuildSection()
    {
        var project = System.Text.Json.JsonSerializer.Deserialize<RDCoreProject>("""{ "Name": "P", "build": { "entryPoint": "Two.Main", "outputType": "Library" } }""")!;

        var settings = ProjectBuildSettings.Of(project);

        Assert.AreEqual("Two.Main", settings.EntryPoint);
        Assert.IsTrue(settings.IsLibrary);
        Assert.AreEqual(new ProjectBuildSettings(null, false), ProjectBuildSettings.Of(new RDCoreProject { Name = "P" }));
    }

    [TestMethod]
    public async Task AProgramNamedForAnAssemblyOfTheRuntime_IsNotBuilt()
    {
        var fileSystem = Files(("Program.bas", Program));
        var (command, messages) = Command(fileSystem);

        Assert.AreEqual(1, await command.ExecuteAsync([Root, "--name", "RDCore.Runtime", "--output", Output], CancellationToken.None));

        Assert.Contains(message => message.Kind == MessageKind.Error && message.Text.Contains("RDCore.Runtime", StringComparison.Ordinal), messages);
    }
}
