using RDCore.Runtime.Composer;
using RDCore.SDK.Platform.Protocol;
using static RDCore.Tests.Composer.ComposedProject;

namespace RDCore.Tests.Composer;

/// <summary>
/// A program composed from its files (<see cref="ProgramComposer.ComposeAsync"/>): its documents, the machines it makes, each in a state of its own, made
/// and run at the same time, and none reading the files again; what keeps a program from being composed, and what a disposed program or a cancelled token
/// makes of a machine asked for.
/// </summary>
[TestClass]
public sealed class ProgramComposerTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void CreateProjectFolder() => _root = NewFolder();

    [TestCleanup]
    public void DeleteProjectFolder() => Directory.Delete(_root, recursive: true);

    // the first machine is the session the program was composed in, every later one a session composed again: each runs the program's code, its class
    // and the interface the class implements, and what one invocation leaves in a machine is in that machine alone.
    [TestMethod]
    public async Task EveryMachine_RunsTheProgram_InAStateOfItsOwn()
    {
        await using var program = await ProgramComposer.ComposeAsync(Write(_root));
        await using var first = await program.CreateMachineAsync(default);
        await using var second = await program.CreateMachineAsync(default);
        await using var third = await program.CreateMachineAsync(default);

        await AssertEachMachineHasItsOwnStateAsync(first, second, third);
    }

    // machines made at the same time run at the same time, each on a thread of its own, and each counts only what it ran.
    [TestMethod]
    public async Task MachinesMadeAndRunAtOnce_EachKeepTheirOwnState()
    {
        const int machines = 4;
        const int bumps = 25;
        await using var program = await ProgramComposer.ComposeAsync(Write(_root));
        var made = await Task.WhenAll(Enumerable.Range(0, machines).Select(_ => Task.Run(async () => await program.CreateMachineAsync(default))));
        try
        {
            var counts = await Task.WhenAll(made.Select((machine, index) => Task.Run(async () =>
            {
                for (var bump = 0; bump < bumps + index; bump++)
                {
                    Assert.AreEqual(ExecutionOutcome.Completed, (await Invoke(machine, "Bump")).Outcome);
                }

                return (await Invoke(machine, "Current")).Value?.ToJsonString();
            })));

            CollectionAssert.AreEqual(Enumerable.Range(0, machines).Select(index => $"{bumps + index}").ToArray(), counts);
        }
        finally
        {
            foreach (var machine in made)
            {
                await machine.DisposeAsync();
            }
        }
    }

    // a machine is composed from what the program was composed of: a file changed after the composition reaches none of the machines.
    [TestMethod]
    public async Task AMachineMadeAfterAFileChanged_RunsTheCodeTheProgramWasComposedOf()
    {
        await using var program = await ProgramComposer.ComposeAsync(Write(_root));
        File.WriteAllText(Path.Combine(_root, "Program.bas"), Program.Replace("Count = Count + 1", "Count = Count + 100", StringComparison.Ordinal));

        await using var first = await program.CreateMachineAsync(default);
        await using var second = await program.CreateMachineAsync(default);

        foreach (var machine in new[] { first, second })
        {
            Assert.AreEqual(ExecutionOutcome.Completed, (await Invoke(machine, "Bump")).Outcome);
            AssertReturned("1", await Invoke(machine, "Current"));
        }
    }

    // a program composed from its project's folder: a document for each of its files, each with its parse, addressed as the file.
    [TestMethod]
    public async Task TheDocuments_AreTheProgramsFiles_EachParsed()
    {
        Write(_root);
        await using var program = await ProgramComposer.ComposeAsync(_root);

        CollectionAssert.AreEquivalent(Files, program.Documents.Select(document => Path.GetFileName(document.File.LocalPath)).ToArray());
        foreach (var document in program.Documents)
        {
            Assert.IsNotNull(document.Parse.SyntaxTree, document.File.ToString());
        }
    }

    // a module with a syntax error keeps the program from being composed, and the problem names it.
    [TestMethod]
    public async Task AModuleWithASyntaxError_KeepsTheProgramFromBeingComposed_AndIsNamed()
    {
        var project = Write(_root, Program.Replace("End Function", "End Fnuction", StringComparison.Ordinal));

        var refused = await Assert.ThrowsExactlyAsync<ProgramCompositionException>(() => ProgramComposer.ComposeAsync(project));

        Assert.HasCount(1, refused.Problems, refused.Message);
        Assert.StartsWith("Program:", refused.Problems[0], refused.Message);
    }

    // a disposed program makes no machine, and a machine made before it was disposed is its owner's: it still runs.
    [TestMethod]
    public async Task ADisposedProgram_MakesNoMachine_AndOneItMadeStillRuns()
    {
        var program = await ProgramComposer.ComposeAsync(Write(_root));
        await using var first = await program.CreateMachineAsync(default);
        await using var second = await program.CreateMachineAsync(default);

        await program.DisposeAsync();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () => await program.CreateMachineAsync(default));
        AssertReturned("42", await Invoke(first, "Total", """{"A":40,"B":2}"""));
        AssertReturned("42", await Invoke(second, "Total", """{"A":40,"B":2}"""));
    }

    // a token cancelled before a machine is asked for makes none.
    [TestMethod]
    public async Task ACancelledToken_MakesNoMachine()
    {
        await using var program = await ProgramComposer.ComposeAsync(Write(_root));

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await program.CreateMachineAsync(new CancellationToken(canceled: true)));
    }
}
