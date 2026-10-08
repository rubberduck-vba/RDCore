using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>rdcore/host/discard</c> inside the environment host, for a program run the way the shell runs one: what a program made does not outlive it once the
/// shell clears it, and what a line typed at the prompt made does not go until then.
/// </summary>
[TestClass]
public sealed class HostDiscardHandlerTests
{
    private static readonly (int Number, string Statement)[] FizzBuzz =
    [
        (10, "N = 30"),
        (20, "FOR I = 1 TO N"),
        (30, "PRINT I"),
        (40, "NEXT"),
    ];

    private static readonly (int Number, string Statement)[] Counter =
    [
        (10, "N = N + 1"),
        (20, "PRINT N"),
    ];

    private static readonly (int Number, string Statement)[] PrintsN = [(10, "PRINT N")];

    private static string[] Printed(ExecuteSessionResult result) => [.. result.Output.Select(line => line.Trim())];

    [TestMethod]
    public async Task TheVariablesOfAProgram_AndTheirStorage_GoWithIt()
    {
        var host = ShellHost.Compose();
        var before = host.Memory;
        Assert.AreEqual(ExecutionOutcome.Completed, (await host.RunAsync(FizzBuzz)).Outcome);
        Assert.IsTrue(host.Declares("N"));
        Assert.IsTrue(host.Declares("I"));
        Assert.IsGreaterThan(before.AllocatedBytes, host.Memory.AllocatedBytes);

        var discarded = await host.DiscardAsync();

        Assert.AreEqual(3, discarded.Discarded, "N and I, and Main, the procedure the program's lines are");
        Assert.IsFalse(host.Declares("N"));
        Assert.IsFalse(host.Declares("I"));
        Assert.AreEqual(before.AllocatedBytes, host.Memory.AllocatedBytes);
        Assert.AreEqual(0, host.Memory.FreeBytes);
        Assert.AreEqual(host.Memory.AllocatedBytes, host.Memory.CommittedBytes);
    }

    [TestMethod]
    public async Task WithoutADiscard_AVariableOfTheProgramBefore_IsStillThereForTheNext()
    {
        var host = ShellHost.Compose();
        await host.RunAsync(FizzBuzz);

        var next = await host.RunAsync(PrintsN);

        CollectionAssert.AreEqual(new[] { "30" }, Printed(next), "the old program's N is read by the new program");
    }

    [TestMethod]
    public async Task AfterADiscard_AVariableOfTheProgramBefore_IsNotThereForTheNext()
    {
        var host = ShellHost.Compose();
        await host.RunAsync(FizzBuzz);
        await host.DiscardAsync();

        var next = await host.RunAsync(PrintsN);

        Assert.AreEqual(ExecutionOutcome.Completed, next.Outcome);
        CollectionAssert.DoesNotContain(Printed(next), "30");
    }

    [TestMethod]
    public async Task ARunAfterADiscard_StartsFromNothing_ARunWithoutOne_StartsFromWhatTheLastLeft()
    {
        var host = ShellHost.Compose();

        var first = await host.RunAsync(Counter);
        var second = await host.RunAsync(Counter);
        await host.DiscardAsync();
        var third = await host.RunAsync(Counter);

        CollectionAssert.AreEqual(new[] { "1" }, Printed(first));
        CollectionAssert.AreEqual(new[] { "2" }, Printed(second));
        CollectionAssert.AreEqual(new[] { "1" }, Printed(third));
    }

    [TestMethod]
    public async Task ALineTypedAtThePrompt_ReadsWhatTheLinesBeforeItAssigned_UntilTheProgramIsDiscarded()
    {
        var host = ShellHost.Compose();
        await host.ImmediateAsync([], "A = 40");
        await host.ImmediateAsync([], "A = A + 2");

        var printed = await host.ImmediateAsync([], "PRINT A");
        await host.DiscardAsync();
        var cleared = await host.ImmediateAsync([], "PRINT A");

        CollectionAssert.AreEqual(new[] { "42" }, Printed(printed));
        CollectionAssert.DoesNotContain(Printed(cleared), "42");
    }

    [TestMethod]
    public async Task ADiscard_OfAModuleThatIsNotThere_DiscardsNothing()
    {
        var host = ShellHost.Compose();
        await host.RunAsync(FizzBuzz);

        var discarded = await host.DiscardAsync("Nowhere");

        Assert.AreEqual(0, discarded.Discarded);
        Assert.IsTrue(host.Declares("N"));
    }

    [TestMethod]
    public async Task ADiscard_BeforeAnythingRan_DiscardsNothing()
    {
        var host = ShellHost.Compose();

        Assert.AreEqual(0, (await host.DiscardAsync()).Discarded);
    }

    [TestMethod]
    public async Task ADiscard_RepeatedAsOftenAsTheProgramIsRun_NeverGrowsTheMemory()
    {
        var host = ShellHost.Compose();
        await host.RunAsync(FizzBuzz);
        await host.DiscardAsync();
        var settled = host.Memory;

        for (var cycle = 0; cycle < 20; cycle++)
        {
            await host.RunAsync(FizzBuzz);
            await host.DiscardAsync();
        }

        Assert.AreEqual(settled, host.Memory);
    }
}
