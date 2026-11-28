using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.App.Bench;
using RDCore.CLI.App.Commands;
using RDCore.CLI.ConsoleIO;
using RDCore.CLI.ConsoleIO.Model;
using RDCore.Runtime.Hosting;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Libraries;
using System.IO.Abstractions;
using static RDCore.Tests.Composer.ComposedProject;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>rdc compose</c>: the composition of a workspace written where a host loads it, and none written of a workspace a module of which does not load.
/// </summary>
[TestClass]
public sealed class ComposeCommandTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void CreateProjectFolder() => _root = NewFolder();

    [TestCleanup]
    public void DeleteProjectFolder() => Directory.Delete(_root, recursive: true);

    private static (ComposeCommand Command, List<(MessageKind Kind, string Body)> Messages) Command()
    {
        var fileSystem = new FileSystem();
        var messages = new List<(MessageKind Kind, string Body)>();
        var writer = Substitute.For<IConsoleMessageWriter>();
        writer.When(each => each.WriteMessage(Arg.Any<ConsoleMessageBuilder>())).Do(call =>
        {
            var message = call.Arg<ConsoleMessageBuilder>();
            messages.Add((message.Kind, string.Concat(message.Parts.OfType<ConsoleMessageBodyPart>().Select(part => part.Body))));
        });
        var libraries = new DirectoryLibrarySource(fileSystem, string.Empty);
        var runner = new BenchRunner(
            RuntimeEnvironmentProfile.Default, fileSystem, Substitute.For<IErrorDetailsRecorder>(), NullLoggerFactory.Instance, libraries);
        return (new ComposeCommand(writer, fileSystem, runner, libraries, NullLoggerFactory.Instance), messages);
    }

    // written to bin/<configuration>/ of the workspace, named as its project, the composition loads with no parser, and a machine of it runs the program.
    [TestMethod]
    public async Task TheComposition_IsWrittenInTheWorkspacesBinFolder_AndAHostRunsIt()
    {
        Write(_root);
        var (command, messages) = Command();

        Assert.AreEqual(0, await command.ExecuteAsync([_root, "--configuration", "Release"], CancellationToken.None), string.Join("; ", messages));

        var file = Path.Combine(_root, "bin", "Release", "Composed" + ComposedProgramFile.Extension);
        Assert.IsTrue(File.Exists(file), string.Join("; ", messages));
        await using var program = await ComposedProgramFile.LoadAsync(file);
        await using var machine = await program.CreateMachineAsync(default);
        AssertReturned("42", await Invoke(machine, "Total", """{"A":40,"B":2}"""));
    }

    // a module that does not parse keeps the workspace from being composed: its problem is said, and nothing is written.
    [TestMethod]
    public async Task AModuleWithASyntaxError_IsSaid_AndNoCompositionIsWritten()
    {
        Write(_root, Program.Replace("End Function", "End Fnuction", StringComparison.Ordinal));
        var (command, messages) = Command();

        Assert.AreEqual(1, await command.ExecuteAsync([_root], CancellationToken.None));

        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "bin")), "nothing is written of a workspace that does not compose");
        Assert.IsTrue(messages.Any(message => message.Kind is MessageKind.Error && message.Body.StartsWith("Program:", StringComparison.Ordinal)),
            string.Join("; ", messages));
    }
}
